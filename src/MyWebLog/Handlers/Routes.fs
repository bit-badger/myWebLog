/// Routes for this application
module MyWebLog.Handlers.Routes

open Giraffe
open Microsoft.AspNetCore.Http
open MyWebLog

/// Module to resolve routes that do not match any other known route (web blog content) 
module CatchAll =
    
    open MyWebLog.ViewModels
    
    /// Sequence where the first returned value is the proper handler for the link
    let private deriveAction (ctx: HttpContext) : HttpHandler seq =
        let webLog   = ctx.WebLog
        let data     = ctx.Data
        let debug    = debug "Routes.CatchAll" ctx
        let textLink =
            let extra = webLog.ExtraPath
            let url   = string ctx.Request.Path
            (if extra = "" then url else url[extra.Length..]).ToLowerInvariant()
        let await it = (Async.AwaitTask >> Async.RunSynchronously) it
        seq {
            debug (fun () -> $"Considering URL {textLink}")
            // Home page directory without the directory slash 
            if textLink = "" then yield redirectTo true (webLog.RelativeUrl Permalink.Empty)
            let permalink = Permalink textLink[1..]
            // Current post
            match data.Post.FindByPermalink permalink webLog.Id |> await with
            | Some post ->
                debug (fun () -> "Found post by permalink")
                if post.Status = Published || Option.isSome ctx.UserAccessLevel then
                    if ctx.Request.Query.ContainsKey "chapters" then
                        yield Post.chapters post
                    else
                        yield fun next ctx ->
                            Post.preparePostList webLog [ post ] Post.ListType.SinglePost "" 1 1 data
                            |> await
                            |> addToHash ViewContext.PageTitle post.Title
                            |> themedView (defaultArg post.Template "single-post") next ctx
            | None -> ()
            // Current page
            match data.Page.FindByPermalink permalink webLog.Id |> await with
            | Some page ->
                debug (fun () -> "Found page by permalink")
                yield fun next ctx ->
                    hashForPage page.Title
                    |> addToHash "page"             (DisplayPage.FromPage webLog page)
                    |> addToHash ViewContext.IsPage true
                    |> themedView (defaultArg page.Template "single-page") next ctx
            | None -> ()
            // RSS feed
            match Feed.deriveFeedType ctx textLink with
            | Some (feedType, postCount) ->
                debug (fun () -> "Found RSS feed")
                yield Feed.generate feedType postCount 
            | None -> ()
            // Post differing only by trailing slash
            let altLink =
                Permalink (if textLink.EndsWith "/" then textLink[1..textLink.Length - 2] else $"{textLink[1..]}/")
            match data.Post.FindByPermalink altLink webLog.Id |> await with
            | Some post ->
                debug (fun () -> "Found post by trailing-slash-agnostic permalink")
                yield redirectTo true (webLog.RelativeUrl post.Permalink)
            | None -> ()
            // Page differing only by trailing slash
            match data.Page.FindByPermalink altLink webLog.Id |> await with
            | Some page ->
                debug (fun () -> "Found page by trailing-slash-agnostic permalink")
                yield redirectTo true (webLog.RelativeUrl page.Permalink)
            | None -> ()
            // Prior post
            match data.Post.FindCurrentPermalink [ permalink; altLink ] webLog.Id |> await with
            | Some link ->
                debug (fun () -> "Found post by prior permalink")
                yield redirectTo true (webLog.RelativeUrl link)
            | None -> ()
            // Prior page
            match data.Page.FindCurrentPermalink [ permalink; altLink ] webLog.Id |> await with
            | Some link ->
                debug (fun () -> "Found page by prior permalink")
                yield redirectTo true (webLog.RelativeUrl link)
            | None -> ()
            debug (fun () -> "No content found")
        }

    // GET {all-of-the-above}
    let route : HttpHandler = fun next ctx ->
        match deriveAction ctx |> Seq.tryHead with Some handler -> handler next ctx | None -> Error.notFound next ctx


/// Serve theme assets
module Asset =
    
    // GET /theme/{theme}/{**path}
    let serve (urlParts: string seq) : HttpHandler = fun next ctx -> task {
        let path = urlParts |> Seq.skip 1 |> Seq.head
        match! ctx.Data.ThemeAsset.FindById(ThemeAssetId.Parse path) with
        | Some asset ->
            match Upload.checkModified asset.UpdatedOn ctx with
            | Some threeOhFour -> return! threeOhFour next ctx
            | None -> return! Upload.sendFile (asset.UpdatedOn.ToDateTimeUtc()) path asset.Data next ctx
        | None -> return! Error.notFound next ctx
    }


