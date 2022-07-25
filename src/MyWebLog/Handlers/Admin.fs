/// Handlers to manipulate admin functions
module MyWebLog.Handlers.Admin

open System.Threading.Tasks
open Giraffe
open MyWebLog
open MyWebLog.ViewModels

// ~~ DASHBOARDS ~~

// GET /admin/dashboard
let dashboard : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let getCount (f : WebLogId -> Task<int>) = f ctx.WebLog.Id
    let data    = ctx.Data
    let posts   = getCount (data.Post.CountByStatus Published)
    let drafts  = getCount (data.Post.CountByStatus Draft)
    let pages   = getCount data.Page.CountAll
    let listed  = getCount data.Page.CountListed
    let cats    = getCount data.Category.CountAll
    let topCats = getCount data.Category.CountTopLevel
    let! _ = Task.WhenAll (posts, drafts, pages, listed, cats, topCats)
    return!
        hashForPage "Dashboard"
        |> addToHash ViewContext.Model {
                Posts              = posts.Result
                Drafts             = drafts.Result
                Pages              = pages.Result
                ListedPages        = listed.Result
                Categories         = cats.Result
                TopLevelCategories = topCats.Result
            }
        |> adminView "dashboard" next ctx
}

// GET /admin/dashboard/administration
let adminDashboard : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
    let! themes          = ctx.Data.Theme.All ()
    let! bodyTemplate    = TemplateCache.get adminTheme "theme-list-body" ctx.Data
    let  cachedTemplates = TemplateCache.allNames ()
    let! hash =
        hashForPage "myWebLog Administration"
        |> withAntiCsrf ctx
        |> addToHash "themes" (themes |> List.map (DisplayTheme.fromTheme WebLogCache.isThemeInUse) |> Array.ofList)
        |> addToHash "cached_themes" (
            themes
            |> Seq.ofList
            |> Seq.map (fun it -> [|
                ThemeId.toString it.Id
                it.Name
                cachedTemplates |> List.filter (fun n -> n.StartsWith (ThemeId.toString it.Id)) |> List.length |> string
            |])
            |> Array.ofSeq)
        |> addToHash "web_logs" (
            WebLogCache.all ()
            |> Seq.ofList
            |> Seq.sortBy (fun it -> it.Name)
            |> Seq.map (fun it -> [| WebLogId.toString it.Id; it.Name; it.UrlBase |])
            |> Array.ofSeq)
        |> addViewContext ctx
    return!
        addToHash "theme_list" (bodyTemplate.Render hash) hash
        |> adminView "admin-dashboard" next ctx
}

/// Redirect the user to the admin dashboard
let toAdminDashboard : HttpHandler = redirectToGet "admin/dashboard/administration"

// ~~ CACHES ~~

// POST /admin/cache/web-log/{id}/refresh
let refreshWebLogCache webLogId : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
    let data = ctx.Data
    if webLogId = "all" then
        do! WebLogCache.fill data
        for webLog in WebLogCache.all () do
            do! PageListCache.refresh webLog    data
            do! CategoryCache.refresh webLog.Id data
        do! addMessage ctx { UserMessage.success with Message = "Successfully refresh web log cache for all web logs" }
    else
        match! data.WebLog.FindById (WebLogId webLogId) with
        | Some webLog ->
            WebLogCache.set webLog
            do! PageListCache.refresh webLog    data
            do! CategoryCache.refresh webLog.Id data
            do! addMessage ctx
                    { UserMessage.success with Message = $"Successfully refreshed web log cache for {webLog.Name}" }
        | None ->
            do! addMessage ctx { UserMessage.error with Message = $"No web log exists with ID {webLogId}" }
    return! toAdminDashboard next ctx
}

// POST /admin/cache/theme/{id}/refresh
let refreshThemeCache themeId : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
    let data = ctx.Data
    if themeId = "all" then
        TemplateCache.empty ()
        do! ThemeAssetCache.fill data
        do! addMessage ctx
                { UserMessage.success with
                    Message = "Successfully cleared template cache and refreshed theme asset cache"
                }
    else
        match! data.Theme.FindById (ThemeId themeId) with
        | Some theme ->
            TemplateCache.invalidateTheme    theme.Id
            do! ThemeAssetCache.refreshTheme theme.Id data
            do! addMessage ctx
                    { UserMessage.success with
                        Message = $"Successfully cleared template cache and refreshed theme asset cache for {theme.Name}"
                    }
        | None ->
            do! addMessage ctx { UserMessage.error with Message = $"No theme exists with ID {themeId}" }
    return! toAdminDashboard next ctx
}

// ~~ CATEGORIES ~~

