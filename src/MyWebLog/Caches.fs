namespace MyWebLog

open Microsoft.AspNetCore.Http

/// Helper functions for caches
module Cache =
    
    /// Create the cache key for the web log for the current request
    let makeKey (ctx : HttpContext) = (ctx.Items["webLog"] :?> WebLog).urlBase


open System.Collections.Concurrent
open Microsoft.Extensions.DependencyInjection
open RethinkDb.Driver.Net

/// <summary>
/// In-memory cache of web log details
/// </summary>
/// <remarks>This is filled by the middleware via the first request for each host, and can be updated via the web log
/// settings update page</remarks>
module WebLogCache =
    
    /// Create the full path of the request
    let private fullPath (ctx : HttpContext) =
        $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}{ctx.Request.Path.Value}"
    
    /// The cache of web log details
    let mutable private _cache : WebLog list = []

    /// Does a host exist in the cache?
    let exists ctx =
        let path = fullPath ctx
        _cache |> List.exists (fun wl -> path.StartsWith wl.urlBase)

    /// Get the web log for the current request
    let get ctx =
        let path = fullPath ctx
        _cache |> List.find (fun wl -> path.StartsWith wl.urlBase)

    /// Cache the web log for a particular host
    let set webLog =
        _cache <- webLog :: (_cache |> List.filter (fun wl -> wl.id <> webLog.id))
    
    /// Fill the web log cache from the database
    let fill conn = backgroundTask {
        let! webLogs = Data.WebLog.all conn
        _cache <- webLogs
    }


/// A cache of page information needed to display the page list in templates
module PageListCache =
    
    open MyWebLog.ViewModels
    
    /// Cache of displayed pages
    let private _cache = ConcurrentDictionary<string, DisplayPage[]> ()
    
    /// Are there pages cached for this web log?
    let exists ctx = _cache.ContainsKey (Cache.makeKey ctx)
    
    /// Get the pages for the web log for this request
    let get ctx = _cache[Cache.makeKey ctx]
    
    /// Update the pages for the current web log
    let update (ctx : HttpContext) = backgroundTask {
        let  webLog = ctx.Items["webLog"] :?> WebLog
        let  conn   = ctx.RequestServices.GetRequiredService<IConnection> ()
        let! pages  = Data.Page.findListed webLog.id conn
        _cache[Cache.makeKey ctx] <- pages |> List.map (DisplayPage.fromPage webLog) |> Array.ofList
    }


/// Cache of all categories, indexed by web log
module CategoryCache =
    
    open MyWebLog.ViewModels
    
    /// The cache itself
    let private _cache = ConcurrentDictionary<string, DisplayCategory[]> ()
    
    /// Are there categories cached for this web log?
    let exists ctx = _cache.ContainsKey (Cache.makeKey ctx)
    
    /// Get the categories for the web log for this request
    let get ctx = _cache[Cache.makeKey ctx]
    
    /// Update the cache with fresh data
    let update (ctx : HttpContext) = backgroundTask {
        let  webLog = ctx.Items["webLog"] :?> WebLog
        let  conn   = ctx.RequestServices.GetRequiredService<IConnection> ()
        let! cats   = Data.Category.findAllForView webLog.id conn
        _cache[Cache.makeKey ctx] <- cats
    }


/// Cache for parsed templates
module TemplateCache =
    
    open DotLiquid
    open System.IO
    
    /// Cache of parsed templates
    let private _cache = ConcurrentDictionary<string, Template> ()
    
    /// Get a template for the given theme and template nate
    let get (theme : string) (templateName : string) = backgroundTask {
        let templatePath = $"themes/{theme}/{templateName}"
        match _cache.ContainsKey templatePath with
        | true -> ()
        | false ->
            let! file = File.ReadAllTextAsync $"{templatePath}.liquid"
            _cache[templatePath] <- Template.Parse (file, SyntaxCompatibility.DotLiquid22)
        return _cache[templatePath]
    }

