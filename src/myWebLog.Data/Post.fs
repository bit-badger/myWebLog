module MyWebLog.Data.Post

open FSharp.Interop.Dynamic
open MyWebLog.Entities
open Rethink
open RethinkDb.Driver.Ast
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to select all published posts for a web log
let private publishedPosts (webLogId : string)=
  r.Table(Table.Post)
    .GetAll(r.Array(webLogId, PostStatus.Published)).OptArg("index", "WebLogAndStatus")

/// Shorthand to sort posts by published date, slice for the given page, and return a list
let private toPostList conn pageNbr nbrPerPage (filter : ReqlExpr) =
  filter
    .OrderBy(r.Desc("PublishedOn"))
    .Slice((pageNbr - 1) * nbrPerPage, pageNbr * nbrPerPage)
    .RunListAsync<Post>(conn)
  |> await
  |> Seq.toList

/// Shorthand to get a newer or older post
let private adjacentPost conn post (theFilter : ReqlExpr -> obj) (sort : obj) =
  (publishedPosts post.WebLogId)
    .Filter(theFilter)
    .OrderBy(sort)
    .Limit(1)
    .RunListAsync<Post>(conn)
  |> await
  |> Seq.tryHead

/// Find a newer post
let private newerPost conn post theFilter = adjacentPost conn post theFilter <| r.Asc "publishedOn"

/// Find an older post
let private olderPost conn post theFilter = adjacentPost conn post theFilter <| r.Desc "publishedOn"

/// Get a page of published posts
let findPageOfPublishedPosts conn webLogId pageNbr nbrPerPage =
  publishedPosts webLogId
  |> toPostList conn pageNbr nbrPerPage

/// Get a page of published posts assigned to a given category
let findPageOfCategorizedPosts conn webLogId (categoryId : string) pageNbr nbrPerPage =
  (publishedPosts webLogId)
    .Filter(ReqlFunction1(fun p -> upcast p.["CategoryIds"].Contains(categoryId)))
  |> toPostList conn pageNbr nbrPerPage

/// Get a page of published posts tagged with a given tag
let findPageOfTaggedPosts conn webLogId (tag : string) pageNbr nbrPerPage =
  (publishedPosts webLogId)
    .Filter(ReqlFunction1(fun p -> upcast p.["Tags"].Contains(tag)))
  |> toPostList conn pageNbr nbrPerPage

/// Try to get the next newest post from the given post
let tryFindNewerPost conn post = newerPost conn post (fun p -> upcast p.["PublishedOn"].Gt(post.PublishedOn))
 
/// Try to get the next newest post assigned to the given category
let tryFindNewerCategorizedPost conn (categoryId : string) post =
  newerPost conn post (fun p -> upcast p.["PublishedOn"].Gt(post.PublishedOn)
                                        .And(p.["CategoryIds"].Contains(categoryId)))
 
/// Try to get the next newest tagged post from the given tagged post
let tryFindNewerTaggedPost conn (tag : string) post =
  newerPost conn post (fun p -> upcast p.["PublishedOn"].Gt(post.PublishedOn).And(p.["Tags"].Contains(tag)))
 
/// Try to get the next oldest post from the given post
let tryFindOlderPost conn post = olderPost conn post (fun p -> upcast p.["PublishedOn"].Lt(post.PublishedOn))

/// Try to get the next oldest post assigned to the given category
let tryFindOlderCategorizedPost conn (categoryId : string) post =
  olderPost conn post (fun p -> upcast p.["PublishedOn"].Lt(post.PublishedOn)
                                        .And(p.["CategoryIds"].Contains(categoryId)))

/// Try to get the next oldest tagged post from the given tagged post
let tryFindOlderTaggedPost conn (tag : string) post =
  olderPost conn post (fun p -> upcast p.["PublishedOn"].Lt(post.PublishedOn).And(p.["Tags"].Contains(tag)))

/// Get a page of all posts in all statuses
let findPageOfAllPosts conn (webLogId : string) pageNbr nbrPerPage =
  // FIXME: sort unpublished posts by their last updated date
  r.Table(Table.Post)
    .GetAll(webLogId).OptArg("index", "WebLogId")
    .OrderBy(r.Desc("PublishedOn"))
    .Slice((pageNbr - 1) * nbrPerPage, pageNbr * nbrPerPage)
    .RunListAsync<Post>(conn)
  |> await
  |> Seq.toList

/// Try to find a post by its Id and web log Id
let tryFindPost conn webLogId postId : Post option =
  match r.Table(Table.Post)
          .Get(postId)
          .Filter(ReqlFunction1(fun p -> upcast p.["WebLogId"].Eq(webLogId)))
          .RunAtomAsync<Post>(conn)
        |> box with
  | null -> None
  | post -> Some <| unbox post

/// Try to find a post by its permalink
// TODO: see if we can make .Merge work for page list even though the attribute is ignored
//       (needs to be ignored for serialization, but included for deserialization)
let tryFindPostByPermalink conn webLogId permalink =
  r.Table(Table.Post)
    .GetAll(r.Array(webLogId, permalink)).OptArg("index", "Permalink")
    .Filter(fun p -> p.["Status"].Eq(PostStatus.Published))
    .Without("Revisions")
    .Merge(fun p -> r.HashMap("Categories", r.Table(Table.Category)
                                              .GetAll(p.["CategoryIds"])
                                              .Without("Children")
                                              .OrderBy("Name")
                                              .CoerceTo("array")))
    .Merge(fun p -> r.HashMap("Comments", r.Table(Table.Comment)
                                            .GetAll(p.["Id"]).OptArg("index", "PostId")
                                            .OrderBy("PostedOn")
                                            .CoerceTo("array")))
    .RunCursorAsync<Post>(conn)
  |> await
  |> Seq.tryHead

/// Try to find a post by its prior permalink
let tryFindPostByPriorPermalink conn (webLogId : string) (permalink : string) =
  r.Table(Table.Post)
    .GetAll(webLogId).OptArg("index", "webLogId")
    .Filter(fun p -> p.["priorPermalinks"].Contains(permalink).And(p.["status"].Eq(PostStatus.Published)))
    .Without("revisions")
    .RunCursorAsync<Post>(conn)
  |> await
  |> Seq.tryHead

/// Get a set of posts for RSS
let findFeedPosts conn webLogId nbr : (Post * User option) list =
  (publishedPosts webLogId)
    .Merge(fun post -> r.HashMap("Categories", r.Table(Table.Category)
                                                  .GetAll(post.["CategoryIds"])
                                                  .OrderBy("Name")
                                                  .Pluck("Id", "Name")
                                                  .CoerceTo("array")))
  |> toPostList conn 1 nbr
  |> List.map (fun post -> post, match r.Table(Table.User)
                                         .Get(post.AuthorId)
                                         .RunAtomAsync<User>(conn)
                                       |> await
                                       |> box with
                                 | null -> None
                                 | user -> Some <| unbox user)

/// Save a post
let savePost conn post =
  match post.Id with
  | "new" -> let newPost = { post with Id = string <| System.Guid.NewGuid() }
             r.Table(Table.Post)
               .Insert(newPost)
               .RunResultAsync(conn)
             |> ignore
             newPost.Id
  | _     -> r.Table(Table.Post)
               .Get(post.Id)
               .Replace( { post with Categories = List.empty
                                     Comments   = List.empty } )
               .RunResultAsync(conn)
             |> ignore
             post.Id
