/// Handlers to manipulate pages
module MyWebLog.Handlers.Page

open DotLiquid
open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// GET /pages
// GET /pages/page/{pageNbr}
let all pageNbr : HttpHandler = requireUser >=> fun next ctx -> task {
    let  webLog = WebLogCache.get ctx
    let! pages  = Data.Page.findPageOfPages webLog.id pageNbr (conn ctx)
    return!
        Hash.FromAnonymousObject
            {| pages      = pages |> List.map (DisplayPage.fromPageMinimal webLog)
               page_title = "Pages"
            |}
        |> viewForTheme "admin" "page-list" next ctx
}

// GET /page/{id}/edit
let edit pgId : HttpHandler = requireUser >=> fun next ctx -> task {
    let! result = task {
        match pgId with
        | "new" -> return Some ("Add a New Page", { Page.empty with id = PageId "new" })
        | _ ->
            match! Data.Page.findByFullId (PageId pgId) (webLogId ctx) (conn ctx) with
            | Some page -> return Some ("Edit Page", page)
            | None -> return None
    }
    match result with
    | Some (title, page) ->
        let model = EditPageModel.fromPage page
        return!
            Hash.FromAnonymousObject {|
                csrf       = csrfToken ctx
                model      = model
                metadata   = Array.zip model.metaNames model.metaValues
                             |> Array.mapi (fun idx (name, value) -> [| string idx; name; value |])
                page_title = title
                templates  = templatesForTheme ctx "page"
            |}
            |> viewForTheme "admin" "page-edit" next ctx
    | None -> return! Error.notFound next ctx
}

open System

// POST /page/save
let save : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let! model    = ctx.BindFormAsync<EditPageModel> ()
    let  webLogId = webLogId ctx
    let  conn     = conn ctx
    let  now      = DateTime.UtcNow
    let! pg       = task {
        match model.pageId with
        | "new" ->
            return Some
                { Page.empty with
                    id          = PageId.create ()
                    webLogId    = webLogId
                    authorId    = userId ctx
                    publishedOn = now
                }
        | pgId -> return! Data.Page.findByFullId (PageId pgId) webLogId conn
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
        do! (match model.pageId with "new" -> Data.Page.add | _ -> Data.Page.update) page conn
        if updateList then do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Page saved successfully" }
        return! redirectToGet $"/page/{PageId.toString page.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}