// GET /admin/categories
let listCategories : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let! catListTemplate = TemplateCache.get adminTheme "category-list-body" ctx.Data
    let! hash =
        hashForPage "Categories"
        |> withAntiCsrf ctx
        |> addViewContext ctx
    return!
           addToHash "category_list" (catListTemplate.Render hash) hash
        |> adminView "category-list" next ctx
}

// GET /admin/categories/bare
let listCategoriesBare : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx ->
    hashForPage "Categories"
    |> withAntiCsrf ctx
    |> adminBareView "category-list-body" next ctx


// GET /admin/category/{id}/edit
let editCategory catId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let! result = task {
        match catId with
        | "new" -> return Some ("Add a New Category", { Category.empty with Id = CategoryId "new" })
        | _ ->
            match! ctx.Data.Category.FindById (CategoryId catId) ctx.WebLog.Id with
            | Some cat -> return Some ("Edit Category", cat)
            | None -> return None
    }
    match result with
    | Some (title, cat) ->
        return!
            hashForPage title
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model (EditCategoryModel.fromCategory cat)
            |> adminBareView "category-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/category/save
let saveCategory : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let  data     = ctx.Data
    let! model    = ctx.BindFormAsync<EditCategoryModel> ()
    let  category =
        if model.IsNew then someTask { Category.empty with Id = CategoryId.create (); WebLogId = ctx.WebLog.Id }
        else data.Category.FindById (CategoryId model.CategoryId) ctx.WebLog.Id
    match! category with
    | Some cat ->
        let updatedCat =
            { cat with
                Name        = model.Name
                Slug        = model.Slug
                Description = if model.Description = "" then None else Some model.Description
                ParentId    = if model.ParentId    = "" then None else Some (CategoryId model.ParentId)
            }
        do! (if model.IsNew then data.Category.Add else data.Category.Update) updatedCat
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with Message = "Category saved successfully" }
        return! listCategoriesBare next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/category/{id}/delete
let deleteCategory catId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    match! ctx.Data.Category.Delete (CategoryId catId) ctx.WebLog.Id with
    | true ->
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with Message = "Category deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with Message = "Category not found; cannot delete" }
    return! listCategoriesBare next ctx
}

open Microsoft.AspNetCore.Http

// ~~ TAG MAPPINGS ~~

/// Get the hash necessary to render the tag mapping list
let private tagMappingHash (ctx : HttpContext) = task {
    let! mappings = ctx.Data.TagMap.FindByWebLog ctx.WebLog.Id
    return!
        hashForPage "Tag Mappings"
        |> withAntiCsrf ctx
        |> addToHash "mappings"    mappings
        |> addToHash "mapping_ids" (mappings |> List.map (fun it -> { Name = it.Tag; Value = TagMapId.toString it.Id }))
        |> addViewContext ctx
}

// GET /admin/settings/tag-mappings
let tagMappings : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let! hash         = tagMappingHash ctx
    let! listTemplate = TemplateCache.get adminTheme "tag-mapping-list-body" ctx.Data
    return!
           addToHash "tag_mapping_list" (listTemplate.Render hash) hash
        |> adminView "tag-mapping-list" next ctx
}

// GET /admin/settings/tag-mappings/bare
let tagMappingsBare : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let! hash = tagMappingHash ctx
    return! adminBareView "tag-mapping-list-body" next ctx hash
}

// GET /admin/settings/tag-mapping/{id}/edit
let editMapping tagMapId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let isNew  = tagMapId = "new"
    let tagMap =
        if isNew then someTask { TagMap.empty with Id = TagMapId "new" }
        else ctx.Data.TagMap.FindById (TagMapId tagMapId) ctx.WebLog.Id
    match! tagMap with
    | Some tm ->
        return!
            hashForPage (if isNew then "Add Tag Mapping" else $"Mapping for {tm.Tag} Tag") 
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model (EditTagMapModel.fromMapping tm)
            |> adminBareView "tag-mapping-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/settings/tag-mapping/save
let saveMapping : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let  data   = ctx.Data
    let! model  = ctx.BindFormAsync<EditTagMapModel> ()
    let  tagMap =
        if model.IsNew then someTask { TagMap.empty with Id = TagMapId.create (); WebLogId = ctx.WebLog.Id }
        else data.TagMap.FindById (TagMapId model.Id) ctx.WebLog.Id
    match! tagMap with
    | Some tm ->
        do! data.TagMap.Save { tm with Tag = model.Tag.ToLower (); UrlValue = model.UrlValue.ToLower () }
        do! addMessage ctx { UserMessage.success with Message = "Tag mapping saved successfully" }
        return! tagMappingsBare next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/settings/tag-mapping/{id}/delete
let deleteMapping tagMapId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    match! ctx.Data.TagMap.Delete (TagMapId tagMapId) ctx.WebLog.Id with
    | true  -> do! addMessage ctx { UserMessage.success with Message = "Tag mapping deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with Message = "Tag mapping not found; nothing deleted" }
    return! tagMappingsBare next ctx
}

