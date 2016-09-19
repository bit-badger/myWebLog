module MyWebLog.Data.RethinkDB.Post

open MyWebLog.Entities
open RethinkDb.Driver.Ast

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
    .RunResultAsync<Post list>(conn)
  |> await

/// Shorthand to get a newer or older post
let private adjacentPost conn (post : Post) (theFilter : ReqlExpr -> obj) (sort : obj) =
  (publishedPosts post.WebLogId)
    .Filter(theFilter)
    .OrderBy(sort)
    .Limit(1)
    .RunResultAsync<Post list>(conn)
  |> await
  |> List.tryHead

/// Find a newer post
let private newerPost conn post theFilter = adjacentPost conn post theFilter <| r.Asc "PublishedOn"

/// Find an older post
let private olderPost conn post theFilter = adjacentPost conn post theFilter <| r.Desc "PublishedOn"

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
    .RunResultAsync<Post list>(conn)
  |> await

/// Try to find a post by its Id and web log Id
let tryFindPost conn webLogId postId : Post option =
  r.Table(Table.Post)
    .Get(postId)
    .Filter(ReqlFunction1(fun p -> upcast p.["WebLogId"].Eq(webLogId)))
    .RunResultAsync<Post>(conn)
  |> await
  |> box
  |> function null -> None | post -> Some <| unbox post

/// Try to find a post by its permalink
let tryFindPostByPermalink conn webLogId permalink =
  r.Table(Table.Post)
    .GetAll(r.Array(webLogId, permalink)).OptArg("index", "Permalink")
    .Filter(ReqlFunction1(fun p -> upcast p.["Status"].Eq(PostStatus.Published)))
    .Without("Revisions")
    .Merge(ReqlFunction1(fun p ->
      upcast r.HashMap("Categories", r.Table(Table.Category)
                                      .GetAll(p.["CategoryIds"])
                                      .Without("Children")
                                      .OrderBy("Name")
                                      .CoerceTo("array"))))
    .Merge(ReqlFunction1(fun p ->
      upcast r.HashMap("Comments", r.Table(Table.Comment)
                                    .GetAll(p.["id"]).OptArg("index", "PostId")
                                    .OrderBy("PostedOn")
                                    .CoerceTo("array"))))
    .RunResultAsync<Post list>(conn)
  |> await
  |> List.tryHead

/// Try to find a post by its prior permalink
let tryFindPostByPriorPermalink conn (webLogId : string) (permalink : string) =
  r.Table(Table.Post)
    .GetAll(webLogId).OptArg("index", "WebLogId")
    .Filter(ReqlFunction1(fun p ->
      upcast p.["PriorPermalinks"].Contains(permalink).And(p.["Status"].Eq(PostStatus.Published))))
    .Without("Revisions")
    .RunResultAsync<Post list>(conn)
  |> await
  |> List.tryHead

/// Get a set of posts for RSS
let findFeedPosts conn webLogId nbr : (Post * User option) list =
  (publishedPosts webLogId)
    .Merge(ReqlFunction1(fun post ->
      upcast r.HashMap("Categories", r.Table(Table.Category)
                                      .GetAll(post.["CategoryIds"])
                                      .OrderBy("Name")
                                      .Pluck("id", "Name")
                                      .CoerceTo("array"))))
  |> toPostList conn 1 nbr
  |> List.map (fun post -> post, r.Table(Table.User)
                                   .Get(post.AuthorId)
                                   .RunAtomAsync<User>(conn)
                                 |> await
                                 |> box
                                 |> function null -> None | user -> Some <| unbox user)

/// Add a post
let addPost conn post =
  r.Table(Table.Post)
    .Insert(post)
    .RunResultAsync(conn)
  |> await
  |> ignore

/// Update a post
let updatePost conn (post : Post) =
  r.Table(Table.Post)
    .Get(post.Id)
    .Replace( { post with Categories = []
                          Comments   = [] } )
    .RunResultAsync(conn)
  |> await
  |> ignore
  
/// Save a post
let savePost conn (post : Post) =
  match post.Id with
  | "new" -> let newPost = { post with Id = string <| System.Guid.NewGuid() }
             r.Table(Table.Post)
               .Insert(newPost)
               .RunResultAsync(conn)
             |> await
             |> ignore
             newPost.Id
  | _ -> r.Table(Table.Post)
           .Get(post.Id)
           .Replace( { post with Categories = []
                                 Comments   = [] } )
           .RunResultAsync(conn)
         |> await
         |> ignore
         post.Id
