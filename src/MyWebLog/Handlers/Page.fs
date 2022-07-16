/// Handlers to manipulate pages
module MyWebLog.Handlers.Page

open DotLiquid
open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// GET /admin/pages
// GET /admin/pages/page/{pageNbr}
let all pageNbr : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let! pages  = ctx.Data.Page.findPageOfPages webLog.id pageNbr
    return!
        Hash.FromAnonymousObject {|
            page_title = "Pages"
            csrf       = ctx.CsrfTokenSet
            pages      = pages |> List.map (DisplayPage.fromPageMinimal webLog)
            page_nbr   = pageNbr
            prev_page  = if pageNbr = 2 then "" else $"/page/{pageNbr - 1}"
            next_page  = $"/page/{pageNbr + 1}"
        |}
        |> viewForTheme "admin" "page-list" next ctx
}

// GET /admin/page/{id}/edit
let edit pgId : HttpHandler = fun next ctx -> task {
    let! result = task {
        match pgId with
        | "new" -> return Some ("Add a New Page", { Page.empty with id = PageId "new" })
        | _ ->
            match! ctx.Data.Page.findFullById (PageId pgId) ctx.WebLog.id with
            | Some page -> return Some ("Edit Page", page)
            | None -> return None
    }
    match result with
    | Some (title, page) ->
        let  model     = EditPageModel.fromPage page
        let! templates = templatesForTheme ctx "page"
        return!
            Hash.FromAnonymousObject {|
                page_title = title
                csrf       = ctx.CsrfTokenSet
                model      = model
                metadata   = Array.zip model.metaNames model.metaValues
                             |> Array.mapi (fun idx (name, value) -> [| string idx; name; value |])
                templates  = templates
            |}
            |> viewForTheme "admin" "page-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/delete
let delete pgId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match! ctx.Data.Page.delete (PageId pgId) webLog.id with
    | true ->
        do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Page deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Page not found; nothing deleted" }
    return! redirectToGet "admin/pages" next ctx
}

// GET /admin/page/{id}/permalinks
let editPermalinks pgId : HttpHandler = fun next ctx -> task {
    match! ctx.Data.Page.findFullById (PageId pgId) ctx.WebLog.id with
    | Some pg ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Prior Permalinks"
                csrf       = ctx.CsrfTokenSet
                model      = ManagePermalinksModel.fromPage pg
            |}
            |> viewForTheme "admin" "permalinks" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/permalinks
let savePermalinks : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let! model  = ctx.BindFormAsync<ManagePermalinksModel> ()
    let  links  = model.prior |> Array.map Permalink |> List.ofArray
    match! ctx.Data.Page.updatePriorPermalinks (PageId model.id) webLog.id links with
    | true ->
        do! addMessage ctx { UserMessage.success with message = "Page permalinks saved successfully" }
        return! redirectToGet $"admin/page/{model.id}/permalinks" next ctx
    | false -> return! Error.notFound next ctx
}

// GET /admin/page/{id}/revisions
let editRevisions pgId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match! ctx.Data.Page.findFullById (PageId pgId) webLog.id with
    | Some pg ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Page Revisions"
                csrf       = ctx.CsrfTokenSet
                model      = ManageRevisionsModel.fromPage webLog pg
            |}
            |> viewForTheme "admin" "revisions" next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/page/{id}/revisions/purge
let purgeRevisions pgId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let data   = ctx.Data
    match! data.Page.findFullById (PageId pgId) webLog.id with
    | Some pg ->
        do! data.Page.update { pg with revisions = [ List.head pg.revisions ] }
        do! addMessage ctx { UserMessage.success with message = "Prior revisions purged successfully" }
        return! redirectToGet $"admin/page/{pgId}/revisions" next ctx
    | None -> return! Error.notFound next ctx
}

open Microsoft.AspNetCore.Http

/// Find the page and the requested revision
let private findPageRevision pgId revDate (ctx : HttpContext) = task {
    match! ctx.Data.Page.findFullById (PageId pgId) ctx.WebLog.id with
    | Some pg ->
        let asOf = parseToUtc revDate
        return Some pg, pg.revisions |> List.tryFind (fun r -> r.asOf = asOf)
    | None -> return None, None
}

// GET /admin/page/{id}/revision/{revision-date}/preview
let previewRevision (pgId, revDate) : HttpHandler = fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some _, Some rev ->
        return!
            Hash.FromAnonymousObject {|
                content = $"""<div class="mwl-revision-preview mb-3">{MarkupText.toHtml rev.text}</div>"""
            |}
            |> bareForTheme "admin" "" next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

open System

// POST /admin/page/{id}/revision/{revision-date}/restore
let restoreRevision (pgId, revDate) : HttpHandler = fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev ->
        do! ctx.Data.Page.update
                { pg with
                    revisions = { rev with asOf = DateTime.UtcNow }
                                  :: (pg.revisions |> List.filter (fun r -> r.asOf <> rev.asOf))
                }
        do! addMessage ctx { UserMessage.success with message = "Revision restored successfully" }
        return! redirectToGet $"admin/page/{pgId}/revisions" next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/revision/{revision-date}/delete
let deleteRevision (pgId, revDate) : HttpHandler = fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev ->
        do! ctx.Data.Page.update { pg with revisions = pg.revisions |> List.filter (fun r -> r.asOf <> rev.asOf) }
        do! addMessage ctx { UserMessage.success with message = "Revision deleted successfully" }
        return! bareForTheme "admin" "" next ctx (Hash.FromAnonymousObject {| content = "" |})
    | None, _
    | _, None -> return! Error.notFound next ctx
}

#nowarn "3511"

// POST /admin/page/save
let save : HttpHandler = fun next ctx -> task {
    let! model  = ctx.BindFormAsync<EditPageModel> ()
    let  webLog = ctx.WebLog
    let  data   = ctx.Data
    let  now    = DateTime.UtcNow
    let! pg     = task {
        match model.pageId with
        | "new" ->
            return Some
                { Page.empty with
                    id          = PageId.create ()
                    webLogId    = webLog.id
                    authorId    = ctx.UserId
                    publishedOn = now
                }
        | pgId -> return! data.Page.findFullById (PageId pgId) webLog.id
    }
    match pg with
    | Some page ->
        let updateList = page.showInPageList <> model.isShownInPageList
        let revision   = { asOf = now; text = MarkupText.parse $"{model.source}: {model.text}" }
        // Detect a permalink change, and add the prior one to the prior list
        let page =
            match Permalink.toString page.permalink with
            | "" -> page
            | link when link = model.permalink -> page
            | _ -> { page with priorPermalinks = page.permalink :: page.priorPermalinks }
        let page =
            { page with
                title          = model.title
                permalink      = Permalink model.permalink
                updatedOn      = now
                showInPageList = model.isShownInPageList
                template       = match model.template with "" -> None | tmpl -> Some tmpl
                text           = MarkupText.toHtml revision.text
                metadata       = Seq.zip model.metaNames model.metaValues
                                 |> Seq.filter (fun it -> fst it > "")
                                 |> Seq.map (fun it -> { name = fst it; value = snd it })
                                 |> Seq.sortBy (fun it -> $"{it.name.ToLower ()} {it.value.ToLower ()}")
                                 |> List.ofSeq
                revisions      = match page.revisions |> List.tryHead with
                                 | Some r when r.text = revision.text -> page.revisions
                                 | _ -> revision :: page.revisions
            }
        do! (if model.pageId = "new" then data.Page.add else data.Page.update) page
        if updateList then do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Page saved successfully" }
        return! redirectToGet $"admin/page/{PageId.toString page.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}
