/// Handlers to manipulate admin functions
module MyWebLog.Handlers.Admin

open System.Threading.Tasks
open Giraffe
open MyWebLog
open MyWebLog.ViewModels
open NodaTime

/// ~~~ DASHBOARDS ~~~
module Dashboard =
    
    // GET /admin/dashboard
    let user : HttpHandler = requireAccess Author >=> fun next ctx -> task {
        let getCount (f : WebLogId -> Task<int>) = f ctx.WebLog.Id
        let  data    = ctx.Data
        let! posts   = getCount (data.Post.CountByStatus Published)
        let! drafts  = getCount (data.Post.CountByStatus Draft)
        let! pages   = getCount data.Page.CountAll
        let! listed  = getCount data.Page.CountListed
        let! cats    = getCount data.Category.CountAll
        let! topCats = getCount data.Category.CountTopLevel
        return!
            hashForPage "Dashboard"
            |> addToHash ViewContext.Model {
                    Posts              = posts
                    Drafts             = drafts
                    Pages              = pages
                    ListedPages        = listed
                    Categories         = cats
                    TopLevelCategories = topCats
                }
            |> adminView "dashboard" next ctx
    }

    // GET /admin/administration
    let admin : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
        match! TemplateCache.get adminTheme "theme-list-body" ctx.Data with
        | Ok bodyTemplate ->
            let! themes          = ctx.Data.Theme.All ()
            let  cachedTemplates = TemplateCache.allNames ()
            let! hash =
                hashForPage "myWebLog Administration"
                |> withAntiCsrf ctx
                |> addToHash "themes" (
                    themes
                    |> List.map (DisplayTheme.fromTheme WebLogCache.isThemeInUse)
                    |> Array.ofList)
                |> addToHash "cached_themes" (
                    themes
                    |> Seq.ofList
                    |> Seq.map (fun it -> [|
                        ThemeId.toString it.Id
                        it.Name
                        cachedTemplates
                        |> List.filter (fun n -> n.StartsWith (ThemeId.toString it.Id))
                        |> List.length
                        |> string
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
        | Error message -> return! Error.server message next ctx
    }

/// Redirect the user to the admin dashboard
let toAdminDashboard : HttpHandler = redirectToGet "admin/administration"


/// ~~~ CACHES ~~~
module Cache =
    
    // POST /admin/cache/web-log/{id}/refresh
    let refreshWebLog webLogId : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
        let data = ctx.Data
        if webLogId = "all" then
            do! WebLogCache.fill data
            for webLog in WebLogCache.all () do
                do! PageListCache.refresh webLog    data
                do! CategoryCache.refresh webLog.Id data
            do! addMessage ctx
                    { UserMessage.success with Message = "Successfully refresh web log cache for all web logs" }
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
    let refreshTheme themeId : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
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


/// ~~~ CATEGORIES ~~~
module Category =
    
    open MyWebLog.Data

    // GET /admin/categories
    let all : HttpHandler = fun next ctx -> task {
        match! TemplateCache.get adminTheme "category-list-body" ctx.Data with
        | Ok catListTemplate ->
            let! hash =
                hashForPage "Categories"
                |> withAntiCsrf ctx
                |> addViewContext ctx
            return!
                   addToHash "category_list" (catListTemplate.Render hash) hash
                |> adminView "category-list" next ctx
        | Error message -> return! Error.server message next ctx
    }

    // GET /admin/categories/bare
    let bare : HttpHandler = fun next ctx ->
        hashForPage "Categories"
        |> withAntiCsrf ctx
        |> adminBareView "category-list-body" next ctx


    // GET /admin/category/{id}/edit
    let edit catId : HttpHandler = fun next ctx -> task {
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
    let save : HttpHandler = fun next ctx -> task {
        let  data     = ctx.Data
        let! model    = ctx.BindFormAsync<EditCategoryModel> ()
        let  category =
            if model.IsNew then someTask { Category.empty with Id = CategoryId.Create(); WebLogId = ctx.WebLog.Id }
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
            return! bare next ctx
        | None -> return! Error.notFound next ctx
    }

    // POST /admin/category/{id}/delete
    let delete catId : HttpHandler = fun next ctx -> task {
        let! result = ctx.Data.Category.Delete (CategoryId catId) ctx.WebLog.Id
        match result with
        | CategoryDeleted
        | ReassignedChildCategories ->
            do! CategoryCache.update ctx
            let detail =
                match result with
                | ReassignedChildCategories ->
                    Some "<em>(Its child categories were reassigned to its parent category)</em>"
                | _ -> None
            do! addMessage ctx { UserMessage.success with Message = "Category deleted successfully"; Detail = detail }
        | CategoryNotFound ->
            do! addMessage ctx { UserMessage.error with Message = "Category not found; cannot delete" }
        return! bare next ctx
    }


/// ~~~ REDIRECT RULES ~~~
module RedirectRules =

    open Microsoft.AspNetCore.Http

    // GET /admin/settings/redirect-rules
    let all : HttpHandler = fun next ctx -> task {
        return!
            hashForPage "Redirect Rules"
            |> withAntiCsrf ctx
            |> addToHash "redirections" ctx.WebLog.RedirectRules
            |> adminView "redirect-list" next ctx
    }

    // GET /admin/settings/redirect-rules/[index]
    let edit idx : HttpHandler = fun next ctx -> task {
        if idx = -1 then
            return!
                hashForPage "Add Redirect Rule"
                |> addToHash "model" (EditRedirectRuleModel.fromRule -1 RedirectRule.Empty)
                |> withAntiCsrf ctx
                |> adminBareView "redirect-edit" next ctx
        else        
            let rules = ctx.WebLog.RedirectRules
            if rules.Length < idx || idx < 0 then
                return! Error.notFound next ctx
            else
                return!
                    hashForPage "Edit Redirect Rule"
                    |> addToHash "model" (EditRedirectRuleModel.fromRule idx (List.item idx rules))
                    |> withAntiCsrf ctx
                    |> adminBareView "redirect-edit" next ctx
    }

    /// Update the web log's redirect rules in the database, the request web log, and the web log cache
    let private updateRedirectRules (ctx : HttpContext) webLog = backgroundTask {
        do! ctx.Data.WebLog.UpdateRedirectRules webLog
        ctx.Items["webLog"] <- webLog
        WebLogCache.set webLog
    }

    // POST /admin/settings/redirect-rules/[index]
    let save idx : HttpHandler = fun next ctx -> task {
        let! model    = ctx.BindFormAsync<EditRedirectRuleModel> ()
        let  isNew    = idx = -1
        let  rules    = ctx.WebLog.RedirectRules
        let  rule     = model.UpdateRule (if isNew then RedirectRule.Empty else List.item idx rules)
        let  newRules =
            match isNew with
            | true when model.InsertAtTop -> List.insertAt 0 rule rules
            | true -> List.insertAt (rules.Length) rule rules
            | false -> rules |> List.removeAt idx |> List.insertAt idx rule
        do! updateRedirectRules ctx { ctx.WebLog with RedirectRules = newRules }
        do! addMessage ctx { UserMessage.success with Message = "Redirect rule saved successfully" }
        return! all next ctx
    }

    // POST /admin/settings/redirect-rules/[index]/up
    let moveUp idx : HttpHandler = fun next ctx -> task {
        if idx < 1 || idx >= ctx.WebLog.RedirectRules.Length then
            return! Error.notFound next ctx
        else
            let toMove   = List.item idx ctx.WebLog.RedirectRules
            let newRules = ctx.WebLog.RedirectRules |> List.removeAt idx |> List.insertAt (idx - 1) toMove
            do! updateRedirectRules ctx { ctx.WebLog with RedirectRules = newRules }
            return! all next ctx
    }

    // POST /admin/settings/redirect-rules/[index]/down
    let moveDown idx : HttpHandler = fun next ctx -> task {
        if idx < 0 || idx >= ctx.WebLog.RedirectRules.Length - 1 then
            return! Error.notFound next ctx
        else
            let toMove   = List.item idx ctx.WebLog.RedirectRules
            let newRules = ctx.WebLog.RedirectRules |> List.removeAt idx |> List.insertAt (idx + 1) toMove
            do! updateRedirectRules ctx { ctx.WebLog with RedirectRules = newRules }
            return! all next ctx
    }

    // POST /admin/settings/redirect-rules/[index]/delete
    let delete idx : HttpHandler = fun next ctx -> task {
        if idx < 0 || idx >= ctx.WebLog.RedirectRules.Length then
            return! Error.notFound next ctx
        else
            let rules = ctx.WebLog.RedirectRules |> List.removeAt idx
            do! updateRedirectRules ctx { ctx.WebLog with RedirectRules = rules }
            do! addMessage ctx { UserMessage.success with Message = "Redirect rule deleted successfully" }
            return! all next ctx
    }


/// ~~~ TAG MAPPINGS ~~~
module TagMapping =
    
    open Microsoft.AspNetCore.Http

    /// Add tag mappings to the given hash
    let withTagMappings (ctx : HttpContext) hash = task {
        let! mappings = ctx.Data.TagMap.FindByWebLog ctx.WebLog.Id
        return
               addToHash "mappings"    mappings hash
            |> addToHash "mapping_ids" (
                mappings
                |> List.map (fun it -> { Name = it.Tag; Value = TagMapId.toString it.Id }))
    }

    // GET /admin/settings/tag-mappings
    let all : HttpHandler = fun next ctx -> task {
        let! hash =
            hashForPage ""
            |> withAntiCsrf    ctx
            |> withTagMappings ctx
        return! adminBareView "tag-mapping-list-body" next ctx hash
    }

    // GET /admin/settings/tag-mapping/{id}/edit
    let edit tagMapId : HttpHandler = fun next ctx -> task {
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
    let save : HttpHandler = fun next ctx -> task {
        let  data   = ctx.Data
        let! model  = ctx.BindFormAsync<EditTagMapModel> ()
        let  tagMap =
            if model.IsNew then someTask { TagMap.empty with Id = TagMapId.create (); WebLogId = ctx.WebLog.Id }
            else data.TagMap.FindById (TagMapId model.Id) ctx.WebLog.Id
        match! tagMap with
        | Some tm ->
            do! data.TagMap.Save { tm with Tag = model.Tag.ToLower (); UrlValue = model.UrlValue.ToLower () }
            do! addMessage ctx { UserMessage.success with Message = "Tag mapping saved successfully" }
            return! all next ctx
        | None -> return! Error.notFound next ctx
    }

    // POST /admin/settings/tag-mapping/{id}/delete
    let delete tagMapId : HttpHandler = fun next ctx -> task {
        match! ctx.Data.TagMap.Delete (TagMapId tagMapId) ctx.WebLog.Id with
        | true  -> do! addMessage ctx { UserMessage.success with Message = "Tag mapping deleted successfully" }
        | false -> do! addMessage ctx { UserMessage.error with Message = "Tag mapping not found; nothing deleted" }
        return! all next ctx
    }


/// ~~~ THEMES ~~~
module Theme =
    
    open System
    open System.IO
    open System.IO.Compression
    open System.Text.RegularExpressions
    open MyWebLog.Data

    // GET /admin/theme/list
    let all : HttpHandler = requireAccess Administrator >=> fun next ctx -> task { 
        let! themes = ctx.Data.Theme.All ()
        return!
            hashForPage "Themes"
            |> withAntiCsrf ctx
            |> addToHash "themes" (themes |> List.map (DisplayTheme.fromTheme WebLogCache.isThemeInUse) |> Array.ofList)
            |> adminBareView "theme-list-body" next ctx
    }

    // GET /admin/theme/new
    let add : HttpHandler = requireAccess Administrator >=> fun next ctx ->
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
                            UpdatedOn = LocalDateTime.FromDateTime(asset.LastWriteTime.DateTime)
                                            .InZoneLeniently(DateTimeZone.Utc).ToInstant ()
                            Data      = stream.ToArray ()
                        }
    }

    /// Derive the theme ID from the file name given
    let deriveIdFromFileName (fileName : string) =
        let themeName = fileName.Split(".").[0].ToLowerInvariant().Replace (" ", "-")
        if themeName.EndsWith "-theme" then
            if Regex.IsMatch (themeName, """^[a-z0-9\-]+$""") then
                Ok (ThemeId (themeName.Substring (0, themeName.Length - 6)))
            else Error $"Theme ID {fileName} is invalid"
        else Error "Theme .zip file name must end in \"-theme.zip\""

    /// Load a theme from the given stream, which should contain a ZIP archive
    let loadFromZip themeId file (data : IData) = backgroundTask {
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
    let save : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
        if ctx.Request.HasFormContentType && ctx.Request.Form.Files.Count > 0 then
            let themeFile = Seq.head ctx.Request.Form.Files
            match deriveIdFromFileName themeFile.FileName with
            | Ok themeId when themeId <> adminTheme ->
                let  data   = ctx.Data
                let! exists = data.Theme.Exists themeId
                let  isNew  = not exists
                let! model  = ctx.BindFormAsync<UploadThemeModel> ()
                if isNew || model.DoOverwrite then 
                    // Load the theme to the database
                    use stream = new MemoryStream ()
                    do! themeFile.CopyToAsync stream
                    let! _ = loadFromZip themeId stream data
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
    let delete themeId : HttpHandler = requireAccess Administrator >=> fun next ctx -> task {
        let data = ctx.Data
        match themeId with
        | "admin" | "default" ->
            do! addMessage ctx { UserMessage.error with Message = $"You may not delete the {themeId} theme" }
            return! all next ctx
        | it when WebLogCache.isThemeInUse (ThemeId it) ->
            do! addMessage ctx
                    { UserMessage.error with
                        Message = $"You may not delete the {themeId} theme, as it is currently in use"
                    }
            return! all next ctx
        | _ ->
            match! data.Theme.Delete (ThemeId themeId) with
            | true ->
                let zippedTheme = $"{themeId}-theme.zip"
                if File.Exists zippedTheme then File.Delete zippedTheme
                do! addMessage ctx { UserMessage.success with Message = $"Theme ID {themeId} deleted successfully" }
                return! all next ctx
            | false -> return! Error.notFound next ctx
    }


/// ~~~ WEB LOG SETTINGS ~~~
module WebLog =
    
    open System.Collections.Generic
    open System.IO

    // GET /admin/settings
    let settings : HttpHandler = fun next ctx -> task {
        let data = ctx.Data
        match! TemplateCache.get adminTheme "user-list-body" data with
        | Ok userTemplate ->
            match! TemplateCache.get adminTheme "tag-mapping-list-body" ctx.Data with
            | Ok tagMapTemplate ->
                let! allPages = data.Page.All ctx.WebLog.Id
                let! themes   = data.Theme.All()
                let! users    = data.WebLogUser.FindByWebLog ctx.WebLog.Id
                let! hash     =
                    hashForPage "Web Log Settings"
                    |> withAntiCsrf ctx
                    |> addToHash ViewContext.Model (SettingsModel.fromWebLog ctx.WebLog)
                    |> addToHash "pages" (
                        seq {
                            KeyValuePair.Create("posts", "- First Page of Posts -")
                            yield! allPages
                                   |> List.sortBy _.Title.ToLower()
                                   |> List.map (fun p -> KeyValuePair.Create(p.Id.Value, p.Title))
                        }
                        |> Array.ofSeq)
                    |> addToHash "themes" (
                        themes
                        |> Seq.ofList
                        |> Seq.map (fun it ->
                            KeyValuePair.Create (ThemeId.toString it.Id, $"{it.Name} (v{it.Version})"))
                        |> Array.ofSeq)
                    |> addToHash "upload_values" [|
                        KeyValuePair.Create (UploadDestination.toString Database, "Database")
                        KeyValuePair.Create (UploadDestination.toString Disk,     "Disk")
                    |]
                    |> addToHash "users" (users |> List.map (DisplayUser.fromUser ctx.WebLog) |> Array.ofList)
                    |> addToHash "rss_model" (EditRssModel.fromRssOptions ctx.WebLog.Rss)
                    |> addToHash "custom_feeds" (
                        ctx.WebLog.Rss.CustomFeeds
                        |> List.map (DisplayCustomFeed.fromFeed (CategoryCache.get ctx))
                        |> Array.ofList)
                    |> addViewContext ctx
                let! hash' = TagMapping.withTagMappings ctx hash
                return!
                       addToHash "user_list"        (userTemplate.Render   hash') hash'
                    |> addToHash "tag_mapping_list" (tagMapTemplate.Render hash')
                    |> adminView "settings" next ctx
            | Error message -> return! Error.server message next ctx
        | Error message -> return! Error.server message next ctx
    }

    // POST /admin/settings
    let saveSettings : HttpHandler = fun next ctx -> task {
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
