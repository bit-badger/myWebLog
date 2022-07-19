/// Handlers to manipulate pages
module MyWebLog.Handlers.Page

open DotLiquid
open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// GET /admin/pages
// GET /admin/pages/page/{pageNbr}
let all pageNbr : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! pages = ctx.Data.Page.FindPageOfPages ctx.WebLog.Id pageNbr
    return!
        Hash.FromAnonymousObject {|
            page_title = "Pages"
            csrf       = ctx.CsrfTokenSet
            pages      = pages |> List.map (DisplayPage.fromPageMinimal ctx.WebLog)
            page_nbr   = pageNbr
            prev_page  = if pageNbr = 2 then "" else $"/page/{pageNbr - 1}"
            next_page  = $"/page/{pageNbr + 1}"
        |}
        |> adminView "page-list" next ctx
}

// GET /admin/page/{id}/edit
let edit pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! result = task {
        match pgId with
        | "new" -> return Some ("Add a New Page", { Page.empty with Id = PageId "new"; AuthorId = ctx.UserId })
        | _ ->
            match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
            | Some page -> return Some ("Edit Page", page)
            | None -> return None
    }
    match result with
    | Some (title, page) when canEdit page.AuthorId ctx ->
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
            |> adminView "page-edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/delete
let delete pgId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    match! ctx.Data.Page.Delete (PageId pgId) ctx.WebLog.Id with
    | true ->
        do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with Message = "Page deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with Message = "Page not found; nothing deleted" }
    return! redirectToGet "admin/pages" next ctx
}

// GET /admin/page/{id}/permalinks
let editPermalinks pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
    | Some pg when canEdit pg.AuthorId ctx ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Prior Permalinks"
                csrf       = ctx.CsrfTokenSet
                model      = ManagePermalinksModel.fromPage pg
            |}
            |> adminView "permalinks" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/permalinks
let savePermalinks : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<ManagePermalinksModel> ()
    let  pageId = PageId model.Id
    match! ctx.Data.Page.FindById pageId ctx.WebLog.Id with
    | Some pg when canEdit pg.AuthorId ctx ->
        let  links = model.Prior |> Array.map Permalink |> List.ofArray
        match! ctx.Data.Page.UpdatePriorPermalinks pageId ctx.WebLog.Id links with
        | true ->
            do! addMessage ctx { UserMessage.success with Message = "Page permalinks saved successfully" }
            return! redirectToGet $"admin/page/{model.Id}/permalinks" next ctx
        | false -> return! Error.notFound next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/page/{id}/revisions
let editRevisions pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
    | Some pg when canEdit pg.AuthorId ctx ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Page Revisions"
                csrf       = ctx.CsrfTokenSet
                model      = ManageRevisionsModel.fromPage ctx.WebLog pg
            |}
            |> adminView "revisions" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/page/{id}/revisions/purge
let purgeRevisions pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let data = ctx.Data
    match! data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
    | Some pg ->
        do! data.Page.Update { pg with Revisions = [ List.head pg.Revisions ] }
        do! addMessage ctx { UserMessage.success with Message = "Prior revisions purged successfully" }
        return! redirectToGet $"admin/page/{pgId}/revisions" next ctx
    | None -> return! Error.notFound next ctx
}

open Microsoft.AspNetCore.Http

/// Find the page and the requested revision
let private findPageRevision pgId revDate (ctx : HttpContext) = task {
    match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
    | Some pg ->
        let asOf = parseToUtc revDate
        return Some pg, pg.Revisions |> List.tryFind (fun r -> r.AsOf = asOf)
    | None -> return None, None
}

// GET /admin/page/{id}/revision/{revision-date}/preview
let previewRevision (pgId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev when canEdit pg.AuthorId ctx ->
        return!
            Hash.FromAnonymousObject {|
                content = $"""<div class="mwl-revision-preview mb-3">{MarkupText.toHtml rev.Text}</div>"""
            |}
            |> adminBareView "" next ctx
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

open System

// POST /admin/page/{id}/revision/{revision-date}/restore
let restoreRevision (pgId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev when canEdit pg.AuthorId ctx ->
        do! ctx.Data.Page.Update
                { pg with
                    Revisions = { rev with AsOf = DateTime.UtcNow }
                                  :: (pg.Revisions |> List.filter (fun r -> r.AsOf <> rev.AsOf))
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
    | Some pg, Some rev when canEdit pg.AuthorId ctx ->
        do! ctx.Data.Page.Update { pg with Revisions = pg.Revisions |> List.filter (fun r -> r.AsOf <> rev.AsOf) }
        do! addMessage ctx { UserMessage.success with Message = "Revision deleted successfully" }
        return! adminBareView "" next ctx (Hash.FromAnonymousObject {| content = "" |})
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
                        Id          = PageId.create ()
                        WebLogId    = ctx.WebLog.Id
                        AuthorId    = ctx.UserId
                        PublishedOn = now
                    })
        | pgId -> data.Page.FindFullById (PageId pgId) ctx.WebLog.Id
    match! pg with
    | Some page when canEdit page.AuthorId ctx ->
        let updateList = page.IsInPageList <> model.IsShownInPageList
        let revision   = { AsOf = now; Text = MarkupText.parse $"{model.Source}: {model.Text}" }
        // Detect a permalink change, and add the prior one to the prior list
        let page =
            match Permalink.toString page.Permalink with
            | "" -> page
            | link when link = model.Permalink -> page
            | _ -> { page with PriorPermalinks = page.Permalink :: page.PriorPermalinks }
        let page =
            { page with
                Title          = model.Title
                Permalink      = Permalink model.Permalink
                UpdatedOn      = now
                IsInPageList = model.IsShownInPageList
                Template       = match model.Template with "" -> None | tmpl -> Some tmpl
                Text           = MarkupText.toHtml revision.Text
                Metadata       = Seq.zip model.MetaNames model.MetaValues
                                 |> Seq.filter (fun it -> fst it > "")
                                 |> Seq.map (fun it -> { Name = fst it; Value = snd it })
                                 |> Seq.sortBy (fun it -> $"{it.Name.ToLower ()} {it.Value.ToLower ()}")
                                 |> List.ofSeq
                Revisions      = match page.Revisions |> List.tryHead with
                                 | Some r when r.Text = revision.Text -> page.Revisions
                                 | _ -> revision :: page.Revisions
            }
        do! (if model.PageId = "new" then data.Page.Add else data.Page.Update) page
        if updateList then do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with Message = "Page saved successfully" }
        return! redirectToGet $"admin/page/{PageId.toString page.Id}/edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}
