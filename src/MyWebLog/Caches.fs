namespace MyWebLog

open Microsoft.AspNetCore.Http

/// Extension properties on HTTP context for web log
[<AutoOpen>]
module Extensions =
    
    open Microsoft.Extensions.DependencyInjection
    open RethinkDb.Driver.Net
    
    type HttpContext with
        /// The web log for the current request
        member this.WebLog = this.Items["webLog"] :?> WebLog
        
        /// The RethinkDB data connection
        member this.Conn = this.RequestServices.GetRequiredService<IConnection> ()

        
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
    let exists (ctx : HttpContext) = _cache.ContainsKey ctx.WebLog.urlBase
    
    /// Get the pages for the web log for this request
    let get (ctx : HttpContext) = _cache[ctx.WebLog.urlBase]
    
    /// Update the pages for the current web log
    let update (ctx : HttpContext) = backgroundTask {
        let  webLog = ctx.WebLog
        let! pages  = Data.Page.findListed webLog.id ctx.Conn
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
        let! cats = Data.Category.findAllForView ctx.WebLog.id ctx.Conn
        _cache[ctx.WebLog.urlBase] <- cats
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

