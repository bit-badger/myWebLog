/// Handlers to manipulate pages
module MyWebLog.Handlers.Page

open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// GET /admin/pages
// GET /admin/pages/page/{pageNbr}
let all pageNbr : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! pages = ctx.Data.Page.FindPageOfPages ctx.WebLog.Id pageNbr
    let displayPages =
        pages
        |> Seq.ofList
        |> Seq.truncate 25
        |> Seq.map (DisplayPage.FromPageMinimal ctx.WebLog)
        |> List.ofSeq
    return!
        Views.Page.pageList displayPages pageNbr (pages.Length > 25)
        |> adminPage "Pages" true next ctx
}

// GET /admin/page/{id}/edit
let edit pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! result = task {
        match pgId with
        | "new" -> return Some ("Add a New Page", { Page.Empty with Id = PageId "new"; AuthorId = ctx.UserId })
        | _ ->
            match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
            | Some page -> return Some ("Edit Page", page)
            | None -> return None
    }
    match result with
    | Some (title, page) when canEdit page.AuthorId ctx ->
        let  model     = EditPageModel.FromPage page
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

// DELETE /admin/page/{id}
let delete pgId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    match! ctx.Data.Page.Delete (PageId pgId) ctx.WebLog.Id with
    | true ->
        do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.Success with Message = "Page deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.Error with Message = "Page not found; nothing deleted" }
    return! redirectToGet "admin/pages" next ctx
}

// GET /admin/page/{id}/permalinks
let editPermalinks pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
    | Some pg when canEdit pg.AuthorId ctx ->
        return!
            ManagePermalinksModel.FromPage pg
            |> Views.Helpers.managePermalinks
            |> adminPage "Manage Prior Permalinks" true next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/permalinks
let savePermalinks : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model  = ctx.BindFormAsync<ManagePermalinksModel>()
    let  pageId = PageId model.Id
    match! ctx.Data.Page.FindById pageId ctx.WebLog.Id with
    | Some pg when canEdit pg.AuthorId ctx ->
        let  links = model.Prior |> Array.map Permalink |> List.ofArray
        match! ctx.Data.Page.UpdatePriorPermalinks pageId ctx.WebLog.Id links with
        | true ->
            do! addMessage ctx { UserMessage.Success with Message = "Page permalinks saved successfully" }
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
            ManageRevisionsModel.FromPage pg
            |> Views.Helpers.manageRevisions
            |> adminPage "Manage Page Revisions" true next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// DELETE /admin/page/{id}/revisions
let purgeRevisions pgId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let data = ctx.Data
    match! data.Page.FindFullById (PageId pgId) ctx.WebLog.Id with
    | Some pg ->
        do! data.Page.Update { pg with Revisions = [ List.head pg.Revisions ] }
        do! addMessage ctx { UserMessage.Success with Message = "Prior revisions purged successfully" }
        return! redirectToGet $"admin/page/{pgId}/revisions" next ctx
    | None -> return! Error.notFound next ctx
}

open Microsoft.AspNetCore.Http

/// Find the page and the requested revision
let private findPageRevision pgId revDate (ctx: HttpContext) = task {
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
        return! {|
            content =
                [   """<div class="mwl-revision-preview mb-3">"""
                    rev.Text.AsHtml() |> addBaseToRelativeUrls ctx.WebLog.ExtraPath
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
                                  :: (pg.Revisions |> List.filter (fun r -> r.AsOf <> rev.AsOf)) }
        do! addMessage ctx { UserMessage.Success with Message = "Revision restored successfully" }
        return! redirectToGet $"admin/page/{pgId}/revisions" next ctx
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// DELETE /admin/page/{id}/revision/{revision-date}
let deleteRevision (pgId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPageRevision pgId revDate ctx with
    | Some pg, Some rev when canEdit pg.AuthorId ctx ->
        do! ctx.Data.Page.Update { pg with Revisions = pg.Revisions |> List.filter (fun r -> r.AsOf <> rev.AsOf) }
        do! addMessage ctx { UserMessage.Success with Message = "Revision deleted successfully" }
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
            { Page.Empty with
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
        do! addMessage ctx { UserMessage.Success with Message = "Page saved successfully" }
        return! redirectToGet $"admin/page/{page.Id}/edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}
