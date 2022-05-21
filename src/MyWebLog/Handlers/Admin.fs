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
let dashboard : HttpHandler = requireUser >=> fun next ctx -> task {
    let webLogId = webLogId ctx
    let conn     = conn ctx
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
let listCategories : HttpHandler = requireUser >=> fun next ctx -> task {
    return!
        Hash.FromAnonymousObject {|
            categories = CategoryCache.get ctx
            page_title = "Categories"
            csrf       = csrfToken ctx
        |}
        |> viewForTheme "admin" "category-list" next ctx
}

// GET /admin/category/{id}/edit
let editCategory catId : HttpHandler = requireUser >=> fun next ctx -> task {
    let  webLogId = webLogId ctx
    let  conn     = conn     ctx
    let! result   = task {
        match catId with
        | "new" -> return Some ("Add a New Category", { Category.empty with id = CategoryId "new" })
        | _ ->
            match! Data.Category.findById (CategoryId catId) webLogId conn with
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
let saveCategory : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let! model    = ctx.BindFormAsync<EditCategoryModel> ()
    let  webLogId = webLogId ctx
    let  conn     = conn     ctx
    let! category = task {
        match model.categoryId with
        | "new" -> return Some { Category.empty with id = CategoryId.create (); webLogId = webLogId }
        | catId -> return! Data.Category.findById (CategoryId catId) webLogId conn
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
        return! redirectToGet $"/admin/category/{CategoryId.toString cat.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/category/{id}/delete
let deleteCategory catId : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let webLogId = webLogId ctx
    let conn     = conn     ctx
    match! Data.Category.delete (CategoryId catId) webLogId conn with
    | true ->
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Category deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Category not found; cannot delete" }
    return! redirectToGet "/admin/categories" next ctx
}

// -- PAGES --

// GET /admin/pages
// GET /admin/pages/page/{pageNbr}
let listPages pageNbr : HttpHandler = requireUser >=> fun next ctx -> task {
    let  webLog = WebLogCache.get ctx
    let! pages  = Data.Page.findPageOfPages webLog.id pageNbr (conn ctx)
    return!
        Hash.FromAnonymousObject
            {| pages      = pages |> List.map (DisplayPage.fromPageMinimal webLog)
               page_title = "Pages"
            |}
        |> viewForTheme "admin" "page-list" next ctx
}

// GET /admin/page/{id}/edit
let editPage pgId : HttpHandler = requireUser >=> fun next ctx -> task {
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

// GET /admin/page/{id}/permalinks
let editPagePermalinks pgId : HttpHandler = requireUser >=> fun next ctx -> task {
    match! Data.Page.findByFullId (PageId pgId) (webLogId ctx) (conn ctx) with
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
let savePagePermalinks : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<ManagePermalinksModel> ()
    let  links = model.prior |> Array.map Permalink |> List.ofArray
    match! Data.Page.updatePriorPermalinks (PageId model.id) (webLogId ctx) links (conn ctx) with
    | true ->
        do! addMessage ctx { UserMessage.success with message = "Page permalinks saved successfully" }
        return! redirectToGet $"/admin/page/{model.id}/permalinks" next ctx
    | false -> return! Error.notFound next ctx
}

// POST /admin/page/{id}/delete
let deletePage pgId : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    match! Data.Page.delete (PageId pgId) (webLogId ctx) (conn ctx) with
    | true  -> do! addMessage ctx { UserMessage.success with message = "Page deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Page not found; nothing deleted" }
    return! redirectToGet "/admin/pages" next ctx
}

open System

#nowarn "3511"

// POST /page/save
let savePage : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
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
        do! (if model.pageId = "new" then Data.Page.add else Data.Page.update) page conn
        if updateList then do! PageListCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Page saved successfully" }
        return! redirectToGet $"/admin/page/{PageId.toString page.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}

// -- WEB LOG SETTINGS --

// GET /admin/settings
let settings : HttpHandler = requireUser >=> fun next ctx -> task {
    let  webLog   = WebLogCache.get ctx
    let! allPages = Data.Page.findAll webLog.id (conn ctx)
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
let saveSettings : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let  conn  = conn ctx
    let! model = ctx.BindFormAsync<SettingsModel> ()
    match! Data.WebLog.findById (WebLogCache.get ctx).id conn with
    | Some webLog ->
        let updated =
            { webLog with
                name         = model.name
                subtitle     = if model.subtitle = "" then None else Some model.subtitle
                defaultPage  = model.defaultPage
                postsPerPage = model.postsPerPage
                timeZone     = model.timeZone
                themePath    = model.themePath
            }
        do! Data.WebLog.updateSettings updated conn

        // Update cache
        WebLogCache.set ctx updated
    
        do! addMessage ctx { UserMessage.success with message = "Web log settings saved successfully" }
        return! redirectToGet "/admin" next ctx
    | None -> return! Error.notFound next ctx
}

// -- TAG MAPPINGS --

// GET /admin/tag-mappings
let tagMappings : HttpHandler = requireUser >=> fun next ctx -> task {
    let! mappings = Data.TagMap.findByWebLogId (webLogId ctx) (conn ctx)
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
let editMapping tagMapId : HttpHandler = requireUser >=> fun next ctx -> task {
    let webLogId = webLogId ctx
    let isNew    = tagMapId = "new"
    let tagMap   =
        if isNew then
            Task.FromResult (Some { TagMap.empty with id = TagMapId "new" })
        else
            Data.TagMap.findById (TagMapId tagMapId) webLogId (conn ctx)
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
let saveMapping : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let  webLogId = webLogId ctx
    let  conn     = conn     ctx
    let! model    = ctx.BindFormAsync<EditTagMapModel> ()
    let  tagMap   =
        if model.id = "new" then
            Task.FromResult (Some { TagMap.empty with id = TagMapId.create (); webLogId = webLogId })
        else
            Data.TagMap.findById (TagMapId model.id) webLogId conn
    match! tagMap with
    | Some tm ->
        do! Data.TagMap.save { tm with tag = model.tag.ToLower (); urlValue = model.urlValue.ToLower () } conn
        do! addMessage ctx { UserMessage.success with message = "Tag mapping saved successfully" }
        return! redirectToGet $"/admin/tag-mapping/{TagMapId.toString tm.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/tag-mapping/{id}/delete
let deleteMapping tagMapId : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    match! Data.TagMap.delete (TagMapId tagMapId) (webLogId ctx) (conn ctx) with
    | true  -> do! addMessage ctx { UserMessage.success with message = "Tag mapping deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Tag mapping not found; nothing deleted" }
    return! redirectToGet "/admin/tag-mappings" next ctx
}
