/// Routes for this application
module MyWebLog.Handlers.Routes

open Giraffe
open MyWebLog

/// Module to resolve routes that do not match any other known route (web blog content) 
module CatchAll =
    
    open DotLiquid
    open Microsoft.AspNetCore.Http
    open MyWebLog.ViewModels
    
    /// Sequence where the first returned value is the proper handler for the link
    let private deriveAction (ctx : HttpContext) : HttpHandler seq =
        let webLog   = ctx.WebLog
        let conn     = ctx.Conn
        let textLink =
            let _, extra = WebLog.hostAndPath webLog
            let url      = string ctx.Request.Path
            (if extra = "" then url else url.Substring extra.Length).ToLowerInvariant ()
        let await it = (Async.AwaitTask >> Async.RunSynchronously) it
        seq {
            debug "Post" ctx (fun () -> $"Considering URL {textLink}")
            // Home page directory without the directory slash 
            if textLink = "" then yield redirectTo true (WebLog.relativeUrl webLog Permalink.empty)
            let permalink = Permalink (textLink.Substring 1)
            // Current post
            match Data.Post.findByPermalink permalink webLog.id conn |> await with
            | Some post ->
                let model = Post.preparePostList webLog [ post ] Post.ListType.SinglePost "" 1 1 ctx conn |> await
                model.Add ("page_title", post.title)
                yield fun next ctx -> themedView "single-post" next ctx model
            | None -> ()
            // Current page
            match Data.Page.findByPermalink permalink webLog.id conn |> await with
            | Some page ->
                yield fun next ctx ->
                    Hash.FromAnonymousObject {| page = DisplayPage.fromPage webLog page; page_title = page.title |}
                    |> themedView (defaultArg page.template "single-page") next ctx
            | None -> ()
            // RSS feed
            match Feed.deriveFeedType ctx textLink with
            | Some (feedType, postCount) -> yield Feed.generate feedType postCount 
            | None -> ()
            // Post differing only by trailing slash
            let altLink = Permalink (if textLink.EndsWith "/" then textLink[..textLink.Length - 2] else $"{textLink}/")
            match Data.Post.findByPermalink altLink webLog.id conn |> await with
            | Some post -> yield redirectTo true (WebLog.relativeUrl webLog post.permalink)
            | None -> ()
            // Page differing only by trailing slash
            match Data.Page.findByPermalink altLink webLog.id conn |> await with
            | Some page -> yield redirectTo true (WebLog.relativeUrl webLog page.permalink)
            | None -> ()
            // Prior post
            match Data.Post.findCurrentPermalink [ permalink; altLink ] webLog.id conn |> await with
            | Some link -> yield redirectTo true (WebLog.relativeUrl webLog link)
            | None -> ()
            // Prior page
            match Data.Page.findCurrentPermalink [ permalink; altLink ] webLog.id conn |> await with
            | Some link -> yield redirectTo true (WebLog.relativeUrl webLog link)
            | None -> ()
        }

    // GET {all-of-the-above}
    let route : HttpHandler = fun next ctx -> task {
        match deriveAction ctx |> Seq.tryHead with
        | Some handler -> return! handler next ctx
        | None -> return! Error.notFound next ctx
    }

/// The primary myWebLog router
let router : HttpHandler = choose [
    GET >=> choose [
        route "/" >=> Post.home
    ]
    subRoute "/admin" (requireUser >=> choose [
        GET >=> choose [
            route    "" >=> Admin.dashboard
            subRoute "/categor" (choose [
                route  "ies"       >=> Admin.listCategories
                routef "y/%s/edit"     Admin.editCategory
            ])
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
                route "/rss" >=> Feed.editSettings
                subRoute "/tag-mapping" (choose [
                    route  "s"        >=> Admin.tagMappings
                    routef "/%s/edit"     Admin.editMapping
                ])
            ])
            route    "/user/edit" >=> User.edit
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
                    routef "/%s/delete"     Feed.deleteCustomFeed
                ])
                subRoute "/tag-mapping" (choose [
                    route  "/save"      >=> Admin.saveMapping
                    routef "/%s/delete"     Admin.deleteMapping
                ])
            ])
            route    "/user/save" >=> User.save
        ]
    ])
    GET >=> routexp "/category/(.*)"  Post.pageOfCategorizedPosts
    GET >=> routef  "/page/%i"        Post.pageOfPosts
    GET >=> routexp "/tag/(.*)"       Post.pageOfTaggedPosts
    subRoute "/user" (choose [
        GET >=> choose [
            route "/log-on"  >=> User.logOn None
            route "/log-off" >=> User.logOff
        ]
        POST >=> validateCsrf >=> choose [
            route "/log-on" >=> User.doLogOn
        ]
    ])
    GET >=> CatchAll.route
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
