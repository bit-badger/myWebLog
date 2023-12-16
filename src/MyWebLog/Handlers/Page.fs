/// Handlers to manipulate pages
module MyWebLog.Handlers.Page

open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// GET /admin/pages
// GET /admin/pages/page/{pageNbr}
let all pageNbr : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! pages = ctx.Data.Page.FindPageOfPages ctx.WebLog.Id pageNbr
    return!
        hashForPage "Pages"
        |> withAntiCsrf ctx
        |> addToHash "pages"     (pages
                                  |> Seq.ofList
                                  |> Seq.truncate 25
                                  |> Seq.map (DisplayPage.FromPageMinimal ctx.WebLog)
                                  |> List.ofSeq)
        |> addToHash "page_nbr"  pageNbr
        |> addToHash "prev_page" (if pageNbr = 2 then "" else $"/page/{pageNbr - 1}")
        |> addToHash "has_next"  (List.length pages > 25)
        |> addToHash "next_page" $"/page/{pageNbr + 1}"
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
            hashForPage title
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model model
            |> addToHash "metadata" (
                 Array.zip model.MetaNames model.MetaValues
                 |> Array.mapi (fun idx (name, value) -> [| string idx; name; value |]))
            |> addToHash "templates" templates
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
            hashForPage "Manage Prior Permalinks"
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model (ManagePermalinksModel.fromPage pg)
            |> adminView "permalinks" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/permalinks
let savePermalinks : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model  = ctx.BindFormAsync<ManagePermalinksModel> ()
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
            hashForPage "Manage Page Revisions"
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model (ManageRevisionsModel.fromPage ctx.WebLog pg)
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
        let _, extra = WebLog.hostAndPath ctx.WebLog
        return! {|
            content =
                [   """<div class="mwl-revision-preview mb-3">"""
                    rev.Text.AsHtml() |> addBaseToRelativeUrls extra
                    "</div>"
                ]
                |> String.concat ""
        |}
        |> makeHash |> adminBareView "" next ctx
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/revision/{revision-date}/restore
let restoreRevision (pgId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev when canEdit pg.AuthorId ctx ->
        do! ctx.Data.Page.Update
                { pg with
                    Revisions = { rev with AsOf = Noda.now () }
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
        return! adminBareView "" next ctx (makeHash {| content = "" |})
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/page/save
let save : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model   = ctx.BindFormAsync<EditPageModel>()
    let  data    = ctx.Data
    let  now     = Noda.now ()
    let  tryPage =
        if model.IsNew then
            { Page.empty with
                Id          = PageId.Create()
                WebLogId    = ctx.WebLog.Id
                AuthorId    = ctx.UserId
                PublishedOn = now
            } |> someTask
        else data.Page.FindFullById (PageId model.PageId) ctx.WebLog.Id
    match! tryPage with
    | Some page when canEdit page.AuthorId ctx ->
        let updateList  = page.IsInPageList <> model.IsShownInPageList
        let updatedPage = model.UpdatePage page now
        do! (if model.IsNew then data.Page.Add else data.Page.Update) updatedPage
        if updateList then do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with Message = "Page saved successfully" }
        return! redirectToGet $"admin/page/{page.Id}/edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}
