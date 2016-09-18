namespace MyWebLog

open MyWebLog.Data
open MyWebLog.Entities
open MyWebLog.Logic.Category
open MyWebLog.Logic.Page
open MyWebLog.Logic.Post
open MyWebLog.Resources
open Nancy
open Nancy.ModelBinding
open Nancy.Security
open Nancy.Session.Persistable
open NodaTime
open RethinkDb.Driver.Net
open System
//open System.ServiceModel.Syndication

/// Routes dealing with posts (including the home page, /tag, /category, RSS, and catch-all routes)
type PostModule(data : IMyWebLogData, clock : IClock) as this =
  inherit NancyModule()

  /// Get the page number from the dictionary
  let getPage (parameters : DynamicDictionary) =
    match parameters.ContainsKey "page" with true -> System.Int32.Parse (parameters.["page"].ToString ()) | _ -> 1

  /// Convert a list of posts to a list of posts for display
  let forDisplay posts = posts |> List.map (fun post -> PostForDisplay(this.WebLog, post))

  /// Generate an RSS/Atom feed of the latest posts
  let generateFeed format : obj =
    this.NotFound ()
    (* let posts  = findFeedPosts data this.WebLog.Id 10
    let feed   =
      SyndicationFeed(
        this.WebLog.Name, defaultArg this.WebLog.Subtitle null,
        Uri(sprintf "%s://%s" this.Request.Url.Scheme this.WebLog.UrlBase), null,
        (match posts |> List.tryHead with
         | Some (post, _) -> Instant(post.UpdatedOn).ToDateTimeOffset ()
         | _ -> System.DateTimeOffset(System.DateTime.MinValue)),
        posts
        |> List.map (fun (post, user) ->
            let item =
              SyndicationItem(
                BaseUri         = Uri(sprintf "%s://%s/%s" this.Request.Url.Scheme this.WebLog.UrlBase post.Permalink),
                PublishDate     = Instant(post.PublishedOn).ToDateTimeOffset (),
                LastUpdatedTime = Instant(post.UpdatedOn).ToDateTimeOffset (),
                Title           = TextSyndicationContent(post.Title),
                Content         = TextSyndicationContent(post.Text, TextSyndicationContentKind.Html))
            user
            |> Option.iter (fun u -> item.Authors.Add
                                       (SyndicationPerson(u.UserName, u.PreferredName, defaultArg u.Url null)))
            post.Categories
            |> List.iter (fun c -> item.Categories.Add(SyndicationCategory(c.Name)))
            item))
    let stream = new IO.MemoryStream()
    Xml.XmlWriter.Create(stream)
    |> match format with "atom" -> feed.SaveAsAtom10 | _ -> feed.SaveAsRss20
    stream.Position <- int64 0
    upcast this.Response.FromStream(stream, sprintf "application/%s+xml" format) *)

  do
    this.Get ("/",                                fun _     -> this.HomePage ())
    this.Get ("/{permalink*}",                    fun parms -> this.CatchAll (downcast parms))
    this.Get ("/posts/page/{page:int}",           fun parms -> this.PublishedPostsPage (getPage <| downcast parms))
    this.Get ("/category/{slug}",                 fun parms -> this.CategorizedPosts (downcast parms))
    this.Get ("/category/{slug}/page/{page:int}", fun parms -> this.CategorizedPosts (downcast parms))
    this.Get ("/tag/{tag}",                       fun parms -> this.TaggedPosts (downcast parms))
    this.Get ("/tag/{tag}/page/{page:int}",       fun parms -> this.TaggedPosts (downcast parms))
    this.Get ("/feed",                            fun _     -> this.Feed ())
    this.Get ("/posts/list",                      fun _     -> this.PostList 1)
    this.Get ("/posts/list/page/{page:int}",      fun parms -> this.PostList (getPage <| downcast parms))
    this.Get ("/post/{postId}/edit",              fun parms -> this.EditPost (downcast parms))
    this.Post("/post/{postId}/edit",              fun parms -> this.SavePost (downcast parms))

  // ---- Display posts to users ----

  /// Display a page of published posts
  member this.PublishedPostsPage pageNbr : obj =
    let model = PostsModel(this.Context, this.WebLog)
    model.PageNbr   <- pageNbr
    model.Posts     <- findPageOfPublishedPosts data this.WebLog.Id pageNbr 10 |> forDisplay
    model.HasNewer  <- match pageNbr with
                       | 1 -> false
                       | _ -> match List.isEmpty model.Posts with
                              | true -> false
                              | _ -> Option.isSome <| tryFindNewerPost data (List.last model.Posts).Post
    model.HasOlder  <- match List.isEmpty model.Posts with
                       | true -> false
                       | _ -> Option.isSome <| tryFindOlderPost data (List.head model.Posts).Post
    model.UrlPrefix <- "/posts"
    model.PageTitle <- match pageNbr with 1 -> "" | _ -> sprintf "%s%i" (Strings.get "PageHash") pageNbr
    this.ThemedView "index" model

  /// Display either the newest posts or the configured home page
  member this.HomePage () : obj =
    match this.WebLog.DefaultPage with
    | "posts" -> this.PublishedPostsPage 1
    | pageId -> match tryFindPageWithoutRevisions data this.WebLog.Id pageId with
                | Some page -> let model = PageModel(this.Context, this.WebLog, page)
                               model.PageTitle <- page.Title
                               this.ThemedView "page" model
                | _ -> this.NotFound ()

  /// Derive a post or page from the URL, or redirect from a prior URL to the current one
  member this.CatchAll (parameters : DynamicDictionary) : obj =
    let url = parameters.["permalink"].ToString ()
    match tryFindPostByPermalink data this.WebLog.Id url with
    | Some post -> // Hopefully the most common result; the permalink is a permalink!
                   let model = PostModel(this.Context, this.WebLog, post)
                   model.NewerPost <- tryFindNewerPost data post
                   model.OlderPost <- tryFindOlderPost data post
                   model.PageTitle <- post.Title
                   this.ThemedView "single" model
    | _ -> // Maybe it's a page permalink instead...
           match tryFindPageByPermalink data this.WebLog.Id url with
           | Some page -> // ...and it is!
                          let model = PageModel(this.Context, this.WebLog, page)
                          model.PageTitle <- page.Title
                          this.ThemedView "page" model
           | _ -> // Maybe it's an old permalink for a post
                  match tryFindPostByPriorPermalink data this.WebLog.Id url with
                  | Some post -> // Redirect them to the proper permalink
                                  upcast this.Response.AsRedirect(sprintf "/%s" post.Permalink)
                                          .WithStatusCode HttpStatusCode.MovedPermanently
                  | _ -> this.NotFound ()

  /// Display categorized posts
  member this.CategorizedPosts (parameters : DynamicDictionary) : obj =
    let slug = parameters.["slug"].ToString ()
    match tryFindCategoryBySlug data this.WebLog.Id slug with
    | Some cat -> let pageNbr = getPage parameters
                  let model   = PostsModel(this.Context, this.WebLog)
                  model.PageNbr   <- pageNbr
                  model.Posts     <- findPageOfCategorizedPosts data this.WebLog.Id cat.Id pageNbr 10 |> forDisplay
                  model.HasNewer  <- match List.isEmpty model.Posts with
                                     | true -> false
                                     | _ -> Option.isSome <| tryFindNewerCategorizedPost data cat.Id
                                                                                         (List.head model.Posts).Post
                  model.HasOlder  <- match List.isEmpty model.Posts with
                                     | true -> false
                                     | _ -> Option.isSome <| tryFindOlderCategorizedPost data cat.Id
                                                                                         (List.last model.Posts).Post
                  model.UrlPrefix <- sprintf "/category/%s" slug
                  model.PageTitle <- sprintf "\"%s\" Category%s" cat.Name
                                             (match pageNbr with | 1 -> "" | n -> sprintf " | Page %i" n)
                  model.Subtitle  <- Some <| match cat.Description with
                                             | Some desc -> desc
                                             | _ -> sprintf "Posts in the \"%s\" category" cat.Name
                  this.ThemedView "index" model
    | _ -> this.NotFound ()

  /// Display tagged posts
  member this.TaggedPosts (parameters : DynamicDictionary) : obj =
    let tag     = parameters.["tag"].ToString ()
    let pageNbr = getPage parameters
    let model   = PostsModel(this.Context, this.WebLog)
    model.PageNbr   <- pageNbr
    model.Posts     <- findPageOfTaggedPosts data this.WebLog.Id tag pageNbr 10 |> forDisplay
    model.HasNewer  <- match List.isEmpty model.Posts with
                       | true -> false
                       | _ -> Option.isSome <| tryFindNewerTaggedPost data tag (List.head model.Posts).Post
    model.HasOlder  <- match List.isEmpty model.Posts with
                       | true -> false
                       | _ -> Option.isSome <| tryFindOlderTaggedPost data tag (List.last model.Posts).Post
    model.UrlPrefix <- sprintf "/tag/%s" tag
    model.PageTitle <- sprintf "\"%s\" Tag%s" tag (match pageNbr with 1 -> "" | n -> sprintf " | Page %i" n)
    model.Subtitle  <- Some <| sprintf "Posts tagged \"%s\"" tag
    this.ThemedView "index" model

  /// Generate an RSS feed
  member this.Feed () : obj =
    let query = this.Request.Query :?> DynamicDictionary
    match query.ContainsKey "format" with
    | true -> match query.["format"].ToString () with
              | x when x = "atom" || x = "rss" -> generateFeed x
              | x when x = "rss2" -> generateFeed "rss"
              | _ -> this.Redirect "/feed" (MyWebLogModel(this.Context, this.WebLog))
    | _ -> generateFeed "rss"

  // ---- Administer posts ----

  /// Display a page of posts in the admin area
  member this.PostList pageNbr : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = PostsModel(this.Context, this.WebLog)
    model.PageNbr   <- pageNbr
    model.Posts     <- findPageOfAllPosts data this.WebLog.Id pageNbr 25 |> forDisplay
    model.HasNewer  <- pageNbr > 1
    model.HasOlder  <- List.length model.Posts > 24
    model.UrlPrefix <- "/posts/list"
    model.PageTitle <- Strings.get "Posts"
    upcast this.View.["admin/post/list", model]

  /// Edit a post
  member this.EditPost (parameters : DynamicDictionary) : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let postId = parameters.["postId"].ToString ()
    match (match postId with "new" -> Some Post.Empty | _ -> tryFindPost data this.WebLog.Id postId) with
    | Some post -> let rev = match post.Revisions
                                   |> List.sortByDescending (fun r -> r.AsOf)
                                   |> List.tryHead with
                             | Some r -> r
                             | None   -> Revision.Empty
                   let model = EditPostModel(this.Context, this.WebLog, post, rev)
                   model.Categories <- findAllCategories data this.WebLog.Id
                                       |> List.map (fun cat -> string (fst cat).Id,
                                                               sprintf "%s%s"
                                                                       (String.replicate (snd cat) " &nbsp; &nbsp; ")
                                                                       (fst cat).Name)
                   model.PageTitle  <- Strings.get <| match post.Id with "new" -> "AddNewPost" | _ -> "EditPost"
                   upcast this.View.["admin/post/edit"]
    | _ -> this.NotFound ()

  /// Save a post
  member this.SavePost (parameters : DynamicDictionary) : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    this.ValidateCsrfToken ()
    let postId = parameters.["postId"].ToString ()
    let form   = this.Bind<EditPostForm>()
    let now    = clock.GetCurrentInstant().ToUnixTimeTicks()
    match (match postId with "new" -> Some Post.Empty | _ -> tryFindPost data this.WebLog.Id postId) with
    | Some p -> let justPublished = p.PublishedOn = int64 0 && form.PublishNow
                let post          = match postId with
                                    | "new" -> { p with
                                                   WebLogId = this.WebLog.Id
                                                   AuthorId = (this.Request.PersistableSession.GetOrDefault<User>
                                                                (Keys.User, User.Empty)).Id }
                                    | _ -> p
                let pId = { post with
                              Status      = match form.PublishNow with
                                            | true -> PostStatus.Published
                                            | _ -> PostStatus.Draft
                              Title       = form.Title
                              Permalink   = form.Permalink
                              PublishedOn = match justPublished with true -> now | _ -> int64 0
                              UpdatedOn   = now
                              Text        = match form.Source with
                                            | RevisionSource.Markdown -> (* Markdown.TransformHtml *) form.Text
                                            | _ -> form.Text
                              CategoryIds = Array.toList form.Categories
                              Tags        = form.Tags.Split ','
                                            |> Seq.map (fun t -> t.Trim().ToLowerInvariant())
                                            |> Seq.toList
                              Revisions   = { AsOf       = now
                                              SourceType = form.Source
                                              Text       = form.Text } :: post.Revisions }
                          |> savePost data
                let model = MyWebLogModel(this.Context, this.WebLog)
                { UserMessage.Empty with
                    Level   = Level.Info
                    Message = System.String.Format
                                (Strings.get "MsgPostEditSuccess",
                                 Strings.get (match postId with "new" -> "Added" | _ -> "Updated"),
                                 (match justPublished with true -> Strings.get "AndPublished" | _ -> "")) }
                |> model.AddMessage
                this.Redirect (sprintf "/post/%s/edit" pId) model
    | _ -> this.NotFound ()
