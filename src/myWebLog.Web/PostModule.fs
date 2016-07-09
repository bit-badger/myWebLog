namespace myWebLog

open myWebLog.Data.Page
open myWebLog.Data.Post
open Nancy
open Nancy.Authentication.Forms
open Nancy.Security
open RethinkDb.Driver.Net
open myWebLog.Entities

type PostModule(conn : IConnection) as this =
  inherit MyWebLogModule()

  do
    this.Get.["/"]                          <- fun _     -> upcast this.HomePage ()
    this.Get.["/posts/page/{page:int}"]     <- fun parms -> upcast this.GetPageOfPublishedPosts (downcast parms)

    this.Get.["/posts/list"]                <- fun _     -> upcast this.PostList 1
    this.Get.["/posts/list/page/{page:int"] <- fun parms -> upcast this.PostList
                                                                     ((parms :?> DynamicDictionary).["page"] :?> int)

  // ---- Display posts to users ----

  /// Display a page of published posts
  member private this.DisplayPageOfPublishedPosts pageNbr =
    let model = PostsModel(this.Context, this.WebLog)
    model.pageNbr   <- pageNbr
    model.posts     <- findPageOfPublishedPosts conn this.WebLog.id pageNbr 10
    model.hasNewer  <- match List.isEmpty model.posts with
                       | true -> false
                       | _    -> Option.isSome <| tryFindNewerPost conn (List.last model.posts)
    model.hasOlder  <- match List.isEmpty model.posts with
                       | true -> false
                       | _    -> Option.isSome <| tryFindOlderPost conn (List.head model.posts)
    model.urlPrefix <- "/posts"
    model.pageTitle <- match pageNbr with
                       | 1 -> ""
                       | _ -> sprintf "Page #%i" pageNbr
    this.ThemedRender "posts" model

  /// Display either the newest posts or the configured home page
  member this.HomePage () =
    match this.WebLog.defaultPage with
    | "posts" -> this.DisplayPageOfPublishedPosts 1
    | page    -> match tryFindPage conn this.WebLog.id page with
                 | Some page -> let model = PageModel(this.Context, this.WebLog, page)
                                model.pageTitle <- page.title
                                this.ThemedRender "page" model
                 | None      -> this.Negotiate.WithStatusCode 404

  /// Get a page of public posts (other than the first one if the home page is a page of posts)
  member this.GetPageOfPublishedPosts (parameters : DynamicDictionary) =
    this.DisplayPageOfPublishedPosts (parameters.["page"] :?> int)

  // ---- Administer posts ----

  /// Display a page of posts in the admin area
  member this.PostList pageNbr =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = PostsModel(this.Context, this.WebLog)
    model.pageNbr   <- pageNbr
    model.posts     <- findPageOfAllPosts conn this.WebLog.id pageNbr 25
    model.hasNewer  <- pageNbr > 1
    model.hasOlder  <- 25 > List.length model.posts
    model.urlPrefix <- "/post/list"
    model.pageTitle <- "Posts"
    this.View.["admin/post/list", model]
