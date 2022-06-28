/// Routes for this application
module MyWebLog.Handlers.Routes

open Giraffe
open Microsoft.AspNetCore.Http
open MyWebLog

/// Module to resolve routes that do not match any other known route (web blog content) 
module CatchAll =
    
    open DotLiquid
    open MyWebLog.ViewModels
    
    /// Sequence where the first returned value is the proper handler for the link
    let private deriveAction (ctx : HttpContext) : HttpHandler seq =
        let webLog   = ctx.WebLog
        let data     = ctx.Data
        let debug    = debug "Routes.CatchAll" ctx
        let textLink =
            let _, extra = WebLog.hostAndPath webLog
            let url      = string ctx.Request.Path
            (if extra = "" then url else url.Substring extra.Length).ToLowerInvariant ()
        let await it = (Async.AwaitTask >> Async.RunSynchronously) it
        seq {
            debug (fun () -> $"Considering URL {textLink}")
            // Home page directory without the directory slash 
            if textLink = "" then yield redirectTo true (WebLog.relativeUrl webLog Permalink.empty)
            let permalink = Permalink (textLink.Substring 1)
            // Current post
            match data.Post.findByPermalink permalink webLog.id |> await with
            | Some post ->
                debug (fun () -> $"Found post by permalink")
                let model = Post.preparePostList webLog [ post ] Post.ListType.SinglePost "" 1 1 ctx data |> await
                model.Add ("page_title", post.title)
                yield fun next ctx -> themedView (defaultArg post.template "single-post") next ctx model
            | None -> ()
            // Current page
            match data.Page.findByPermalink permalink webLog.id |> await with
            | Some page ->
                debug (fun () -> $"Found page by permalink")
                yield fun next ctx ->
                    Hash.FromAnonymousObject {|
                        page       = DisplayPage.fromPage webLog page
                        categories = CategoryCache.get ctx
                        page_title = page.title
                        is_page    = true
                    |}
                    |> themedView (defaultArg page.template "single-page") next ctx
            | None -> ()
            // RSS feed
            match Feed.deriveFeedType ctx textLink with
            | Some (feedType, postCount) ->
                debug (fun () -> $"Found RSS feed")
                yield Feed.generate feedType postCount 
            | None -> ()
            // Post differing only by trailing slash
            let altLink =
                Permalink (if textLink.EndsWith "/" then textLink[1..textLink.Length - 2] else $"{textLink[1..]}/")
            match data.Post.findByPermalink altLink webLog.id |> await with
            | Some post ->
                debug (fun () -> $"Found post by trailing-slash-agnostic permalink")
                yield redirectTo true (WebLog.relativeUrl webLog post.permalink)
            | None -> ()
            // Page differing only by trailing slash
            match data.Page.findByPermalink altLink webLog.id |> await with
            | Some page ->
                debug (fun () -> $"Found page by trailing-slash-agnostic permalink")
                yield redirectTo true (WebLog.relativeUrl webLog page.permalink)
            | None -> ()
            // Prior post
            match data.Post.findCurrentPermalink [ permalink; altLink ] webLog.id |> await with
            | Some link ->
                debug (fun () -> $"Found post by prior permalink")
                yield redirectTo true (WebLog.relativeUrl webLog link)
            | None -> ()
            // Prior page
            match data.Page.findCurrentPermalink [ permalink; altLink ] webLog.id |> await with
            | Some link ->
                debug (fun () -> $"Found page by prior permalink")
                yield redirectTo true (WebLog.relativeUrl webLog link)
            | None -> ()
            debug (fun () -> $"No content found")
        }

    // GET {all-of-the-above}
    let route : HttpHandler = fun next ctx -> task {
        match deriveAction ctx |> Seq.tryHead with
        | Some handler -> return! handler next ctx
        | None -> return! Error.notFound next ctx
    }


/// Serve theme assets
module Asset =
    
    // GET /theme/{theme}/{**path}
    let serve (urlParts : string seq) : HttpHandler = fun next ctx -> task {
        let path = urlParts |> Seq.skip 1 |> Seq.head
        match! ctx.Data.ThemeAsset.findById (ThemeAssetId.ofString path) with
        | Some asset ->
            match Upload.checkModified asset.updatedOn ctx with
            | Some threeOhFour -> return! threeOhFour next ctx
            | None -> return! Upload.sendFile asset.updatedOn path asset.data next ctx
        | None -> return! Error.notFound next ctx
    }


/// The primary myWebLog router
let router : HttpHandler = choose [
    GET >=> choose [
        route "/" >=> Post.home
    ]
    subRoute "/admin" (requireUser >=> choose [
        GET >=> choose [
            subRoute "/categor" (choose [
                route  "ies"       >=> Admin.listCategories
                route  "ies/bare"  >=> Admin.listCategoriesBare
                routef "y/%s/edit"     Admin.editCategory
            ])
            route    "/dashboard" >=> Admin.dashboard
            subRoute "/page" (choose [
                route  "s"              >=> Admin.listPages 1
                routef "s/page/%i"          Admin.listPages
                routef "/%s/edit"           Admin.editPage
                routef "/%s/permalinks"     Admin.editPagePermalinks
            ])
            subRoute "/post" (choose [
                route  "s"              >=> Post.all 1
                routef "s/page/%i"          Post.all
                routef "/%s/edit"           Post.edit
                routef "/%s/permalinks"     Post.editPermalinks
            ])
            subRoute "/settings" (choose [
                route ""     >=> Admin.settings
                subRoute "/rss" (choose [
                    route  ""         >=> Feed.editSettings
                    routef "/%s/edit"     Feed.editCustomFeed
                ])
                subRoute "/tag-mapping" (choose [
                    route  "s"        >=> Admin.tagMappings
                    route  "s/bare"   >=> Admin.tagMappingsBare
                    routef "/%s/edit"     Admin.editMapping
                ])
            ])
            route    "/theme/update" >=> Admin.themeUpdatePage
            route    "/user/edit"    >=> User.edit
        ]
        POST >=> validateCsrf >=> choose [
            subRoute "/category" (choose [
                route  "/save"      >=> Admin.saveCategory
                routef "/%s/delete"     Admin.deleteCategory
            ])
            subRoute "/page" (choose [
                route  "/save"       >=> Admin.savePage
                route  "/permalinks" >=> Admin.savePagePermalinks
                routef "/%s/delete"      Admin.deletePage
            ])
            subRoute "/post" (choose [
                route  "/save"       >=> Post.save
                route  "/permalinks" >=> Post.savePermalinks
                routef "/%s/delete"      Post.delete
            ])
            subRoute "/settings" (choose [
                route ""     >=> Admin.saveSettings
                subRoute "/rss" (choose [
                    route  ""           >=> Feed.saveSettings
                    route  "/save"      >=> Feed.saveCustomFeed
                    routef "/%s/delete"     Feed.deleteCustomFeed
                ])
                subRoute "/tag-mapping" (choose [
                    route  "/save"      >=> Admin.saveMapping
                    routef "/%s/delete"     Admin.deleteMapping
                ])
            ])
            route    "/theme/update" >=> Admin.updateTheme
            route    "/user/save"    >=> User.save
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
let handleRoute : HttpHandler = fun next ctx -> task {
    let _, extraPath = WebLog.hostAndPath ctx.WebLog
    return! (if extraPath = "" then router else routerWithPath extraPath) next ctx
}

open Giraffe.EndpointRouting

/// Endpoint-routed handler to deal with sub-routes
let endpoint = [ route "{**url}" handleRoute ]