// ~~ THEMES ~~

open System
open System.IO
open System.IO.Compression
open System.Text.RegularExpressions
open MyWebLog.Data

// GET /admin/theme/list
let listThemes : HttpHandler = requireAccess Administrator >=> fun next ctx -> task { 
    let! themes = ctx.Data.Theme.All ()
    return!
        hashForPage "Themes"
        |> withAntiCsrf ctx
        |> addToHash "themes" (themes |> List.map (DisplayTheme.fromTheme WebLogCache.isThemeInUse) |> Array.ofList)
        |> adminBareView "theme-list-body" next ctx
}

// GET /admin/theme/new
let addTheme : HttpHandler = requireAccess Administrator >=> fun next ctx ->
    hashForPage "Upload a Theme File"
    |> withAntiCsrf ctx
    |> adminBareView "theme-upload" next ctx

/// Update the name and version for a theme based on the version.txt file, if present
let private updateNameAndVersion (theme : Theme) (zip : ZipArchive) = backgroundTask {
    let now () = DateTime.UtcNow.ToString "yyyyMMdd.HHmm"
    match zip.Entries |> Seq.filter (fun it -> it.FullName = "version.txt") |> Seq.tryHead with
    | Some versionItem ->
        use  versionFile = new StreamReader(versionItem.Open ())
        let! versionText = versionFile.ReadToEndAsync ()
        let  parts       = versionText.Trim().Replace("\r", "").Split "\n"
        let  displayName = if parts[0] > "" then parts[0] else ThemeId.toString theme.Id
        let  version     = if parts.Length > 1 && parts[1] > "" then parts[1] else now ()
        return { theme with Name = displayName; Version = version }
    | None -> return { theme with Name = ThemeId.toString theme.Id; Version = now () }
}

/// Update the theme with all templates from the ZIP archive
let private updateTemplates (theme : Theme) (zip : ZipArchive) = backgroundTask {
    let tasks =
        zip.Entries
        |> Seq.filter (fun it -> it.Name.EndsWith ".liquid")
        |> Seq.map (fun templateItem -> backgroundTask {
            use templateFile = new StreamReader (templateItem.Open ())
            let! template = templateFile.ReadToEndAsync ()
            return { Name = templateItem.Name.Replace (".liquid", ""); Text = template }
        })
    let! templates = Task.WhenAll tasks
    return
        templates
        |> Array.fold (fun t template ->
            { t with Templates = template :: (t.Templates |> List.filter (fun it -> it.Name <> template.Name)) })
            theme
}

/// Update theme assets from the ZIP archive
let private updateAssets themeId (zip : ZipArchive) (data : IData) = backgroundTask {
    for asset in zip.Entries |> Seq.filter (fun it -> it.FullName.StartsWith "wwwroot") do
        let assetName = asset.FullName.Replace ("wwwroot/", "")
        if assetName <> "" && not (assetName.EndsWith "/") then
            use stream = new MemoryStream ()
            do! asset.Open().CopyToAsync stream
            do! data.ThemeAsset.Save
                    {   Id        = ThemeAssetId (themeId, assetName)
                        UpdatedOn = asset.LastWriteTime.DateTime
                        Data      = stream.ToArray ()
                    }
}

/// Get the theme name from the file name given
let getThemeIdFromFileName (fileName : string) =
    let themeName = fileName.Split(".").[0].ToLowerInvariant().Replace (" ", "-")
    if themeName.EndsWith "-theme" then
        if Regex.IsMatch (themeName, """^[a-z0-9\-]+$""") then
            Ok (ThemeId (themeName.Substring (0, themeName.Length - 6)))
        else Error $"Theme ID {fileName} is invalid"
    else Error "Theme .zip file name must end in \"-theme.zip\""

/// Load a theme from the given stream, which should contain a ZIP archive
let loadThemeFromZip themeId file (data : IData) = backgroundTask {
    let! isNew, theme = backgroundTask {
        match! data.Theme.FindById themeId with
        | Some t -> return false, t
        | None   -> return true, { Theme.empty with Id = themeId }
    }
    use  zip   = new ZipArchive (file, ZipArchiveMode.Read)
    let! theme = updateNameAndVersion theme zip
    if not isNew then do! data.ThemeAsset.DeleteByTheme theme.Id
    let! theme = updateTemplates { theme with Templates = [] } zip
    do! data.Theme.Save theme
    do! updateAssets themeId zip data
    
    return theme
}

