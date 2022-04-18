namespace MyWebLog

open Microsoft.AspNetCore.Http

/// Helper functions for caches
module Cache =
    
    /// Create the cache key for the web log for the current request
    let makeKey (ctx : HttpContext) = ctx.Request.Host.ToUriComponent ()


open System.Collections.Concurrent

/// <summary>
/// In-memory cache of web log details
/// </summary>
/// <remarks>This is filled by the middleware via the first request for each host, and can be updated via the web log
/// settings update page</remarks>
module WebLogCache =
        
    /// The cache of web log details
    let private _cache = ConcurrentDictionary<string, WebLog> ()

    /// Does a host exist in the cache?
    let exists ctx = _cache.ContainsKey (Cache.makeKey ctx)

    /// Get the web log for the current request
    let get ctx = _cache[Cache.makeKey ctx]

    /// Cache the web log for a particular host
    let set ctx webLog = _cache[Cache.makeKey ctx] <- webLog


/// A cache of page information needed to display the page list in templates
module PageListCache =
    
    open Microsoft.Extensions.DependencyInjection
    open MyWebLog.ViewModels
    open RethinkDb.Driver.Net
    
    /// Cache of displayed pages
    let private _cache = ConcurrentDictionary<string, DisplayPage[]> ()
    
    /// Get the pages for the web log for this request
    let get ctx = _cache[Cache.makeKey ctx]
    
    /// Update the pages for the current web log
    let update ctx = task {
        let  webLog = WebLogCache.get ctx
        let  conn   = ctx.RequestServices.GetRequiredService<IConnection> ()
        let! pages  = Data.Page.findListed webLog.id conn
        _cache[Cache.makeKey ctx] <- pages |> List.map (DisplayPage.fromPage webLog) |> Array.ofList
    }

/// Cache for parsed templates
module TemplateCache =
    
    open DotLiquid
    open System.IO
    
    /// Cache of parsed templates
    let private _cache = ConcurrentDictionary<string, Template> ()
    
    /// Get a template for the given theme and template nate
    let get (theme : string) (templateName : string) = task {
        let templatePath = $"themes/{theme}/{templateName}"
        match _cache.ContainsKey templatePath with
        | true -> ()
        | false ->
            let! file = File.ReadAllTextAsync $"{templatePath}.liquid"
            _cache[templatePath] <- Template.Parse (file, SyntaxCompatibility.DotLiquid22)
        return _cache[templatePath]
    }

