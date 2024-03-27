module MyWebLog.Views.Admin

open Giraffe.Htmx.Common
open Giraffe.ViewEngine
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels

/// The administrator dashboard
let dashboard (themes: Theme list) app = [
    let templates      = TemplateCache.allNames ()
    let cacheBaseUrl   = relUrl app "admin/cache/"
    let webLogCacheUrl = $"{cacheBaseUrl}web-log/"
    let themeCacheUrl  = $"{cacheBaseUrl}theme/"
    let webLogDetail (webLog: WebLog) =
        let refreshUrl = $"{webLogCacheUrl}{webLog.Id}/refresh"
        div [ _class "row mwl-table-detail" ] [
            div [ _class "col" ] [
                txt webLog.Name; br []
                small [] [
                    span [ _class "text-muted" ] [ raw webLog.UrlBase ]; br []
                    a [ _href refreshUrl; _hxPost refreshUrl ] [ raw "Refresh" ]
                ]
            ]
        ]
    let themeDetail (theme: Theme) =
        let refreshUrl = $"{themeCacheUrl}{theme.Id}/refresh"
        div [ _class "row mwl-table-detail" ] [
            div [ _class "col-8" ] [
                txt theme.Name; br []
                small [] [
                    span [ _class "text-muted" ] [ txt (string theme.Id); raw " &bull; " ]
                    a [ _href refreshUrl; _hxPost refreshUrl ] [ raw "Refresh" ]
                ]
            ]
            div [ _class "col-4" ] [
                raw (templates |> List.filter _.StartsWith(string theme.Id) |> List.length |> string)
            ]
        ]

    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        fieldset [ _class "container mb-3 pb-0" ] [
            legend [] [ raw "Themes" ]
            span [ _hxGet (relUrl app "admin/theme/list"); _hxTrigger HxTrigger.Load; _hxSwap HxSwap.OuterHtml ] []
        ]
        fieldset [ _class "container mb-3 pb-0" ] [
            legend [] [ raw "Caches" ]
            p [ _class "pb-2" ] [
                raw "myWebLog uses a few caches to ensure that it serves pages as fast as possible. ("
                a [ _href "https://bitbadger.solutions/open-source/myweblog/advanced.html#cache-management"
                    _target "_blank" ] [
                    raw "more information"
                ]; raw ")"
            ]
            div [ _class "row" ] [
                div [ _class "col-12 col-lg-6 pb-3" ] [
                    div [ _class "card" ] [ 
                        header [ _class "card-header text-white bg-secondary" ] [ raw "Web Logs" ]
                        div [ _class "card-body pb-0" ] [
                            h6 [ _class "card-subtitle text-muted pb-3" ] [
                                raw "These caches include the page list and categories for each web log"
                            ]
                            let webLogUrl = $"{cacheBaseUrl}web-log/"
                            form [ _method "post"; _class "container g-0"; _hxNoBoost; _hxTarget "body"
                                   _hxSwap $"{HxSwap.InnerHtml} show:window:top" ] [
                                antiCsrf app
                                button [ _type "submit"; _class "btn btn-sm btn-primary mb-2"
                                         _hxPost $"{webLogUrl}all/refresh" ] [
                                    raw "Refresh All"
                                ]
                                div [ _class "row mwl-table-heading" ] [ div [ _class "col" ] [ raw "Web Log" ] ]
                                yield! WebLogCache.all () |> List.sortBy _.Name |> List.map webLogDetail
                            ]
                        ]
                    ]
                ]
                div [ _class "col-12 col-lg-6 pb-3" ] [
                    div [ _class "card" ] [
                        header [ _class "card-header text-white bg-secondary" ] [ raw "Themes" ]
                        div [ _class "card-body pb-0" ] [
                            h6 [ _class "card-subtitle text-muted pb-3" ] [
                                raw "The theme template cache is filled on demand as pages are displayed; "
                                raw "refreshing a theme with no cached templates will still refresh its asset cache"
                            ]
                            form [ _method "post"; _class "container g-0"; _hxNoBoost; _hxTarget "body"
                                   _hxSwap $"{HxSwap.InnerHtml} show:window:top" ] [
                                antiCsrf app
                                button [ _type "submit"; _class "btn btn-sm btn-primary mb-2"
                                         _hxPost $"{themeCacheUrl}all/refresh" ] [
                                    raw "Refresh All"
                                ]
                                div [ _class "row mwl-table-heading" ] [
                                    div [ _class "col-8" ] [ raw "Theme" ]; div [ _class "col-4" ] [ raw "Cached" ]
                                ]
                                yield! themes |> List.filter (fun t -> t.Id <> ThemeId "admin") |> List.map themeDetail
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]
]

/// Display a list of themes
let themeList (model: DisplayTheme list) app =
    let themeCol = "col-12 col-md-6"
    let slugCol  = "d-none d-md-block col-md-3"
    let tmplCol  = "d-none d-md-block col-md-3"
    div [ _id "theme_panel" ] [
        a [ _href (relUrl app "admin/theme/new"); _class "btn btn-primary btn-sm mb-3"; _hxTarget "#theme_new" ] [
            raw "Upload a New Theme"
        ]
        div [ _class "container g-0" ] [
            div [ _class "row mwl-table-heading" ] [
                div [ _class themeCol ] [ raw "Theme" ]
                div [ _class slugCol ] [ raw "Slug" ]
                div [ _class tmplCol ] [ raw "Templates" ]
            ]
        ]
        div [ _class "row mwl-table-detail"; _id "theme_new" ] []
        form [ _method "post"; _id "themeList"; _class "container g-0"; _hxTarget "#theme_panel"
               _hxSwap $"{HxSwap.OuterHtml} show:window:top" ] [
            antiCsrf app
            for theme in model do
                let url = relUrl app $"admin/theme/{theme.Id}"
                div [ _class "row mwl-table-detail"; _id $"theme_{theme.Id}" ] [
                    div [ _class $"{themeCol} no-wrap" ] [
                        txt theme.Name
                        if theme.IsInUse then span [ _class "badge bg-primary ms-2" ] [ raw "IN USE" ]
                        if not theme.IsOnDisk then
                            span [ _class "badge bg-warning text-dark ms-2" ] [ raw "NOT ON DISK" ]
                        br []
                        small [] [
                            span [ _class "text-muted" ] [ txt $"v{theme.Version}" ]
                            if not (theme.IsInUse || theme.Id = "default") then
                                span [ _class "text-muted" ] [ raw " &bull; " ]
                                a [ _href url; _hxDelete url; _class "text-danger"
                                    _hxConfirm $"Are you sure you want to delete the theme “{theme.Name}”? This action cannot be undone." ] [
                                    raw "Delete"
                                ]
                            span [ _class "d-md-none text-muted" ] [
                                br []; raw "Slug: "; txt theme.Id; raw $" &bull; {theme.TemplateCount} Templates"
                            ]
                        ]
                    ]
                    div [ _class slugCol ] [ txt (string theme.Id) ]
                    div [ _class tmplCol ] [ txt (string theme.TemplateCount) ]
                ]
        ]
    ]
    |> List.singleton


/// Form to allow a theme to be uploaded
let themeUpload app =
    div [ _class "col" ] [
        h5 [ _class "mt-2" ] [ raw app.PageTitle ]
        form [ _action (relUrl app "admin/theme/new"); _method "post"; _class "container"
               _enctype "multipart/form-data"; _hxNoBoost ] [
            antiCsrf app
            div [ _class "row " ] [
                div [ _class "col-12 col-sm-6 pb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "file"; _id "file"; _name "file"; _class "form-control"; _accept ".zip"
                                _placeholder "Theme File"; _required ]
                        label [ _for "file" ] [ raw "Theme File" ]
                    ]
                ]
                div [ _class "col-12 col-sm-6 pb-3 d-flex justify-content-center align-items-center" ] [
                    div [ _class "form-check form-switch pb-2" ] [
                        input [ _type "checkbox"; _name "DoOverwrite"; _id "doOverwrite"; _class "form-check-input"
                                _value "true" ]
                        label [ _for "doOverwrite"; _class "form-check-label" ] [ raw "Overwrite" ]
                    ]
                ]
            ]
            div [ _class "row pb-3" ] [
                div [ _class "col text-center" ] [
                    button [ _type "submit"; _class "btn btn-sm btn-primary" ] [ raw "Upload Theme" ]; raw " &nbsp; "
                    button [ _type "button"; _class "btn btn-sm btn-secondary ms-3"
                             _onclick "document.getElementById('theme_new').innerHTML = ''" ] [
                        raw "Cancel"
                    ]
                ]
            ]
        ]
    ]
    |> List.singleton
