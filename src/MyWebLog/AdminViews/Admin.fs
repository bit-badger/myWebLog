module MyWebLog.AdminViews.Admin

open Giraffe.ViewEngine
open MyWebLog.ViewModels

/// The main dashboard
let dashboard (model: DashboardModel) app = [
    h2 [ _class "my-3" ] [ txt app.WebLog.Name; raw " &bull; Dashboard" ]
    article [ _class "container" ] [
        div [ _class "row" ] [
            section [ _class "col-lg-5 offset-lg-1 col-xl-4 offset-xl-2 pb-3" ] [
                div [ _class "card" ] [
                    header [ _class "card-header text-white bg-primary" ] [ raw "Posts" ]
                    div [ _class "card-body" ] [
                        h6 [ _class "card-subtitle text-muted pb-3" ] [
                            raw "Published "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Posts) ]
                            raw "&nbsp; Drafts "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Drafts) ]
                        ]
                        if app.IsAuthor then
                            a [ _href (relUrl app "admin/posts"); _class "btn btn-secondary me-2" ] [ raw "View All" ]
                            a [ _href (relUrl app "admin/post/new/edit"); _class "btn btn-primary" ] [
                                raw "Write a New Post"
                            ]
                    ]
                ]
            ]
            section [ _class "col-lg-5 col-xl-4 pb-3" ] [
                div [ _class "card" ] [
                    header [ _class "card-header text-white bg-primary" ]  [ raw "Pages" ]
                    div [ _class "card-body" ] [
                        h6 [ _class "card-subtitle text-muted pb-3" ] [
                            raw "All "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Pages) ]
                            raw "&nbsp; Shown in Page List "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.ListedPages) ]
                        ]
                        if app.IsAuthor then
                            a [ _href (relUrl app "admin/pages"); _class "btn btn-secondary me-2" ] [ raw "View All" ]
                            a [ _href (relUrl app "admin/page/new/edit"); _class "btn btn-primary" ] [
                                raw "Create a New Page"
                            ]
                    ]
                ]
            ]
        ]
        div [ _class "row" ] [
            section [ _class "col-lg-5 offset-lg-1 col-xl-4 offset-xl-2 pb-3" ] [
                div [ _class "card" ] [
                    header [ _class "card-header text-white bg-secondary" ] [ raw "Categories" ]
                    div [ _class "card-body" ] [
                        h6 [ _class "card-subtitle text-muted pb-3"] [
                            raw "All "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Categories) ]
                            raw "&nbsp; Top Level "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.TopLevelCategories) ]
                        ]
                        if app.IsWebLogAdmin then
                            a [ _href (relUrl app "admin/categories"); _class "btn btn-secondary me-2" ] [
                                raw "View All"
                            ]
                            a [ _href (relUrl app "admin/category/new/edit"); _class "btn btn-secondary" ] [
                                raw "Add a New Category"
                            ]
                    ]
                ]
            ]
        ]
        if app.IsWebLogAdmin then
            div [ _class "row pb-3" ] [
                div [ _class "col text-end" ] [
                    a [ _href (relUrl app "admin/settings"); _class "btn btn-secondary" ] [ raw "Modify Settings" ]
                ]
            ]
    ]
]
