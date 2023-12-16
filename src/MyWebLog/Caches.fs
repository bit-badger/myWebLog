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
            |> Option.map (fun claim -> AccessLevel.Parse claim.Value)

        /// The user ID for the current request
        member this.UserId =
            WebLogUserId (this.User.Claims |> Seq.find (fun c -> c.Type = ClaimTypes.NameIdentifier)).Value

        /// The web log for the current request
        member this.WebLog = this.Items["webLog"] :?> WebLog
        
        /// Does the current user have the requested level of access?
        member this.HasAccessLevel level =
            defaultArg (this.UserAccessLevel |> Option.map _.HasAccess(level)) false


open System.Collections.Concurrent

/// <summary>
/// In-memory cache of web log details
/// </summary>
/// <remarks>This is filled by the middleware via the first request for each host, and can be updated via the web log
/// settings update page</remarks>
module WebLogCache =
    
    open System.Text.RegularExpressions

    /// A redirect rule that caches compiled regular expression rules
    type CachedRedirectRule =
    /// A straight text match rule
    | Text of string * string
    /// A regular expression match rule
    | RegEx of Regex * string

    /// The cache of web log details
    let mutable private _cache : WebLog list = []

    /// Redirect rules with compiled regular expressions
    let mutable private _redirectCache = ConcurrentDictionary<WebLogId, CachedRedirectRule list> ()

    /// Try to get the web log for the current request (longest matching URL base wins)
    let tryGet (path : string) =
        _cache
        |> List.filter (fun wl -> path.StartsWith wl.UrlBase)
        |> List.sortByDescending (fun wl -> wl.UrlBase.Length)
        |> List.tryHead

    /// Cache the web log for a particular host
    let set webLog =
        _cache <- webLog :: (_cache |> List.filter (fun wl -> wl.Id <> webLog.Id))
        _redirectCache[webLog.Id] <-
            webLog.RedirectRules
            |> List.map (fun it ->
                let relUrl = Permalink >> webLog.RelativeUrl
                let urlTo = if it.To.Contains "://" then it.To else relUrl it.To
                if it.IsRegex then
                    let pattern = if it.From.StartsWith "^" then $"^{relUrl it.From[1..]}" else it.From
                    RegEx(Regex(pattern, RegexOptions.Compiled ||| RegexOptions.IgnoreCase), urlTo)
                else
                    Text(relUrl it.From, urlTo))
    
    /// Get all cached web logs
    let all () =
        _cache
    
    /// Fill the web log cache from the database
    let fill (data : IData) = backgroundTask {
        let! webLogs = data.WebLog.All ()
        webLogs |> List.iter set
    }
    
    /// Get the cached redirect rules for the given web log
    let redirectRules webLogId =
        _redirectCache[webLogId]
    
    /// Is the given theme in use by any web logs?
    let isThemeInUse themeId =
        _cache |> List.exists (fun wl -> wl.ThemeId = themeId)


/// A cache of page information needed to display the page list in templates
module PageListCache =
    
    open MyWebLog.ViewModels
    
    /// Cache of displayed pages
    let private _cache = ConcurrentDictionary<WebLogId, DisplayPage[]> ()
    
    let private fillPages (webLog : WebLog) pages =
        _cache[webLog.Id] <-
            pages
            |> List.map (fun pg -> DisplayPage.FromPage webLog { pg with Text = "" })
            |> Array.ofList
    
    /// Are there pages cached for this web log?
    let exists (ctx : HttpContext) = _cache.ContainsKey ctx.WebLog.Id
    
    /// Get the pages for the web log for this request
    let get (ctx : HttpContext) = _cache[ctx.WebLog.Id]
    
    /// Update the pages for the current web log
    let update (ctx : HttpContext) = backgroundTask {
        let! pages = ctx.Data.Page.FindListed ctx.WebLog.Id
        fillPages ctx.WebLog pages
    }
    
    /// Refresh the pages for the given web log
    let refresh (webLog : WebLog) (data : IData) = backgroundTask {
        let! pages = data.Page.FindListed webLog.Id
        fillPages webLog pages
    }


/// Cache of all categories, indexed by web log
module CategoryCache =
    
    open MyWebLog.ViewModels
    
    /// The cache itself
    let private _cache = ConcurrentDictionary<WebLogId, DisplayCategory[]> ()
    
    /// Are there categories cached for this web log?
    let exists (ctx : HttpContext) = _cache.ContainsKey ctx.WebLog.Id
    
    /// Get the categories for the web log for this request
    let get (ctx : HttpContext) = _cache[ctx.WebLog.Id]
    
    /// Update the cache with fresh data
    let update (ctx : HttpContext) = backgroundTask {
        let! cats = ctx.Data.Category.FindAllForView ctx.WebLog.Id
        _cache[ctx.WebLog.Id] <- cats
    }
    
    /// Refresh the category cache for the given web log
    let refresh webLogId (data : IData) = backgroundTask {
        let! cats = data.Category.FindAllForView webLogId
        _cache[webLogId] <- cats
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
    let get (themeId: ThemeId) (templateName: string) (data: IData) = backgroundTask {
        let templatePath = $"{themeId}/{templateName}"
        match _cache.ContainsKey templatePath with
        | true -> return Ok _cache[templatePath]
        | false ->
            match! data.Theme.FindById themeId with
            | Some theme ->
                match theme.Templates |> List.tryFind (fun t -> t.Name = templateName) with
                | Some template ->
                    let mutable text = template.Text
                    let mutable childNotFound = ""
                    while hasInclude.IsMatch text do
                        let child = hasInclude.Match text
                        let childText =
                            match theme.Templates |> List.tryFind (fun t -> t.Name = child.Groups[1].Value) with
                            | Some childTemplate -> childTemplate.Text
                            | None ->
                                childNotFound <-
                                    if childNotFound = "" then child.Groups[1].Value
                                    else $"{childNotFound}; {child.Groups[1].Value}"
                                ""
                        text <- text.Replace(child.Value, childText)
                    if childNotFound <> "" then
                        let s = if childNotFound.IndexOf ";" >= 0 then "s" else ""
                        return Error $"Could not find the child template{s} {childNotFound} required by {templateName}"
                    else
                        _cache[templatePath] <- Template.Parse (text, SyntaxCompatibility.DotLiquid22)
                        return Ok _cache[templatePath]
                | None ->
                    return Error $"Theme ID {themeId} does not have a template named {templateName}"
            | None -> return Error $"Theme ID {themeId} does not exist"
    }
    
    /// Get all theme/template names currently cached
    let allNames () =
        _cache.Keys |> Seq.sort |> Seq.toList
    
    /// Invalidate all template cache entries for the given theme ID
    let invalidateTheme (themeId: ThemeId) =
        let keyPrefix = string themeId
        _cache.Keys
        |> Seq.filter _.StartsWith(keyPrefix)
        |> List.ofSeq
        |> List.iter (fun key -> match _cache.TryRemove key with _, _ -> ())
    
    /// Remove all entries from the template cache
    let empty () =
        _cache.Clear()


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
