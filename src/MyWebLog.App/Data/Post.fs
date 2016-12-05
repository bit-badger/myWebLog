module MyWebLog.Data.RethinkDB.Post

open MyWebLog.Entities
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to select all published posts for a web log
let private publishedPosts (webLogId : string) =
  r.Table(Table.Post)
    .GetAll(r.Array (webLogId, PostStatus.Published)).OptArg("index", "WebLogAndStatus")
    .Without("Revisions")
    // This allows us to count comments without retrieving them all
    .Merge(ReqlFunction1 (fun p ->
          upcast r.HashMap(
            "Comments", r.Table(Table.Comment)
                          .GetAll(p.["id"]).OptArg("index", "PostId")
                          .Pluck("id")
                          .CoerceTo("array"))))


/// Shorthand to sort posts by published date, slice for the given page, and return a list
let private toPostList conn pageNbr nbrPerPage (filter : ReqlExpr) =
  async {
    return!
      filter
        .OrderBy(r.Desc "PublishedOn")
        .Slice((pageNbr - 1) * nbrPerPage, pageNbr * nbrPerPage)
        .RunResultAsync<Post list> conn
    }
  |> Async.RunSynchronously

/// Shorthand to get a newer or older post
let private adjacentPost conn (post : Post) (theFilter : ReqlExpr -> obj) (sort : obj) =
  async {
    let! post =
      (publishedPosts post.WebLogId)
        .Filter(theFilter)
        .OrderBy(sort)
        .Limit(1)
        .RunResultAsync<Post list> conn
    return List.tryHead post
    }
  |> Async.RunSynchronously

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
    .Filter(ReqlFunction1 (fun p -> upcast p.["CategoryIds"].Contains categoryId))
  |> toPostList conn pageNbr nbrPerPage

/// Get a page of published posts tagged with a given tag
let findPageOfTaggedPosts conn webLogId (tag : string) pageNbr nbrPerPage =
  (publishedPosts webLogId)
    .Filter(ReqlFunction1 (fun p -> upcast p.["Tags"].Contains tag))
  |> toPostList conn pageNbr nbrPerPage

/// Try to get the next newest post from the given post
let tryFindNewerPost conn post = newerPost conn post (fun p -> upcast p.["PublishedOn"].Gt post.PublishedOn)
 
/// Try to get the next newest post assigned to the given category
let tryFindNewerCategorizedPost conn (categoryId : string) post =
  newerPost conn post (fun p -> upcast p.["PublishedOn"].Gt(post.PublishedOn)
                                        .And(p.["CategoryIds"].Contains categoryId))
 
/// Try to get the next newest tagged post from the given tagged post
let tryFindNewerTaggedPost conn (tag : string) post =
  newerPost conn post (fun p -> upcast p.["PublishedOn"].Gt(post.PublishedOn).And(p.["Tags"].Contains tag))
 
/// Try to get the next oldest post from the given post
let tryFindOlderPost conn post = olderPost conn post (fun p -> upcast p.["PublishedOn"].Lt post.PublishedOn)

/// Try to get the next oldest post assigned to the given category
let tryFindOlderCategorizedPost conn (categoryId : string) post =
  olderPost conn post (fun p -> upcast p.["PublishedOn"].Lt(post.PublishedOn)
                                        .And(p.["CategoryIds"].Contains categoryId))

/// Try to get the next oldest tagged post from the given tagged post
let tryFindOlderTaggedPost conn (tag : string) post =
  olderPost conn post (fun p -> upcast p.["PublishedOn"].Lt(post.PublishedOn).And(p.["Tags"].Contains tag))

/// Get a page of all posts in all statuses
let findPageOfAllPosts conn (webLogId : string) pageNbr nbrPerPage =
  // FIXME: sort unpublished posts by their last updated date
  async {
  //  .orderBy(r.desc(r.branch(r.row("Status").eq("Published"), r.row("PublishedOn"), r.row("UpdatedOn"))))
    return!
      r.Table(Table.Post)
        .GetAll(webLogId).OptArg("index", "WebLogId")
        .OrderBy(r.Desc (ReqlFunction1 (fun p ->
            upcast r.Branch (p.["Status"].Eq("Published"), p.["PublishedOn"], p.["UpdatedOn"]))))
        .Slice((pageNbr - 1) * nbrPerPage, pageNbr * nbrPerPage)
        .RunResultAsync<Post list> conn
    }
  |> Async.RunSynchronously

