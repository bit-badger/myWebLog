/// Handlers to manipulate pages
module MyWebLog.Handlers.Page

open DotLiquid
open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// GET /admin/pages
// GET /admin/pages/page/{pageNbr}
let all pageNbr : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! pages = ctx.Data.Page.FindPageOfPages ctx.WebLog.id pageNbr
    return!
        Hash.FromAnonymousObject {|
            page_title = "Pages"
            csrf       = ctx.CsrfTokenSet
            pages      = pages |> List.map (DisplayPage.fromPageMinimal ctx.WebLog)
            page_nbr   = pageNbr
            prev_page  = if pageNbr = 2 then "" else $"/page/{pageNbr - 1}"
            next_page  = $"/page/{pageNbr + 1}"
        |}
        |> viewForTheme "admin" "page-list" next ctx
}

// GET /admin/page/{id}/edit
let edit pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! result = task {
        match pgId with
        | "new" -> return Some ("Add a New Page", { Page.empty with id = PageId "new"; authorId = ctx.UserId })
        | _ ->
            match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.id with
            | Some page -> return Some ("Edit Page", page)
            | None -> return None
    }
    match result with
    | Some (title, page) when canEdit page.authorId ctx ->
        let  model     = EditPageModel.fromPage page
        let! templates = templatesForTheme ctx "page"
        return!
            Hash.FromAnonymousObject {|
                page_title = title
                csrf       = ctx.CsrfTokenSet
                model      = model
                metadata   = Array.zip model.MetaNames model.MetaValues
                             |> Array.mapi (fun idx (name, value) -> [| string idx; name; value |])
                templates  = templates
            |}
            |> viewForTheme "admin" "page-edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/delete
let delete pgId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    match! ctx.Data.Page.Delete (PageId pgId) ctx.WebLog.id with
    | true ->
        do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with Message = "Page deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with Message = "Page not found; nothing deleted" }
    return! redirectToGet "admin/pages" next ctx
}

// GET /admin/page/{id}/permalinks
let editPermalinks pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.id with
    | Some pg when canEdit pg.authorId ctx ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Prior Permalinks"
                csrf       = ctx.CsrfTokenSet
                model      = ManagePermalinksModel.fromPage pg
            |}
            |> viewForTheme "admin" "permalinks" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/permalinks
let savePermalinks : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<ManagePermalinksModel> ()
    let  pageId = PageId model.Id
    match! ctx.Data.Page.FindById pageId ctx.WebLog.id with
    | Some pg when canEdit pg.authorId ctx ->
        let  links = model.Prior |> Array.map Permalink |> List.ofArray
        match! ctx.Data.Page.UpdatePriorPermalinks pageId ctx.WebLog.id links with
        | true ->
            do! addMessage ctx { UserMessage.success with Message = "Page permalinks saved successfully" }
            return! redirectToGet $"admin/page/{model.Id}/permalinks" next ctx
        | false -> return! Error.notFound next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/page/{id}/revisions
let editRevisions pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.id with
    | Some pg when canEdit pg.authorId ctx ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Page Revisions"
                csrf       = ctx.CsrfTokenSet
                model      = ManageRevisionsModel.fromPage ctx.WebLog pg
            |}
            |> viewForTheme "admin" "revisions" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/page/{id}/revisions/purge
let purgeRevisions pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let data = ctx.Data
    match! data.Page.FindFullById (PageId pgId) ctx.WebLog.id with
    | Some pg ->
        do! data.Page.Update { pg with revisions = [ List.head pg.revisions ] }
        do! addMessage ctx { UserMessage.success with Message = "Prior revisions purged successfully" }
        return! redirectToGet $"admin/page/{pgId}/revisions" next ctx
    | None -> return! Error.notFound next ctx
}

open Microsoft.AspNetCore.Http

/// Find the page and the requested revision
let private findPageRevision pgId revDate (ctx : HttpContext) = task {
    match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.id with
    | Some pg ->
        let asOf = parseToUtc revDate
        return Some pg, pg.revisions |> List.tryFind (fun r -> r.asOf = asOf)
    | None -> return None, None
}

// GET /admin/page/{id}/revision/{revision-date}/preview
let previewRevision (pgId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev when canEdit pg.authorId ctx ->
        return!
            Hash.FromAnonymousObject {|
                content = $"""<div class="mwl-revision-preview mb-3">{MarkupText.toHtml rev.text}</div>"""
            |}
            |> bareForTheme "admin" "" next ctx
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

open System

// POST /admin/page/{id}/revision/{revision-date}/restore
let restoreRevision (pgId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev when canEdit pg.authorId ctx ->
        do! ctx.Data.Page.Update
                { pg with
                    revisions = { rev with asOf = DateTime.UtcNow }
                                  :: (pg.revisions |> List.filter (fun r -> r.asOf <> rev.asOf))
                }
        do! addMessage ctx { UserMessage.success with Message = "Revision restored successfully" }
        return! redirectToGet $"admin/page/{pgId}/revisions" next ctx
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/revision/{revision-date}/delete
let deleteRevision (pgId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev when canEdit pg.authorId ctx ->
        do! ctx.Data.Page.Update { pg with revisions = pg.revisions |> List.filter (fun r -> r.asOf <> rev.asOf) }
        do! addMessage ctx { UserMessage.success with Message = "Revision deleted successfully" }
        return! bareForTheme "admin" "" next ctx (Hash.FromAnonymousObject {| content = "" |})
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

//#nowarn "3511"

open System.Threading.Tasks

// POST /admin/page/save
let save : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<EditPageModel> ()
    let  data  = ctx.Data
    let  now   = DateTime.UtcNow
    let  pg    =
        match model.PageId with
        | "new" ->
            Task.FromResult (
                Some
                    { Page.empty with
                        id          = PageId.create ()
                        webLogId    = ctx.WebLog.id
                        authorId    = ctx.UserId
                        publishedOn = now
                    })
        | pgId -> data.Page.FindFullById (PageId pgId) ctx.WebLog.id
    match! pg with
    | Some page when canEdit page.authorId ctx ->
        let updateList = page.showInPageList <> model.IsShownInPageList
        let revision   = { asOf = now; text = MarkupText.parse $"{model.Source}: {model.Text}" }
        // Detect a permalink change, and add the prior one to the prior list
        let page =
            match Permalink.toString page.permalink with
            | "" -> page
            | link when link = model.Permalink -> page
            | _ -> { page with priorPermalinks = page.permalink :: page.priorPermalinks }
        let page =
            { page with
                title          = model.Title
                permalink      = Permalink model.Permalink
                updatedOn      = now
                showInPageList = model.IsShownInPageList
                template       = match model.Template with "" -> None | tmpl -> Some tmpl
                text           = MarkupText.toHtml revision.text
                metadata       = Seq.zip model.MetaNames model.MetaValues
                                 |> Seq.filter (fun it -> fst it > "")
                                 |> Seq.map (fun it -> { name = fst it; value = snd it })
                                 |> Seq.sortBy (fun it -> $"{it.name.ToLower ()} {it.value.ToLower ()}")
                                 |> List.ofSeq
                revisions      = match page.revisions |> List.tryHead with
                                 | Some r when r.text = revision.text -> page.revisions
                                 | _ -> revision :: page.revisions
            }
        do! (if model.PageId = "new" then data.Page.Add else data.Page.Update) page
        if updateList then do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with Message = "Page saved successfully" }
        return! redirectToGet $"admin/page/{PageId.toString page.id}/edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}
