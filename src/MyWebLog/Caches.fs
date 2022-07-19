namespace MyWebLog

open Microsoft.AspNetCore.Http
open MyWebLog.Data

/// Extension properties on HTTP context for web log
[<AutoOpen>]
module Extensions =
    
    open System.Security.Claims
    open Microsoft.AspNetCore.Antiforgery
    open Microsoft.Extensions.Configuration
    open Microsoft.Extensions.DependencyInjection
    
    /// Hold variable for the configured generator string
    let mutable private generatorString : string option = None
    
    type HttpContext with
        
        /// The anti-CSRF service
        member this.AntiForgery = this.RequestServices.GetRequiredService<IAntiforgery> ()
        
        /// The cross-site request forgery token set for this request
        member this.CsrfTokenSet = this.AntiForgery.GetAndStoreTokens this

        /// The data implementation
        member this.Data = this.RequestServices.GetRequiredService<IData> ()
        
        /// The generator string
        member this.Generator =
            match generatorString with
            | Some gen -> gen
            | None ->
                let cfg = this.RequestServices.GetRequiredService<IConfiguration> ()
                generatorString <-
                    match Option.ofObj cfg["Generator"] with
                    | Some gen -> Some gen
                    | None -> Some "generator not configured"
                generatorString.Value

        /// The access level for the current user
        member this.UserAccessLevel =
            this.User.Claims
            |> Seq.tryFind (fun claim -> claim.Type = ClaimTypes.Role)
            |> Option.map (fun claim -> AccessLevel.parse claim.Value)

        /// The user ID for the current request
        member this.UserId =
            WebLogUserId (this.User.Claims |> Seq.find (fun c -> c.Type = ClaimTypes.NameIdentifier)).Value

        /// The web log for the current request
        member this.WebLog = this.Items["webLog"] :?> WebLog


open System.Collections.Concurrent

/// <summary>
/// In-memory cache of web log details
/// </summary>
/// <remarks>This is filled by the middleware via the first request for each host, and can be updated via the web log
/// settings update page</remarks>
module WebLogCache =
    
    /// The cache of web log details
    let mutable private _cache : WebLog list = []

    /// Try to get the web log for the current request (longest matching URL base wins)
    let tryGet (path : string) =
        _cache
        |> List.filter (fun wl -> path.StartsWith wl.UrlBase)
        |> List.sortByDescending (fun wl -> wl.UrlBase.Length)
        |> List.tryHead

    /// Cache the web log for a particular host
    let set webLog =
        _cache <- webLog :: (_cache |> List.filter (fun wl -> wl.Id <> webLog.Id))
    
    /// Fill the web log cache from the database
    let fill (data : IData) = backgroundTask {
        let! webLogs = data.WebLog.All ()
        _cache <- webLogs
    }


/// A cache of page information needed to display the page list in templates
module PageListCache =
    
    open MyWebLog.ViewModels
    
    /// Cache of displayed pages
    let private _cache = ConcurrentDictionary<string, DisplayPage[]> ()
    
    /// Are there pages cached for this web log?
    let exists (ctx : HttpContext) = _cache.ContainsKey ctx.WebLog.UrlBase
    
    /// Get the pages for the web log for this request
    let get (ctx : HttpContext) = _cache[ctx.WebLog.UrlBase]
    
    /// Update the pages for the current web log
    let update (ctx : HttpContext) = backgroundTask {
        let  webLog = ctx.WebLog
        let! pages  = ctx.Data.Page.FindListed webLog.Id
        _cache[webLog.UrlBase] <-
            pages
            |> List.map (fun pg -> DisplayPage.fromPage webLog { pg with Text = "" })
            |> Array.ofList
    }


/// Cache of all categories, indexed by web log
module CategoryCache =
    
    open MyWebLog.ViewModels
    
    /// The cache itself
    let private _cache = ConcurrentDictionary<string, DisplayCategory[]> ()
    
    /// Are there categories cached for this web log?
    let exists (ctx : HttpContext) = _cache.ContainsKey ctx.WebLog.UrlBase
    
    /// Get the categories for the web log for this request
    let get (ctx : HttpContext) = _cache[ctx.WebLog.UrlBase]
    
    /// Update the cache with fresh data
    let update (ctx : HttpContext) = backgroundTask {
        let! cats = ctx.Data.Category.FindAllForView ctx.WebLog.Id
        _cache[ctx.WebLog.UrlBase] <- cats
    }


/// Cache for parsed templates
module TemplateCache =
    
    open System
    open System.Text.RegularExpressions
    open DotLiquid
    
    /// Cache of parsed templates
    let private _cache = ConcurrentDictionary<string, Template> ()
    
    /// Custom include parameter pattern
    let private hasInclude = Regex ("""{% include_template \"(.*)\" %}""", RegexOptions.None, TimeSpan.FromSeconds 2)
    
    /// Get a template for the given theme and template name
    let get (themeId : string) (templateName : string) (data : IData) = backgroundTask {
        let templatePath = $"{themeId}/{templateName}"
        match _cache.ContainsKey templatePath with
        | true -> ()
        | false ->
            match! data.Theme.FindById (ThemeId themeId) with
            | Some theme ->
                let mutable text = (theme.Templates |> List.find (fun t -> t.Name = templateName)).Text
                while hasInclude.IsMatch text do
                    let child = hasInclude.Match text
                    let childText  = (theme.Templates |> List.find (fun t -> t.Name = child.Groups[1].Value)).Text
                    text <- text.Replace (child.Value, childText)
                _cache[templatePath] <- Template.Parse (text, SyntaxCompatibility.DotLiquid22)
            | None -> ()
        return _cache[templatePath]
    }
    
    /// Invalidate all template cache entries for the given theme ID
    let invalidateTheme (themeId : string) =
        _cache.Keys
        |> Seq.filter (fun key -> key.StartsWith themeId)
        |> List.ofSeq
        |> List.iter (fun key -> match _cache.TryRemove key with _, _ -> ())


/// A cache of asset names by themes
module ThemeAssetCache =
    
    /// A list of asset names for each theme
    let private _cache = ConcurrentDictionary<ThemeId, string list> ()
    
    /// Retrieve the assets for the given theme ID
    let get themeId = _cache[themeId]
    
    /// Refresh the list of assets for the given theme
    let refreshTheme themeId (data : IData) = backgroundTask {
        let! assets = data.ThemeAsset.FindByTheme themeId
        _cache[themeId] <- assets |> List.map (fun a -> match a.Id with ThemeAssetId (_, path) -> path)
    }
    
    /// Fill the theme asset cache
    let fill (data : IData) = backgroundTask {
        let! assets = data.ThemeAsset.All ()
        for asset in assets do
            let (ThemeAssetId (themeId, path)) = asset.Id
            if not (_cache.ContainsKey themeId) then _cache[themeId] <- []
            _cache[themeId] <- path :: _cache[themeId]
    }
