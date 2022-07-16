/// Handlers to manipulate admin functions
module MyWebLog.Handlers.Admin

open System.Threading.Tasks
open DotLiquid
open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// GET /admin
let dashboard : HttpHandler = fun next ctx -> task {
    let webLogId = ctx.WebLog.id
    let data     = ctx.Data
    let getCount (f : WebLogId -> Task<int>) = f webLogId
    let! posts   = data.Post.countByStatus Published |> getCount
    let! drafts  = data.Post.countByStatus Draft     |> getCount
    let! pages   = data.Page.countAll                |> getCount
    let! listed  = data.Page.countListed             |> getCount
    let! cats    = data.Category.countAll            |> getCount
    let! topCats = data.Category.countTopLevel       |> getCount
    return!
        Hash.FromAnonymousObject {|
            page_title = "Dashboard"
            model      =
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
    let! catListTemplate = TemplateCache.get "admin" "category-list-body" ctx.Data
    let hash = Hash.FromAnonymousObject {|
        page_title = "Categories"
        csrf       = ctx.CsrfTokenSet
        web_log    = ctx.WebLog
        categories = CategoryCache.get ctx
    |}
    hash.Add ("category_list", catListTemplate.Render hash)
    return! viewForTheme "admin" "category-list" next ctx hash
}

// GET /admin/categories/bare
let listCategoriesBare : HttpHandler = fun next ctx -> task {
    return!
        Hash.FromAnonymousObject {|
            categories = CategoryCache.get ctx
            csrf       = ctx.CsrfTokenSet
        |}
        |> bareForTheme "admin" "category-list-body" next ctx
}


// GET /admin/category/{id}/edit
let editCategory catId : HttpHandler = fun next ctx -> task {
    let! result = task {
        match catId with
        | "new" -> return Some ("Add a New Category", { Category.empty with id = CategoryId "new" })
        | _ ->
            match! ctx.Data.Category.findById (CategoryId catId) ctx.WebLog.id with
            | Some cat -> return Some ("Edit Category", cat)
            | None -> return None
    }
    match result with
    | Some (title, cat) ->
        return!
            Hash.FromAnonymousObject {|
                page_title = title
                csrf       = ctx.CsrfTokenSet
                model      = EditCategoryModel.fromCategory cat
                categories = CategoryCache.get ctx
            |}
            |> bareForTheme "admin" "category-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/category/save
let saveCategory : HttpHandler = fun next ctx -> task {
    let  webLog   = ctx.WebLog
    let  data     = ctx.Data
    let! model    = ctx.BindFormAsync<EditCategoryModel> ()
    let! category = task {
        match model.categoryId with
        | "new" -> return Some { Category.empty with id = CategoryId.create (); webLogId = webLog.id }
        | catId -> return! data.Category.findById (CategoryId catId) webLog.id
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
        do! (match model.categoryId with "new" -> data.Category.add | _ -> data.Category.update) cat
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Category saved successfully" }
        return! listCategoriesBare next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/category/{id}/delete
let deleteCategory catId : HttpHandler = fun next ctx -> task {
    match! ctx.Data.Category.delete (CategoryId catId) ctx.WebLog.id with
    | true ->
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Category deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Category not found; cannot delete" }
    return! listCategoriesBare next ctx
}

open Microsoft.AspNetCore.Http

// -- TAG MAPPINGS --

/// Get the hash necessary to render the tag mapping list
let private tagMappingHash (ctx : HttpContext) = task {
    let! mappings = ctx.Data.TagMap.findByWebLog ctx.WebLog.id
    return Hash.FromAnonymousObject {|
        csrf        = ctx.CsrfTokenSet
        web_log     = ctx.WebLog
        mappings    = mappings
        mapping_ids = mappings |> List.map (fun it -> { name = it.tag; value = TagMapId.toString it.id })
    |}
}

// GET /admin/settings/tag-mappings
let tagMappings : HttpHandler = fun next ctx -> task {
    let! hash         = tagMappingHash ctx
    let! listTemplate = TemplateCache.get "admin" "tag-mapping-list-body" ctx.Data
    
    hash.Add ("tag_mapping_list", listTemplate.Render hash)
    hash.Add ("page_title", "Tag Mappings")
    
    return! viewForTheme "admin" "tag-mapping-list" next ctx hash
}

// GET /admin/settings/tag-mappings/bare
let tagMappingsBare : HttpHandler = fun next ctx -> task {
    let! hash = tagMappingHash ctx
    return! bareForTheme "admin" "tag-mapping-list-body" next ctx hash
}

// GET /admin/settings/tag-mapping/{id}/edit
let editMapping tagMapId : HttpHandler = fun next ctx -> task {
    let isNew  = tagMapId = "new"
    let tagMap =
        if isNew then
            Task.FromResult (Some { TagMap.empty with id = TagMapId "new" })
        else
            ctx.Data.TagMap.findById (TagMapId tagMapId) ctx.WebLog.id
    match! tagMap with
    | Some tm ->
        return!
            Hash.FromAnonymousObject {|
                page_title = if isNew then "Add Tag Mapping" else $"Mapping for {tm.tag} Tag" 
                csrf       = ctx.CsrfTokenSet
                model      = EditTagMapModel.fromMapping tm
            |}
            |> bareForTheme "admin" "tag-mapping-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/settings/tag-mapping/save
let saveMapping : HttpHandler = fun next ctx -> task {
    let  data   = ctx.Data
    let! model  = ctx.BindFormAsync<EditTagMapModel> ()
    let  tagMap =
        if model.id = "new" then
            Task.FromResult (Some { TagMap.empty with id = TagMapId.create (); webLogId = ctx.WebLog.id })
        else
            data.TagMap.findById (TagMapId model.id) ctx.WebLog.id
    match! tagMap with
    | Some tm ->
        do! data.TagMap.save { tm with tag = model.tag.ToLower (); urlValue = model.urlValue.ToLower () }
        do! addMessage ctx { UserMessage.success with message = "Tag mapping saved successfully" }
        return! tagMappingsBare next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/settings/tag-mapping/{id}/delete
let deleteMapping tagMapId : HttpHandler = fun next ctx -> task {
    match! ctx.Data.TagMap.delete (TagMapId tagMapId) ctx.WebLog.id with
    | true  -> do! addMessage ctx { UserMessage.success with message = "Tag mapping deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Tag mapping not found; nothing deleted" }
    return! tagMappingsBare next ctx
}

// -- THEMES --

open System
open System.IO
open System.IO.Compression
open System.Text.RegularExpressions
open MyWebLog.Data

// GET /admin/theme/update
let themeUpdatePage : HttpHandler = fun next ctx -> task {
    return!
        Hash.FromAnonymousObject {|
            page_title = "Upload Theme"
            csrf       = ctx.CsrfTokenSet
        |}
        |> viewForTheme "admin" "upload-theme" next ctx
}

/// Update the name and version for a theme based on the version.txt file, if present
let private updateNameAndVersion (theme : Theme) (zip : ZipArchive) = backgroundTask {
    let now () = DateTime.UtcNow.ToString "yyyyMMdd.HHmm"
    match zip.Entries |> Seq.filter (fun it -> it.FullName = "version.txt") |> Seq.tryHead with
    | Some versionItem ->
        use  versionFile = new StreamReader(versionItem.Open ())
        let! versionText = versionFile.ReadToEndAsync ()
        let  parts       = versionText.Trim().Replace("\r", "").Split "\n"
        let  displayName = if parts[0] > "" then parts[0] else ThemeId.toString theme.id
        let  version     = if parts.Length > 1 && parts[1] > "" then parts[1] else now ()
        return { theme with name = displayName; version = version }
    | None ->
        return { theme with name = ThemeId.toString theme.id; version = now () }
}

/// Delete all theme assets, and remove templates from theme
let private checkForCleanLoad (theme : Theme) cleanLoad (data : IData) = backgroundTask {
    if cleanLoad then
        do! data.ThemeAsset.deleteByTheme theme.id
        return { theme with templates = [] }
    else
        return theme
}

/// Update the theme with all templates from the ZIP archive
let private updateTemplates (theme : Theme) (zip : ZipArchive) = backgroundTask {
    let tasks =
        zip.Entries
        |> Seq.filter (fun it -> it.Name.EndsWith ".liquid")
        |> Seq.map (fun templateItem -> backgroundTask {
            use templateFile = new StreamReader (templateItem.Open ())
            let! template = templateFile.ReadToEndAsync ()
            return { name = templateItem.Name.Replace (".liquid", ""); text = template }
        })
    let! templates = Task.WhenAll tasks
    return
        templates
        |> Array.fold (fun t template ->
            { t with templates = template :: (t.templates |> List.filter (fun it -> it.name <> template.name)) })
            theme
}

/// Update theme assets from the ZIP archive
let private updateAssets themeId (zip : ZipArchive) (data : IData) = backgroundTask {
    for asset in zip.Entries |> Seq.filter (fun it -> it.FullName.StartsWith "wwwroot") do
        let assetName = asset.FullName.Replace ("wwwroot/", "")
        if assetName <> "" && not (assetName.EndsWith "/") then
            use stream = new MemoryStream ()
            do! asset.Open().CopyToAsync stream
            do! data.ThemeAsset.save
                    { id        = ThemeAssetId (themeId, assetName)
                      updatedOn = asset.LastWriteTime.DateTime
                      data      = stream.ToArray ()
                    }
}

/// Get the theme name from the file name given
let getThemeName (fileName : string) =
    let themeName = fileName.Split(".").[0].ToLowerInvariant().Replace (" ", "-")
    if Regex.IsMatch (themeName, """^[a-z0-9\-]+$""") then Ok themeName else Error $"Theme name {fileName} is invalid"

/// Load a theme from the given stream, which should contain a ZIP archive
let loadThemeFromZip themeName file clean (data : IData) = backgroundTask {
    use  zip     = new ZipArchive (file, ZipArchiveMode.Read)
    let  themeId = ThemeId themeName
    let! theme   = backgroundTask {
        match! data.Theme.findById themeId with
        | Some t -> return t
        | None   -> return { Theme.empty with id = themeId }
    }
    let! theme = updateNameAndVersion theme   zip
    let! theme = checkForCleanLoad    theme   clean data
    let! theme = updateTemplates      theme   zip
    do! data.Theme.save theme
    do! updateAssets themeId zip data
}

// POST /admin/theme/update
let updateTheme : HttpHandler = fun next ctx -> task {
    if ctx.Request.HasFormContentType && ctx.Request.Form.Files.Count > 0 then
        let themeFile = Seq.head ctx.Request.Form.Files
        match getThemeName themeFile.FileName with
        | Ok themeName when themeName <> "admin" ->
            let data   = ctx.Data
            use stream = new MemoryStream ()
            do! themeFile.CopyToAsync stream
            do! loadThemeFromZip themeName stream true data
            do! ThemeAssetCache.refreshTheme (ThemeId themeName) data
            TemplateCache.invalidateTheme themeName
            do! addMessage ctx { UserMessage.success with message = "Theme updated successfully" }
            return! redirectToGet "admin/dashboard" next ctx
        | Ok _ ->
            do! addMessage ctx { UserMessage.error with message = "You may not replace the admin theme" }
            return! redirectToGet "admin/theme/update" next ctx
        | Error message ->
            do! addMessage ctx { UserMessage.error with message = message }
            return! redirectToGet "admin/theme/update" next ctx
    else
        return! RequestErrors.BAD_REQUEST "Bad request" next ctx
}

// -- WEB LOG SETTINGS --

open System.Collections.Generic

// GET /admin/settings
let settings : HttpHandler = fun next ctx -> task {
    let  webLog   = ctx.WebLog
    let  data     = ctx.Data
    let! allPages = data.Page.all webLog.id
    let! themes   = data.Theme.all ()
    return!
        Hash.FromAnonymousObject {|
            page_title = "Web Log Settings"
            csrf       = ctx.CsrfTokenSet
            web_log    = webLog
            model      = SettingsModel.fromWebLog webLog
            pages      = seq
                {   KeyValuePair.Create ("posts", "- First Page of Posts -")
                    yield! allPages
                           |> List.sortBy (fun p -> p.title.ToLower ())
                           |> List.map (fun p -> KeyValuePair.Create (PageId.toString p.id, p.title))
                }
                |> Array.ofSeq
            themes =
                themes
                 |> Seq.ofList
                 |> Seq.map (fun it -> KeyValuePair.Create (ThemeId.toString it.id, $"{it.name} (v{it.version})"))
                 |> Array.ofSeq
            upload_values = [|
                    KeyValuePair.Create (UploadDestination.toString Database, "Database")
                    KeyValuePair.Create (UploadDestination.toString Disk,     "Disk")
                |]
        |}
        |> viewForTheme "admin" "settings" next ctx
}

// POST /admin/settings
let saveSettings : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  data   = ctx.Data
    let! model  = ctx.BindFormAsync<SettingsModel> ()
    match! data.WebLog.findById webLog.id with
    | Some webLog ->
        let oldSlug = webLog.slug
        let webLog  = model.update webLog
        do! data.WebLog.updateSettings webLog

        // Update cache
        WebLogCache.set webLog
        
        if oldSlug <> webLog.slug then
            // Rename disk directory if it exists
            let uploadRoot = Path.Combine ("wwwroot", "upload")
            let oldDir     = Path.Combine (uploadRoot, oldSlug)
            if Directory.Exists oldDir then Directory.Move (oldDir, Path.Combine (uploadRoot, webLog.slug))
    
        do! addMessage ctx { UserMessage.success with message = "Web log settings saved successfully" }
        return! redirectToGet "admin/settings" next ctx
    | None -> return! Error.notFound next ctx
}