/// The primary myWebLog router
let router : HttpHandler = choose [
    GET_HEAD >=> choose [
        route "/" >=> Post.home
    ]
    subRoute "/admin" (requireUser >=> choose [
        GET_HEAD >=> choose [
            route    "/administration" >=> Admin.Dashboard.admin
            subRoute "/categor" (requireAccess WebLogAdmin >=> choose [
                route  "ies"       >=> Admin.Category.all
                routef "y/%s/edit"     Admin.Category.edit
            ])
            route    "/dashboard" >=> Admin.Dashboard.user
            route    "/my-info"   >=> User.myInfo
            subRoute "/page" (choose [
                route  "s"                       >=> Page.all 1
                routef "s/page/%i"                   Page.all
                routef "/%s/edit"                    Page.edit
                routef "/%s/permalinks"              Page.editPermalinks
                routef "/%s/revision/%s/preview"     Page.previewRevision
                routef "/%s/revisions"               Page.editRevisions
            ])
            subRoute "/post" (choose [
                route  "s"                       >=> Post.all 1
                routef "s/page/%i"                   Post.all
                routef "/%s/edit"                    Post.edit
                routef "/%s/permalinks"              Post.editPermalinks
                routef "/%s/revision/%s/preview"     Post.previewRevision
                routef "/%s/revisions"               Post.editRevisions
                routef "/%s/chapter/%i"              Post.editChapter
                routef "/%s/chapters"                Post.manageChapters
            ])
            subRoute "/settings" (requireAccess WebLogAdmin >=> choose [
                route    ""             >=> Admin.WebLog.settings
                routef   "/rss/%s/edit"     Feed.editCustomFeed
                subRoute "/redirect-rules" (choose [
                    route  ""    >=> Admin.RedirectRules.all
                    routef "/%i"     Admin.RedirectRules.edit
                ])
                subRoute "/tag-mapping" (choose [
                    route  "s"        >=> Admin.TagMapping.all
                    routef "/%s/edit"     Admin.TagMapping.edit
                ])
                subRoute "/user" (choose [
                    route  "s"        >=> User.all
                    routef "/%s/edit"     User.edit
                ])
            ])
            subRoute "/theme" (choose [
                route "/list" >=> Admin.Theme.all
                route "/new"  >=> Admin.Theme.add
            ])
            subRoute "/upload" (choose [
                route "s"    >=> Upload.list
                route "/new" >=> Upload.showNew
            ])
        ]
        POST >=> validateCsrf >=> choose [
            subRoute "/cache" (choose [
                routef "/theme/%s/refresh"   Admin.Cache.refreshTheme
                routef "/web-log/%s/refresh" Admin.Cache.refreshWebLog
            ])
            subRoute "/category" (requireAccess WebLogAdmin >=> choose [
                route  "/save"      >=> Admin.Category.save
                routef "/%s/delete"     Admin.Category.delete
            ])
            route    "/my-info" >=> User.saveMyInfo
            subRoute "/page" (choose [
                route  "/save"                   >=> Page.save
                route  "/permalinks"             >=> Page.savePermalinks
                routef "/%s/revision/%s/restore"     Page.restoreRevision
            ])
            subRoute "/post" (choose [
                route  "/save"                   >=> Post.save
                route  "/permalinks"             >=> Post.savePermalinks
                routef "/%s/chapter/%i"              Post.saveChapter
                routef "/%s/revision/%s/restore"     Post.restoreRevision
            ])
            subRoute "/settings" (requireAccess WebLogAdmin >=> choose [
                route "" >=> Admin.WebLog.saveSettings
                subRoute "/rss" (choose [
                    route  ""      >=> Feed.saveSettings
                    route  "/save" >=> Feed.saveCustomFeed
                ])
                subRoute "/redirect-rules" (choose [
                    routef "/%i"      Admin.RedirectRules.save
                    routef "/%i/up"   Admin.RedirectRules.moveUp
                    routef "/%i/down" Admin.RedirectRules.moveDown
                ])
                route "/tag-mapping/save" >=> Admin.TagMapping.save
                route "/user/save"        >=> User.save
            ])
            subRoute "/theme" (choose [
                route  "/new"       >=> Admin.Theme.save
                routef "/%s/delete"     Admin.Theme.delete
            ])
            route "/upload/save" >=> Upload.save
        ]
        DELETE >=> validateCsrf >=> choose [
            routef "/category/%s" Admin.Category.delete
            subRoute "/page" (choose [
                routef "/%s"             Page.delete
                routef "/%s/revision/%s" Page.deleteRevision
                routef "/%s/revisions"   Page.purgeRevisions
            ])
            subRoute "/post" (choose [
                routef "/%s"             Post.delete
                routef "/%s/chapter/%i"  Post.deleteChapter
                routef "/%s/revision/%s" Post.deleteRevision
                routef "/%s/revisions"   Post.purgeRevisions
            ])
            subRoute "/settings" (requireAccess WebLogAdmin >=> choose [
                routef "/redirect-rules/%i" Admin.RedirectRules.delete
                routef "/rss/%s"            Feed.deleteCustomFeed
                routef "/tag-mapping/%s"    Admin.TagMapping.delete
                routef "/user/%s"           User.delete
            ])
            subRoute "/upload" (requireAccess WebLogAdmin >=> choose [
                routexp "/disk/(.*)" Upload.deleteFromDisk
                routef  "/%s"        Upload.deleteFromDb
            ])
        ]
    ])
    GET_HEAD >=> routexp "/category/(.*)"  Post.pageOfCategorizedPosts
    GET_HEAD >=> routef  "/page/%i"        Post.pageOfPosts
    GET_HEAD >=> routef  "/page/%i/"       Post.redirectToPageOfPosts       
    GET_HEAD >=> routexp "/tag/(.*)"       Post.pageOfTaggedPosts
    GET_HEAD >=> routexp "/themes/(.*)"    Asset.serve
    GET_HEAD >=> routexp "/upload/(.*)"    Upload.serve
    subRoute "/user" (choose [
        GET_HEAD >=> choose [
            route "/log-on"  >=> User.logOn None
            route "/log-off" >=> User.logOff
        ]
        POST >=> validateCsrf >=> choose [
            route "/log-on" >=> User.doLogOn
        ]
    ])
    GET_HEAD >=> CatchAll.route
    Error.notFound
]

/// Wrap a router in a sub-route
let routerWithPath extraPath : HttpHandler =
    subRoute extraPath router

/// Handler to apply Giraffe routing with a possible sub-route
let handleRoute : HttpHandler = fun next ctx ->
    let extraPath = ctx.WebLog.ExtraPath
    (if extraPath = "" then router else routerWithPath extraPath) next ctx


open Giraffe.EndpointRouting

/// Endpoint-routed handler to deal with sub-routes
let endpoint = [ route "{**url}" handleRoute ]
