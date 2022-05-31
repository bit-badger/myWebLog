/// Handlers to manipulate admin functions
module MyWebLog.Handlers.Admin

open System.Collections.Generic
open System.IO

/// The currently available themes
let private themes () =
    Directory.EnumerateDirectories "themes"
    |> Seq.map (fun it -> it.Split Path.DirectorySeparatorChar |> Array.last)
    |> Seq.filter (fun it -> it <> "admin")
    |> Seq.map (fun it -> KeyValuePair.Create (it, it))
    |> Array.ofSeq

open System.Threading.Tasks
open DotLiquid
open Giraffe
open MyWebLog
open MyWebLog.ViewModels
open RethinkDb.Driver.Net

// GET /admin
let dashboard : HttpHandler = fun next ctx -> task {
    let webLogId = ctx.WebLog.id
    let conn     = ctx.Conn
    let getCount (f : WebLogId -> IConnection -> Task<int>) = f webLogId conn
    let! posts   = Data.Post.countByStatus Published |> getCount
    let! drafts  = Data.Post.countByStatus Draft     |> getCount
    let! pages   = Data.Page.countAll                |> getCount
    let! listed  = Data.Page.countListed             |> getCount
    let! cats    = Data.Category.countAll            |> getCount
    let! topCats = Data.Category.countTopLevel       |> getCount
    return!
        Hash.FromAnonymousObject
            {| page_title = "Dashboard"
               model =
                   { posts              = posts
                     drafts             = drafts
                     pages              = pages
                     listedPages        = listed
                     categories         = cats
                     topLevelCategories = topCats
                   }
            |}
        |> viewForTheme "admin" "dashboard" next ctx
}

// -- CATEGORIES --

// GET /admin/categories
let listCategories : HttpHandler = fun next ctx -> task {
    return!
        Hash.FromAnonymousObject {|
            categories = CategoryCache.get ctx
            page_title = "Categories"
            csrf       = csrfToken ctx
        |}
        |> viewForTheme "admin" "category-list" next ctx
}

// GET /admin/category/{id}/edit
let editCategory catId : HttpHandler = fun next ctx -> task {
    let! result = task {
        match catId with
        | "new" -> return Some ("Add a New Category", { Category.empty with id = CategoryId "new" })
        | _ ->
            match! Data.Category.findById (CategoryId catId) ctx.WebLog.id ctx.Conn with
            | Some cat -> return Some ("Edit Category", cat)
            | None -> return None
    }
    match result with
    | Some (title, cat) ->
        return!
            Hash.FromAnonymousObject {|
                csrf       = csrfToken ctx
                model      = EditCategoryModel.fromCategory cat
                page_title = title
                categories = CategoryCache.get ctx
            |}
            |> viewForTheme "admin" "category-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/category/save
let saveCategory : HttpHandler = fun next ctx -> task {
    let  webLog   = ctx.WebLog
    let  conn     = ctx.Conn
    let! model    = ctx.BindFormAsync<EditCategoryModel> ()
    let! category = task {
        match model.categoryId with
        | "new" -> return Some { Category.empty with id = CategoryId.create (); webLogId = webLog.id }
        | catId -> return! Data.Category.findById (CategoryId catId) webLog.id conn
    }
    match category with
    | Some cat ->
        let cat =
            { cat with
                name        = model.name
                slug        = model.slug
                description = if model.description = "" then None else Some model.description
                parentId    = if model.parentId    = "" then None else Some (CategoryId model.parentId)
            }
        do! (match model.categoryId with "new" -> Data.Category.add | _ -> Data.Category.update) cat conn
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Category saved successfully" }
        return!
            redirectToGet (WebLog.relativeUrl webLog (Permalink $"admin/category/{CategoryId.toString cat.id}/edit"))
                next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/category/{id}/delete
let deleteCategory catId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match! Data.Category.delete (CategoryId catId) webLog.id ctx.Conn with
    | true ->
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Category deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Category not found; cannot delete" }
    return! redirectToGet (WebLog.relativeUrl webLog (Permalink "admin/categories")) next ctx
}

// -- PAGES --

// GET /admin/pages
// GET /admin/pages/page/{pageNbr}
let listPages pageNbr : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let! pages  = Data.Page.findPageOfPages webLog.id pageNbr ctx.Conn
    return!
        Hash.FromAnonymousObject
            {|  csrf       = csrfToken ctx
                pages      = pages |> List.map (DisplayPage.fromPageMinimal webLog)
                page_title = "Pages"
                page_nbr   = pageNbr
                prev_page  = if pageNbr = 2 then "" else $"/page/{pageNbr - 1}"
                next_page  = $"/page/{pageNbr + 1}"
            |}
        |> viewForTheme "admin" "page-list" next ctx
}

