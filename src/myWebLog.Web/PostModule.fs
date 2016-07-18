namespace myWebLog

open FSharp.Markdown
open myWebLog.Data.Category
open myWebLog.Data.Page
open myWebLog.Data.Post
open myWebLog.Entities
open Nancy
open Nancy.ModelBinding
open Nancy.Security
open Nancy.Session.Persistable
open NodaTime
open RethinkDb.Driver.Net

/// Routes dealing with posts (including the home page, /tag, /category, and catch-all routes)
type PostModule(conn : IConnection, clock : IClock) as this =
  inherit NancyModule()

  /// Get the page number from the dictionary
  let getPage (parameters : DynamicDictionary) =
    match parameters.ContainsKey "page" with | true -> downcast parameters.["page"] | _ -> 1

  do
    this.Get .["/"                               ] <- fun _     -> upcast this.HomePage ()
    this.Get .["/{permalink*}"                   ] <- fun parms -> upcast this.CatchAll (downcast parms)
    this.Get .["/posts/page/{page:int}"          ] <- fun parms -> upcast this.PublishedPostsPage (getPage <| downcast parms)
    this.Get .["/category/{slug}"                ] <- fun parms -> upcast this.CategorizedPosts (downcast parms)
    this.Get .["/category/{slug}/page/{page:int}"] <- fun parms -> upcast this.CategorizedPosts (downcast parms)
    this.Get .["/tag/{tag}"                      ] <- fun parms -> upcast this.TaggedPosts (downcast parms)
    this.Get .["/tag/{tag}/page/{page:int}"      ] <- fun parms -> upcast this.TaggedPosts (downcast parms)
    this.Get .["/posts/list"                     ] <- fun _     -> upcast this.PostList 1
    this.Get .["/posts/list/page/{page:int}"     ] <- fun parms -> upcast this.PostList (getPage <| downcast parms)
    this.Get .["/post/{postId}/edit"             ] <- fun parms -> upcast this.EditPost (downcast parms)
    this.Post.["/post/{postId}/edit"             ] <- fun parms -> upcast this.SavePost (downcast parms)

  // ---- Display posts to users ----

  /// Display a page of published posts
  member this.PublishedPostsPage pageNbr =
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
                       | _ -> sprintf "%s%i" Resources.PageHash pageNbr
    this.ThemedView "index" model

  /// Display either the newest posts or the configured home page
  member this.HomePage () =
    match this.WebLog.defaultPage with
    | "posts" -> this.PublishedPostsPage 1
    | pageId  -> match tryFindPageWithoutRevisions conn this.WebLog.id pageId with
                 | Some page -> let model = PageModel(this.Context, this.WebLog, page)
                                model.pageTitle <- page.title
                                this.ThemedView "page" model
                 | None      -> this.NotFound ()

  /// Derive a post or page from the URL, or redirect from a prior URL to the current one
  member this.CatchAll (parameters : DynamicDictionary) =
    let url : string = parameters.["permalink"].ToString ()
    match tryFindPostByPermalink conn this.WebLog.id url with
    | Some post -> // Hopefully the most common result; the permalink is a permalink!
                   let model = PostModel(this.Context, this.WebLog, post)
                   model.newerPost <- tryFindNewerPost conn post
                   model.olderPost <- tryFindOlderPost conn post
                   model.pageTitle <- post.title
                   this.ThemedView "single" model
    | None      -> // Maybe it's a page permalink instead...
                   match tryFindPageByPermalink conn this.WebLog.id url with
                   | Some page -> // ...and it is!
                                  let model = PageModel(this.Context, this.WebLog, page)
                                  model.pageTitle <- page.title
                                  this.ThemedView "page" model
                   | None      -> // Maybe it's an old permalink for a post
                                  match tryFindPostByPriorPermalink conn this.WebLog.id url with
                                  | Some post -> // Redirect them to the proper permalink
                                                 this.Negotiate
                                                   .WithHeader("Location", sprintf "/%s" post.permalink)
                                                   .WithStatusCode HttpStatusCode.MovedPermanently
                                  | None      -> this.NotFound ()

  /// Display categorized posts
  member this.CategorizedPosts (parameters : DynamicDictionary) =
    let slug : string = downcast parameters.["slug"]
    match tryFindCategoryBySlug conn this.WebLog.id slug with
    | Some cat -> let pageNbr = getPage parameters
                  let model   = PostsModel(this.Context, this.WebLog)
                  model.pageNbr   <- pageNbr
                  model.posts     <- findPageOfCategorizedPosts conn this.WebLog.id cat.id pageNbr 10
                  model.hasNewer  <- match List.isEmpty model.posts with
                                     | true -> false
                                     | _    -> Option.isSome <| tryFindNewerCategorizedPost conn cat.id
                                                                                            (List.last model.posts)
                  model.hasOlder  <- match List.isEmpty model.posts with
                                     | true -> false
                                     | _    -> Option.isSome <| tryFindOlderCategorizedPost conn cat.id
                                                                                            (List.last model.posts)
                  model.urlPrefix <- sprintf "/category/%s" slug
                  model.pageTitle <- sprintf "\"%s\" Category%s" cat.name
                                             (match pageNbr with | 1 -> "" | n -> sprintf " | Page %i" n)
                  model.subtitle  <- Some <| match cat.description with
                                             | Some desc -> desc
                                             | None      -> sprintf "Posts in the \"%s\" category" cat.name
                  this.ThemedView "index" model
    | None     -> this.NotFound ()

  /// Display tagged posts
  member this.TaggedPosts (parameters : DynamicDictionary) =
    let tag : string = downcast parameters.["tag"]
    let pageNbr      = getPage parameters
    let model        = PostsModel(this.Context, this.WebLog)
    model.pageNbr   <- pageNbr
    model.posts     <- findPageOfTaggedPosts conn this.WebLog.id tag pageNbr 10
    model.hasNewer  <- match List.isEmpty model.posts with
                       | true -> false
                       | _    -> Option.isSome <| tryFindNewerTaggedPost conn tag (List.last model.posts)
    model.hasOlder  <- match List.isEmpty model.posts with
                       | true -> false
                       | _    -> Option.isSome <| tryFindOlderTaggedPost conn tag (List.last model.posts)
    model.urlPrefix <- sprintf "/tag/%s" tag
    model.pageTitle <- sprintf "\"%s\" Tag%s" tag (match pageNbr with | 1 -> "" | n -> sprintf " | Page %i" n)
    model.subtitle  <- Some <| sprintf "Posts tagged \"%s\"" tag
    this.ThemedView "index" model


  // ---- Administer posts ----

  /// Display a page of posts in the admin area
  member this.PostList pageNbr =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = PostsModel(this.Context, this.WebLog)
    model.pageNbr   <- pageNbr
    model.posts     <- findPageOfAllPosts conn this.WebLog.id pageNbr 25
    model.hasNewer  <- pageNbr > 1
    model.hasOlder  <- List.length model.posts < 25
    model.urlPrefix <- "/post/list"
    model.pageTitle <- Resources.Posts
    this.View.["admin/post/list", model]

  /// Edit a post
  member this.EditPost (parameters : DynamicDictionary) =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let postId : string = downcast parameters.["postId"]
    match (match postId with
           | "new" -> Some Post.empty
           | _     -> tryFindPost conn this.WebLog.id postId) with
    | Some post -> let rev = match post.revisions
                                   |> List.sortByDescending (fun r -> r.asOf)
                                   |> List.tryHead with
                             | Some r -> r
                             | None   -> Revision.empty
                   let model = EditPostModel(this.Context, this.WebLog, post, rev)
                   model.categories <- getAllCategories conn this.WebLog.id
                                       |> List.map (fun cat -> string (fst cat).id,
                                                               sprintf "%s%s"
                                                                       (String.replicate (snd cat) " &nbsp; &nbsp; ")
                                                                       (fst cat).name)
                   model.pageTitle  <- match post.id with
                                       | "new" -> Resources.AddNewPost
                                       | _     -> Resources.EditPost
                   this.View.["admin/post/edit"]
    | None      -> this.NotFound ()

  /// Save a post
  member this.SavePost (parameters : DynamicDictionary) =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    this.ValidateCsrfToken ()
    let postId : string = downcast parameters.["postId"]
    let form = this.Bind<EditPostForm>()
    let now = clock.Now.Ticks
    match (match postId with
           | "new" -> Some Post.empty
           | _     -> tryFindPost conn this.WebLog.id postId) with
    | Some p -> let justPublished = p.publishedOn = int64 0 && form.publishNow
                let post          = match postId with
                                    | "new" -> { p with
                                                   webLogId = this.WebLog.id
                                                   authorId = (this.Request.PersistableSession.GetOrDefault<User>
                                                                (Keys.User, User.empty)).id }
                                    | _     -> p
                let pId = { post with
                              status      = match form.publishNow with
                                            | true -> PostStatus.Published
                                            | _    -> PostStatus.Draft
                              title       = form.title
                              permalink   = form.permalink
                              publishedOn = match justPublished with | true -> now | _ -> int64 0
                              updatedOn   = now
                              text        = match form.source with
                                            | RevisionSource.Markdown -> Markdown.TransformHtml form.text
                                            | _                       -> form.text
                              categoryIds = Array.toList form.categories
                              tags        = form.tags.Split ','
                                            |> Seq.map (fun t -> t.Trim().ToLowerInvariant())
                                            |> Seq.toList
                              revisions   = { asOf       = now
                                              sourceType = form.source
                                              text       = form.text } :: post.revisions }
                          |> savePost conn
                let model = MyWebLogModel(this.Context, this.WebLog)
                { level   = Level.Info
                  message = System.String.Format
                              (Resources.MsgPostEditSuccess,
                               (match postId with | "new" -> Resources.Added | _ -> Resources.Updated),
                               (match justPublished with | true  -> Resources.AndPublished | _ -> ""))
                  details = None }
                |> model.addMessage
                this.Redirect (sprintf "/post/%s/edit" pId) model
    | None   -> this.NotFound ()
