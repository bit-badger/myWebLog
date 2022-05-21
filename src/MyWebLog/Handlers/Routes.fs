/// Routes for this application
module MyWebLog.Handlers.Routes

open Giraffe.EndpointRouting

/// The endpoints defined in the above handlers
let endpoints = [
    GET [
        route "/" Post.home
    ]
    subRoute "/admin" [
        GET [
            route    "" Admin.dashboard
            subRoute "/categor" [
                route  "ies"       Admin.listCategories
                routef "y/%s/edit" Admin.editCategory
            ]
            subRoute "/page" [
                route  "s"              (Admin.listPages 1)
                routef "s/page/%d"      Admin.listPages
                routef "/%s/edit"       Admin.editPage
                routef "/%s/permalinks" Admin.editPagePermalinks
            ]
            subRoute "/post" [
                route  "s"              (Post.all 1)
                routef "s/page/%d"      Post.all
                routef "/%s/edit"       Post.edit
                routef "/%s/permalinks" Post.editPermalinks
            ]
            route    "/settings" Admin.settings
            subRoute "/tag-mapping" [
                route  "s"        Admin.tagMappings
                routef "/%s/edit" Admin.editMapping
            ]
            route    "/user/edit" User.edit
        ]
        POST [
            subRoute "/category" [
                route  "/save"      Admin.saveCategory
                routef "/%s/delete" Admin.deleteCategory
            ]
            subRoute "/page" [
                route  "/save"       Admin.savePage
                route  "/permalinks" Admin.savePagePermalinks
                routef "/%s/delete"  Admin.deletePage
            ]
            subRoute "/post" [
                route  "/save"       Post.save
                route  "/permalinks" Post.savePermalinks
                routef "/%s/delete"  Post.delete
            ]
            route    "/settings" Admin.saveSettings
            subRoute "/tag-mapping" [
                route  "/save"      Admin.saveMapping
                routef "/%s/delete" Admin.deleteMapping
            ]
            route    "/user/save" User.save
        ]
    ]
    GET [
        route  "/category/{**slug}" Post.pageOfCategorizedPosts
        routef "/page/%d"           Post.pageOfPosts
        route  "/tag/{**slug}"      Post.pageOfTaggedPosts
    ]
    subRoute "/user" [
        GET [
            route "/log-on"  (User.logOn None)
            route "/log-off" User.logOff
        ]
        POST [
            route "/log-on" User.doLogOn
        ]
    ]
    route "{**link}" Post.catchAll
]
