/// Routes for this application
module MyWebLog.Handlers.Routes

open Giraffe
open MyWebLog

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
            route    "/settings" >=> Admin.settings
            subRoute "/tag-mapping" (choose [
                route  "s"        >=> Admin.tagMappings
                routef "/%s/edit"     Admin.editMapping
            ])
            route    "/user/edit" >=> User.edit
        ]
        POST >=> choose [
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
            route    "/settings" >=> Admin.saveSettings
            subRoute "/tag-mapping" (choose [
                route  "/save"      >=> Admin.saveMapping
                routef "/%s/delete"     Admin.deleteMapping
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
        POST >=> choose [
            route "/log-on" >=> User.doLogOn
        ]
    ])
    GET >=> Post.catchAll
    Error.notFound
]

/// Wrap a router in a sub-route
let routerWithPath extraPath : HttpHandler =
    subRoute extraPath router

/// Handler to apply Giraffe routing with a possible sub-route
let handleRoute : HttpHandler = fun next ctx -> task {
    let _, extraPath = WebLog.hostAndPath (webLog ctx)
    return! (if extraPath = "" then router else routerWithPath extraPath) next ctx
}

open Giraffe.EndpointRouting

/// Endpoint-routed handler to deal with sub-routes
let endpoint = [ route "{**url}" handleRoute ]