/// Try to find a post by its Id and web log Id
let tryFindPost conn webLogId postId : Post option =
  async {
    let! p =
      r.Table(Table.Post)
        .Get(postId)
        .RunAtomAsync<Post> conn
    return
      match box p with
      | null -> None
      | pst ->
          let post : Post = unbox pst
          match post.WebLogId = webLogId with true -> Some post | _ -> None
    }
  |> Async.RunSynchronously

/// Try to find a post by its permalink
let tryFindPostByPermalink conn webLogId permalink =
  async {
    let! post =
      r.Table(Table.Post)
        .GetAll(r.Array (webLogId, permalink)).OptArg("index", "Permalink")
        .Filter(ReqlFunction1 (fun p -> upcast p.["Status"].Eq PostStatus.Published))
        .Without("Revisions")
        .Merge(ReqlFunction1 (fun p ->
          upcast r.HashMap(
            "Categories", r.Table(Table.Category)
                            .GetAll(r.Args p.["CategoryIds"])
                            .Without("Children")
                            .OrderBy("Name")
                            .CoerceTo("array")).With(
            "Comments", r.Table(Table.Comment)
                          .GetAll(p.["id"]).OptArg("index", "PostId")
                          .OrderBy("PostedOn")
                          .CoerceTo("array"))))
        .RunResultAsync<Post list> conn
    return List.tryHead post
    }
  |> Async.RunSynchronously

/// Try to find a post by its prior permalink
let tryFindPostByPriorPermalink conn (webLogId : string) (permalink : string) =
  async {
    let! post =
      r.Table(Table.Post)
        .GetAll(webLogId).OptArg("index", "WebLogId")
        .Filter(ReqlFunction1 (fun p ->
          upcast p.["PriorPermalinks"].Contains(permalink).And(p.["Status"].Eq PostStatus.Published)))
        .Without("Revisions")
        .RunResultAsync<Post list> conn
    return List.tryHead post
    }
  |> Async.RunSynchronously

/// Get a set of posts for RSS
let findFeedPosts conn webLogId nbr : (Post * User option) list =
  let tryFindUser userId =
    async {
      let! u =
        r.Table(Table.User)
          .Get(userId)
          .RunAtomAsync<User> conn
      return match box u with null -> None | user -> Some <| unbox user
      }
    |> Async.RunSynchronously
  (publishedPosts webLogId)
    .Merge(ReqlFunction1 (fun post ->
      upcast r.HashMap(
        "Categories", r.Table(Table.Category)
                        .GetAll(r.Args post.["CategoryIds"])
                        .OrderBy("Name")
                        .Pluck("id", "Name")
                        .CoerceTo("array"))))
  |> toPostList conn 1 nbr
  |> List.map (fun post -> post, tryFindUser post.AuthorId)

/// Add a post
let addPost conn post =
  async {
    do! r.Table(Table.Post)
          .Insert(post)
          .RunResultAsync conn
    }
  |> (Async.RunSynchronously >> ignore)

/// Update a post
let updatePost conn (post : Post) =
  async {
    do! r.Table(Table.Post)
          .Get(post.Id)
          .Replace( { post with Categories = []
                                Comments   = [] } )
          .RunResultAsync conn
    }
  |> (Async.RunSynchronously >> ignore)
  
/// Save a post
let savePost conn (post : Post) =
  match post.Id with
  | "new" ->
      let newPost = { post with Id = string <| System.Guid.NewGuid() }
      async {
        do! r.Table(Table.Post)
              .Insert(newPost)
              .RunResultAsync conn
        }
      |> Async.RunSynchronously
      newPost.Id
  | _ ->
      async {
        do! r.Table(Table.Post)
              .Get(post.Id)
              .Replace( { post with Categories = []
                                    Comments   = [] } )
              .RunResultAsync conn
        }
      |> Async.RunSynchronously
      post.Id