// POST /admin/theme/new
let saveTheme : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
    if ctx.Request.HasFormContentType && ctx.Request.Form.Files.Count > 0 then
        let themeFile = Seq.head ctx.Request.Form.Files
        match getThemeIdFromFileName themeFile.FileName with
        | Ok themeId when themeId <> adminTheme ->
            let  data   = ctx.Data
            let! exists = data.Theme.Exists themeId
            let  isNew  = not exists
            let! model  = ctx.BindFormAsync<UploadThemeModel> ()
            if isNew || model.DoOverwrite then 
                // Load the theme to the database
                use stream = new MemoryStream ()
                do! themeFile.CopyToAsync stream
                let! _ = loadThemeFromZip themeId stream data
                do! ThemeAssetCache.refreshTheme themeId data
                TemplateCache.invalidateTheme themeId
                // Save the .zip file
                use file = new FileStream ($"{ThemeId.toString themeId}-theme.zip", FileMode.Create)
                do! themeFile.CopyToAsync file
                do! addMessage ctx
                        { UserMessage.success with
                            Message = $"""Theme {if isNew then "add" else "updat"}ed successfully"""
                        }
                return! toAdminDashboard next ctx
            else
                do! addMessage ctx
                        { UserMessage.error with
                            Message = "Theme exists and overwriting was not requested; nothing saved"
                        }
                return! toAdminDashboard next ctx
        | Ok _ ->
            do! addMessage ctx { UserMessage.error with Message = "You may not replace the admin theme" }
            return! toAdminDashboard next ctx
        | Error message ->
            do! addMessage ctx { UserMessage.error with Message = message }
            return! toAdminDashboard next ctx
    else return! RequestErrors.BAD_REQUEST "Bad request" next ctx
}

// POST /admin/theme/{id}/delete
let deleteTheme themeId : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
    let data = ctx.Data
    match themeId with
    | "admin" | "default" ->
        do! addMessage ctx { UserMessage.error with Message = $"You may not delete the {themeId} theme" }
        return! listThemes next ctx
    | it when WebLogCache.isThemeInUse (ThemeId it) ->
        do! addMessage ctx
                { UserMessage.error with
                    Message = $"You may not delete the {themeId} theme, as it is currently in use"
                }
        return! listThemes next ctx
    | _ ->
        match! data.Theme.Delete (ThemeId themeId) with
        | true ->
            let zippedTheme = $"{themeId}-theme.zip"
            if File.Exists zippedTheme then File.Delete zippedTheme
            do! addMessage ctx { UserMessage.success with Message = $"Theme ID {themeId} deleted successfully" }
            return! listThemes next ctx
        | false -> return! Error.notFound next ctx
}

// ~~ WEB LOG SETTINGS ~~

open System.Collections.Generic

// GET /admin/settings
let settings : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let  data     = ctx.Data
    let! allPages = data.Page.All ctx.WebLog.Id
    let! themes   = data.Theme.All ()
    return!
        hashForPage "Web Log Settings"
        |> withAntiCsrf ctx
        |> addToHash ViewContext.Model (SettingsModel.fromWebLog ctx.WebLog)
        |> addToHash "pages" (
            seq {
                KeyValuePair.Create ("posts", "- First Page of Posts -")
                yield! allPages
                       |> List.sortBy (fun p -> p.Title.ToLower ())
                       |> List.map (fun p -> KeyValuePair.Create (PageId.toString p.Id, p.Title))
            }
            |> Array.ofSeq)
        |> addToHash "themes" (
            themes
            |> Seq.ofList
            |> Seq.map (fun it -> KeyValuePair.Create (ThemeId.toString it.Id, $"{it.Name} (v{it.Version})"))
            |> Array.ofSeq)
        |> addToHash "upload_values" [|
            KeyValuePair.Create (UploadDestination.toString Database, "Database")
            KeyValuePair.Create (UploadDestination.toString Disk,     "Disk")
        |]
        |> adminView "settings" next ctx
}

// POST /admin/settings
let saveSettings : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let  data  = ctx.Data
    let! model = ctx.BindFormAsync<SettingsModel> ()
    match! data.WebLog.FindById ctx.WebLog.Id with
    | Some webLog ->
        let oldSlug = webLog.Slug
        let webLog  = model.update webLog
        do! data.WebLog.UpdateSettings webLog

        // Update cache
        WebLogCache.set webLog
        
        if oldSlug <> webLog.Slug then
            // Rename disk directory if it exists
            let uploadRoot = Path.Combine ("wwwroot", "upload")
            let oldDir     = Path.Combine (uploadRoot, oldSlug)
            if Directory.Exists oldDir then Directory.Move (oldDir, Path.Combine (uploadRoot, webLog.Slug))
    
        do! addMessage ctx { UserMessage.success with Message = "Web log settings saved successfully" }
        return! redirectToGet "admin/settings" next ctx
    | None -> return! Error.notFound next ctx
}
