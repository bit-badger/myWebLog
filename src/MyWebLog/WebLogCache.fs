/// <summary>
/// In-memory cache of web log details
/// </summary>
/// <remarks>This is filled by the middleware via the first request for each host, and can be updated via the web log
/// settings update page</remarks>
module MyWebLog.WebLogCache

open Microsoft.AspNetCore.Http
open System.Collections.Concurrent
    
/// The cache of web log details
let private _cache = ConcurrentDictionary<string, WebLog> ()

/// Does a host exist in the cache?
let exists host = _cache.ContainsKey host

/// Get the details for a web log via its host
let get host = _cache[host]

/// Get the details for a web log via its host
let getByCtx (ctx : HttpContext) = _cache[ctx.Request.Host.ToUriComponent ()]

/// Set the details for a particular host
let set host details = _cache[host] <- details
