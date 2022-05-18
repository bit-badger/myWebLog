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
            route ""          Admin.dashboard
            route "/settings" Admin.settings
        ]
        POST [
            route "/settings" Admin.saveSettings
        ]
    ]
    subRoute "/categor" [
        GET [
            route  "ies"        Category.all
            routef "y/%s/edit"  Category.edit
            route  "y/{**slug}" Post.pageOfCategorizedPosts
        ]
        POST [
            route  "y/save"      Category.save
            routef "y/%s/delete" Category.delete
        ]
    ]
    subRoute "/page" [
        GET [
            routef "/%d"       Post.pageOfPosts
            //routef "/%d/"      (fun pg -> redirectTo true $"/page/{pg}")
            routef "/%s/edit"  Page.edit
            route  "s"         (Page.all 1)
            routef "s/page/%d" Page.all
        ]
        POST [
            route "/save" Page.save
        ]
    ]
    subRoute "/post" [
        GET [
            routef "/%s/edit"  Post.edit
            route  "s"         (Post.all 1)
            routef "s/page/%d" Post.all
        ]
        POST [
            route "/save" Post.save
        ]
    ]
    subRoute "/tag" [
        GET [
            route "/{**slug}" Post.pageOfTaggedPosts
        ]
    ]
    subRoute "/user" [
        GET [
            route "/edit"    User.edit
            route "/log-on"  (User.logOn None)
            route "/log-off" User.logOff
        ]
        POST [
            route "/log-on" User.doLogOn
            route "/save"   User.save
        ]
    ]
    route "{**link}" Post.catchAll
]
