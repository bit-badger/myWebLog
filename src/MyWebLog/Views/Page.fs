module MyWebLog.Views.Page

open Giraffe.ViewEngine
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels

/// Display a list of pages for this web log
let pageList (pages: DisplayPage list) pageNbr hasNext app = [
    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        a [ _href (relUrl app "admin/page/new/edit"); _class "btn btn-primary btn-sm mb-3" ] [ raw "Create a New Page" ]
        if pages.Length = 0 then
            p [ _class "text-muted fst-italic text-center" ] [ raw "This web log has no pages" ]
        else
            let titleCol = "col-12 col-md-5"
            let linkCol  = "col-12 col-md-5"
            let upd8Col  = "col-12 col-md-2"
            form [ _method "post"; _class "container mb-3"; _hxTarget "body" ] [
                antiCsrf app
                div [ _class "row mwl-table-heading" ] [
                    div [ _class titleCol ] [
                        span [ _class "d-none d-md-inline" ] [ raw "Title" ]; span [ _class "d-md-none" ] [ raw "Page" ]
                    ]
                    div [ _class $"{linkCol} d-none d-md-inline-block" ] [ raw "Permalink" ]
                    div [ _class $"{upd8Col} d-none d-md-inline-block" ] [ raw "Updated" ]
                ]
                for pg in pages do
                    let pageLink = if pg.IsDefault then "" else pg.Permalink
                    div [ _class "row mwl-table-detail" ] [
                        div [ _class titleCol ] [
                            txt pg.Title
                            if pg.IsDefault then
                                raw " &nbsp; "; span [ _class "badge bg-success" ] [ raw "HOME PAGE" ]
                            if pg.IsInPageList then
                                raw " &nbsp; "; span [ _class "badge bg-primary" ] [ raw "IN PAGE LIST" ]
                            br [] ; small [] [
                                let adminUrl = relUrl app $"admin/page/{pg.Id}"
                                a [ _href (relUrl app pageLink); _target "_blank" ] [ raw "View Page" ]
                                if app.IsEditor || (app.IsAuthor && app.UserId.Value = WebLogUserId pg.AuthorId) then
                                    span [ _class "text-muted" ] [ raw " &bull; " ]
                                    a [ _href $"{adminUrl}/edit" ] [ raw "Edit" ]
                                if app.IsWebLogAdmin then
                                    span [ _class "text-muted" ] [ raw " &bull; " ]
                                    a [ _href adminUrl; _hxDelete adminUrl; _class "text-danger"
                                        _hxConfirm $"Are you sure you want to delete the page &ldquo;{pg.Title}&rdquo;? This action cannot be undone." ] [
                                        raw "Delete"
                                    ]
                            ]
                        ]
                        div [ _class linkCol ] [
                            small [ _class "d-md-none" ] [ txt pageLink ]
                            span [ _class "d-none d-md-inline" ] [ txt pageLink ]
                        ]
                        div [ _class upd8Col ] [
                            small [ _class "d-md-none text-muted" ] [
                                raw "Updated "; txt (pg.UpdatedOn.ToString "MMMM d, yyyy")
                            ]
                            span [ _class "d-none d-md-inline" ] [ txt (pg.UpdatedOn.ToString "MMMM d, yyyy") ]
                        ]
                    ]
            ]
            if pageNbr > 1 || hasNext then
                div [ _class "d-flex justify-content-evenly mb-3" ] [
                    div [] [
                        if pageNbr > 1 then
                            let prevPage = if pageNbr = 2 then "" else $"/page/{pageNbr - 1}"
                            p [] [
                                a [ _class "btn btn-secondary"; _href (relUrl app $"admin/pages{prevPage}") ] [
                                    raw "&laquo; Previous"
                                ]
                            ]
                    ]
                    div [ _class "text-right" ] [
                        if hasNext then
                            p [] [
                                a [ _class "btn btn-secondary"; _href (relUrl app $"admin/pages/page/{pageNbr + 1}") ] [
                                    raw "Next &raquo;"
                                ]
                            ]
                    ]
                ]
    ]
]
