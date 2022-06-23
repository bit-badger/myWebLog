﻿namespace MyWebLog

open Microsoft.AspNetCore.Http
open MyWebLog.Data

/// Extension properties on HTTP context for web log
[<AutoOpen>]
module Extensions =
    
    open Microsoft.Extensions.DependencyInjection
    
    type HttpContext with
        /// The web log for the current request
        member this.WebLog = this.Items["webLog"] :?> WebLog
        
        /// The data implementation
        member this.Data = this.RequestServices.GetRequiredService<IData> ()

        
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
        |> List.filter (fun wl -> path.StartsWith wl.urlBase)
        |> List.sortByDescending (fun wl -> wl.urlBase.Length)
        |> List.tryHead

    /// Cache the web log for a particular host
    let set webLog =
        _cache <- webLog :: (_cache |> List.filter (fun wl -> wl.id <> webLog.id))
    
    /// Fill the web log cache from the database
    let fill (data : IData) = backgroundTask {
        let! webLogs = data.WebLog.all ()
        _cache <- webLogs
    }


/// A cache of page information needed to display the page list in templates
module PageListCache =
    
    open MyWebLog.ViewModels
    
    /// Cache of displayed pages
    let private _cache = ConcurrentDictionary<string, DisplayPage[]> ()
    
    /// Are there pages cached for this web log?
    let exists (ctx : HttpContext) = _cache.ContainsKey ctx.WebLog.urlBase
    
    /// Get the pages for the web log for this request
    let get (ctx : HttpContext) = _cache[ctx.WebLog.urlBase]
    
    /// Update the pages for the current web log
    let update (ctx : HttpContext) = backgroundTask {
        let  webLog = ctx.WebLog
        let! pages  = ctx.Data.Page.findListed webLog.id
        _cache[webLog.urlBase] <-
            pages
            |> List.map (fun pg -> DisplayPage.fromPage webLog { pg with text = "" })
            |> Array.ofList
    }


/// Cache of all categories, indexed by web log
module CategoryCache =
    
    open MyWebLog.ViewModels
    
    /// The cache itself
    let private _cache = ConcurrentDictionary<string, DisplayCategory[]> ()
    
    /// Are there categories cached for this web log?
    let exists (ctx : HttpContext) = _cache.ContainsKey ctx.WebLog.urlBase
    
    /// Get the categories for the web log for this request
    let get (ctx : HttpContext) = _cache[ctx.WebLog.urlBase]
    
    /// Update the cache with fresh data
    let update (ctx : HttpContext) = backgroundTask {
        let! cats = ctx.Data.Category.findAllForView ctx.WebLog.id
        _cache[ctx.WebLog.urlBase] <- cats
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
            match! data.Theme.findById (ThemeId themeId) with
            | Some theme ->
                let mutable text = (theme.templates |> List.find (fun t -> t.name = templateName)).text
                while hasInclude.IsMatch text do
                    let child = hasInclude.Match text
                    let childText  = (theme.templates |> List.find (fun t -> t.name = child.Groups[1].Value)).text
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
        let! assets = data.ThemeAsset.findByTheme themeId
        _cache[themeId] <- assets |> List.map (fun a -> match a.id with ThemeAssetId (_, path) -> path)
    }
    
    /// Fill the theme asset cache
    let fill (data : IData) = backgroundTask {
        let! assets = data.ThemeAsset.all ()
        for asset in assets do
            let (ThemeAssetId (themeId, path)) = asset.id
            if not (_cache.ContainsKey themeId) then _cache[themeId] <- []
            _cache[themeId] <- path :: _cache[themeId]
    }