// GET /admin/page/{id}/edit
let editPage pgId : HttpHandler = fun next ctx -> task {
    let! result = task {
        match pgId with
        | "new" -> return Some ("Add a New Page", { Page.empty with id = PageId "new" })
        | _ ->
            match! Data.Page.findByFullId (PageId pgId) ctx.WebLog.id ctx.Conn with
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

// GET /admin/page/{id}/permalinks
let editPagePermalinks pgId : HttpHandler = fun next ctx -> task {
    match! Data.Page.findByFullId (PageId pgId) ctx.WebLog.id ctx.Conn with
    | Some pg ->
        return!
            Hash.FromAnonymousObject {|
                csrf       = csrfToken ctx
                model      = ManagePermalinksModel.fromPage pg
                page_title = $"Manage Prior Permalinks"
            |}
            |> viewForTheme "admin" "permalinks" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/page/permalinks
let savePagePermalinks : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let! model  = ctx.BindFormAsync<ManagePermalinksModel> ()
    let  links  = model.prior |> Array.map Permalink |> List.ofArray
    match! Data.Page.updatePriorPermalinks (PageId model.id) webLog.id links ctx.Conn with
    | true ->
        do! addMessage ctx { UserMessage.success with message = "Page permalinks saved successfully" }
        return! redirectToGet (WebLog.relativeUrl webLog (Permalink $"admin/page/{model.id}/permalinks")) next ctx
    | false -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/delete
let deletePage pgId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match! Data.Page.delete (PageId pgId) webLog.id ctx.Conn with
    | true ->
        do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Page deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Page not found; nothing deleted" }
    return! redirectToGet (WebLog.relativeUrl webLog (Permalink "admin/pages")) next ctx
}

open System

#nowarn "3511"

// POST /admin/page/save
let savePage : HttpHandler = fun next ctx -> task {
    let! model  = ctx.BindFormAsync<EditPageModel> ()
    let  webLog = ctx.WebLog
    let  conn   = ctx.Conn
    let  now    = DateTime.UtcNow
    let! pg     = task {
        match model.pageId with
        | "new" ->
            return Some
                { Page.empty with
                    id          = PageId.create ()
                    webLogId    = webLog.id
                    authorId    = userId ctx
                    publishedOn = now
                }
        | pgId -> return! Data.Page.findByFullId (PageId pgId) webLog.id conn
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
        do! (if model.pageId = "new" then Data.Page.add else Data.Page.update) page conn
        if updateList then do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Page saved successfully" }
        return!
            redirectToGet (WebLog.relativeUrl webLog (Permalink $"admin/page/{PageId.toString page.id}/edit")) next ctx
    | None -> return! Error.notFound next ctx
}

// -- WEB LOG SETTINGS --

// GET /admin/settings
let settings : HttpHandler = fun next ctx -> task {
    let  webLog   = ctx.WebLog
    let! allPages = Data.Page.findAll webLog.id ctx.Conn
    return!
        Hash.FromAnonymousObject
            {|  csrf  = csrfToken ctx
                model = SettingsModel.fromWebLog webLog
                pages =
                    seq {
                        KeyValuePair.Create ("posts", "- First Page of Posts -")
                        yield! allPages
                               |> List.sortBy (fun p -> p.title.ToLower ())
                               |> List.map (fun p -> KeyValuePair.Create (PageId.toString p.id, p.title))
                    }
                    |> Array.ofSeq
                themes     = themes ()
                web_log    = webLog
                page_title = "Web Log Settings"
            |}
        |> viewForTheme "admin" "settings" next ctx
}

// POST /admin/settings
let saveSettings : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  conn   = ctx.Conn
    let! model  = ctx.BindFormAsync<SettingsModel> ()
    match! Data.WebLog.findById webLog.id conn with
    | Some webLog ->
        let webLog = model.update webLog
        do! Data.WebLog.updateSettings webLog conn

        // Update cache
        WebLogCache.set webLog
    
        do! addMessage ctx { UserMessage.success with message = "Web log settings saved successfully" }
        return! redirectToGet (WebLog.relativeUrl webLog (Permalink "admin/settings")) next ctx
    | None -> return! Error.notFound next ctx
}

// -- TAG MAPPINGS --

// GET /admin/tag-mappings
let tagMappings : HttpHandler = fun next ctx -> task {
    let! mappings = Data.TagMap.findByWebLogId ctx.WebLog.id ctx.Conn
    return!
        Hash.FromAnonymousObject
            {|  csrf        = csrfToken ctx
                mappings    = mappings
                mapping_ids = mappings |> List.map (fun it -> { name = it.tag; value = TagMapId.toString it.id })
                page_title  = "Tag Mappings"
            |}
        |> viewForTheme "admin" "tag-mapping-list" next ctx
}

// GET /admin/tag-mapping/{id}/edit
let editMapping tagMapId : HttpHandler = fun next ctx -> task {
    let isNew  = tagMapId = "new"
    let tagMap =
        if isNew then
            Task.FromResult (Some { TagMap.empty with id = TagMapId "new" })
        else
            Data.TagMap.findById (TagMapId tagMapId) ctx.WebLog.id ctx.Conn
    match! tagMap with
    | Some tm ->
        return!
            Hash.FromAnonymousObject
                {|  csrf       = csrfToken ctx
                    model      = EditTagMapModel.fromMapping tm
                    page_title = if isNew then "Add Tag Mapping" else $"Mapping for {tm.tag} Tag" 
                |}
            |> viewForTheme "admin" "tag-mapping-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/tag-mapping/save
let saveMapping : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  conn   = ctx.Conn
    let! model  = ctx.BindFormAsync<EditTagMapModel> ()
    let  tagMap =
        if model.id = "new" then
            Task.FromResult (Some { TagMap.empty with id = TagMapId.create (); webLogId = webLog.id })
        else
            Data.TagMap.findById (TagMapId model.id) webLog.id conn
    match! tagMap with
    | Some tm ->
        do! Data.TagMap.save { tm with tag = model.tag.ToLower (); urlValue = model.urlValue.ToLower () } conn
        do! addMessage ctx { UserMessage.success with message = "Tag mapping saved successfully" }
        return!
            redirectToGet (WebLog.relativeUrl webLog (Permalink $"admin/tag-mapping/{TagMapId.toString tm.id}/edit"))
                next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/tag-mapping/{id}/delete
let deleteMapping tagMapId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match! Data.TagMap.delete (TagMapId tagMapId) webLog.id ctx.Conn with
    | true  -> do! addMessage ctx { UserMessage.success with message = "Tag mapping deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Tag mapping not found; nothing deleted" }
    return! redirectToGet (WebLog.relativeUrl webLog (Permalink "admin/tag-mappings")) next ctx
}
