module myWebLog.Data.Post

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open RethinkDb.Driver.Ast
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to select all published posts for a web log
let private publishedPosts (webLogId : string)=
  r.Table(Table.Post)
    .GetAll(r.Array(webLogId, PostStatus.Published)).OptArg("index", "webLogAndStatus")

/// Shorthand to sort posts by published date, slice for the given page, and return a list
let private toPostList conn pageNbr nbrPerPage (filter : ReqlExpr) =
  filter
    .OrderBy(r.Desc("publishedOn"))
    .Slice((pageNbr - 1) * nbrPerPage, pageNbr * nbrPerPage)
    .RunListAsync<Post>(conn)
  |> await
  |> Seq.toList

/// Shorthand to get a newer or older post
// TODO: older posts need to sort by published on DESC
//let private adjacentPost conn post (theFilter : ReqlExpr -> ReqlExpr) (sort :ReqlExpr) : Post option =
let private adjacentPost conn post (theFilter : ReqlExpr -> obj) (sort : obj) : Post option =
  (publishedPosts post.webLogId)
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
    .Filter(fun p -> p.["categoryIds"].Contains(categoryId))
  |> toPostList conn pageNbr nbrPerPage

/// Get a page of published posts tagged with a given tag
let findPageOfTaggedPosts conn webLogId (tag : string) pageNbr nbrPerPage =
  (publishedPosts webLogId)
    .Filter(fun p -> p.["tags"].Contains(tag))
  |> toPostList conn pageNbr nbrPerPage

/// Try to get the next newest post from the given post
let tryFindNewerPost conn post = newerPost conn post (fun p -> upcast p.["publishedOn"].Gt(post.publishedOn))
 
/// Try to get the next newest post assigned to the given category
let tryFindNewerCategorizedPost conn (categoryId : string) post =
  newerPost conn post (fun p -> upcast p.["publishedOn"].Gt(post.publishedOn)
                                        .And(p.["categoryIds"].Contains(categoryId)))
 
/// Try to get the next newest tagged post from the given tagged post
let tryFindNewerTaggedPost conn (tag : string) post =
  newerPost conn post (fun p -> upcast p.["publishedOn"].Gt(post.publishedOn).And(p.["tags"].Contains(tag)))
 
/// Try to get the next oldest post from the given post
let tryFindOlderPost conn post = olderPost conn post (fun p -> upcast p.["publishedOn"].Lt(post.publishedOn))

/// Try to get the next oldest post assigned to the given category
let tryFindOlderCategorizedPost conn (categoryId : string) post =
  olderPost conn post (fun p -> upcast p.["publishedOn"].Lt(post.publishedOn)
                                        .And(p.["categoryIds"].Contains(categoryId)))

/// Try to get the next oldest tagged post from the given tagged post
let tryFindOlderTaggedPost conn (tag : string) post =
  olderPost conn post (fun p -> upcast p.["publishedOn"].Lt(post.publishedOn).And(p.["tags"].Contains(tag)))

/// Get a page of all posts in all statuses
let findPageOfAllPosts conn (webLogId : string) pageNbr nbrPerPage =
  // FIXME: sort unpublished posts by their last updated date
  r.Table(Table.Post)
    .GetAll(webLogId).OptArg("index", "webLogId")
    .OrderBy(r.Desc("publishedOn"))
    .Slice((pageNbr - 1) * nbrPerPage, pageNbr * nbrPerPage)
    .RunListAsync<Post>(conn)
  |> await
  |> Seq.toList

/// Try to find a post by its Id and web log Id
let tryFindPost conn webLogId postId : Post option =
  match r.Table(Table.Post)
          .Get(postId)
          .Filter(fun p -> p.["webLogId"].Eq(webLogId))
          .RunAtomAsync<Post>(conn)
        |> box with
  | null -> None
  | post -> Some <| unbox post

/// Try to find a post by its permalink
let tryFindPostByPermalink conn webLogId permalink =
  r.Table(Table.Post)
    .GetAll(r.Array(webLogId, permalink)).OptArg("index", "permalink")
    .Filter(fun p -> p.["status"].Eq(PostStatus.Published))
    .Without("revisions")
    .Merge(fun post -> r.HashMap("categories",
                         post.["categoryIds"]
                           .Map(ReqlFunction1(fun cat -> upcast r.Table(Table.Category).Get(cat).Without("children")))
                           .CoerceTo("array")))
    .Merge(fun post -> r.HashMap("comments",
                         r.Table(Table.Comment)
                           .GetAll(post.["id"]).OptArg("index", "postId")
                           .OrderBy("postedOn")
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

/// Save a post
let savePost conn post =
  match post.id with
  | "new" -> let newPost = { post with id = string <| System.Guid.NewGuid() }
             r.Table(Table.Post)
               .Insert(newPost)
               .RunResultAsync(conn)
             |> ignore
             newPost.id
  | _     -> r.Table(Table.Post)
               .Get(post.id)
               .Replace(post)
               .RunResultAsync(conn)
             |> ignore
             post.id
