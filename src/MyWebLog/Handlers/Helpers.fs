[<AutoOpen>]
module private MyWebLog.Handlers.Helpers

open System.Text.Json
open Microsoft.AspNetCore.Http

/// Session extensions to get and set objects
type ISession with
    
    /// Set an item in the session
    member this.Set<'T> (key, item : 'T) =
        this.SetString (key, JsonSerializer.Serialize item)
    
    /// Get an item from the session
    member this.Get<'T> key =
        match this.GetString key with
        | null -> None
        | item -> Some (JsonSerializer.Deserialize<'T> item)

    
/// The HTTP item key for loading the session
let private sessionLoadedKey = "session-loaded"

/// Load the session if it has not been loaded already; ensures async access but not excessive loading
let private loadSession (ctx : HttpContext) = task {
    if not (ctx.Items.ContainsKey sessionLoadedKey) then
        do! ctx.Session.LoadAsync ()
        ctx.Items.Add (sessionLoadedKey, "yes")
}

/// Ensure that the session is committed
let private commitSession (ctx : HttpContext) = task {
    if ctx.Items.ContainsKey sessionLoadedKey then do! ctx.Session.CommitAsync ()
}

open MyWebLog.ViewModels

/// Add a message to the user's session
let addMessage (ctx : HttpContext) message = task {
    do! loadSession ctx
    let msg = match ctx.Session.Get<UserMessage list> "messages" with Some it -> it | None -> []
    ctx.Session.Set ("messages", message :: msg)
}

/// Get any messages from the user's session, removing them in the process
let messages (ctx : HttpContext) = task {
    do! loadSession ctx
    match ctx.Session.Get<UserMessage list> "messages" with
    | Some msg ->
        ctx.Session.Remove "messages"
        return msg |> (List.rev >> Array.ofList)
    | None -> return [||]
}

/// Hold variable for the configured generator string
let mutable private generatorString : string option = None

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection

/// Get the generator string
let generator (ctx : HttpContext) =
    match generatorString with
    | Some gen -> gen
    | None ->
        let cfg = ctx.RequestServices.GetRequiredService<IConfiguration> ()
        generatorString <-
            match Option.ofObj cfg["Generator"] with
            | Some gen -> Some gen
            | None -> Some "generator not configured"
        generatorString.Value

open MyWebLog
open DotLiquid

/// Either get the web log from the hash, or get it from the cache and add it to the hash
let private deriveWebLogFromHash (hash : Hash) (ctx : HttpContext) =
    if hash.ContainsKey "web_log" then () else hash.Add ("web_log", ctx.WebLog)
    hash["web_log"] :?> WebLog

open Giraffe
open Giraffe.Htmx
open Giraffe.ViewEngine

/// htmx script tag
let private htmxScript = RenderView.AsString.htmlNode Htmx.Script.minified

/// Populate the DotLiquid hash with standard information
let private populateHash hash ctx = task {
    // Don't need the web log, but this adds it to the hash if the function is called directly
    let _ = deriveWebLogFromHash hash ctx
    let! messages = messages ctx
    hash.Add ("logged_on",    ctx.User.Identity.IsAuthenticated)
    hash.Add ("page_list",    PageListCache.get ctx)
    hash.Add ("current_page", ctx.Request.Path.Value.Substring 1)
    hash.Add ("messages",     messages)
    hash.Add ("generator",    generator ctx)
    hash.Add ("htmx_script",  htmxScript)
    
    do! commitSession ctx
}

/// Render a view for the specified theme, using the specified template, layout, and hash
let viewForTheme theme template next ctx = fun (hash : Hash) -> task {
    do! populateHash hash ctx
    
    // NOTE: DotLiquid does not support {% render %} or {% include %} in its templates, so we will do a 2-pass render;
    //       the net effect is a "layout" capability similar to Razor or Pug
    
    // Render view content...
    let! contentTemplate = TemplateCache.get theme template ctx.Conn
    hash.Add ("content", contentTemplate.Render hash)
    
    // ...then render that content with its layout
    let  isHtmx         = ctx.Request.IsHtmx && not ctx.Request.IsHtmxRefresh
    let! layoutTemplate = TemplateCache.get theme (if isHtmx then "layout-partial" else "layout") ctx.Conn
    
    return! htmlString (layoutTemplate.Render hash) next ctx
}

/// Render a bare view for the specified theme, using the specified template and hash
let bareForTheme theme template next ctx = fun (hash : Hash) -> task {
    do! populateHash hash ctx
    
    // Bare templates are rendered with layout-bare
    let! contentTemplate = TemplateCache.get theme template ctx.Conn
    hash.Add ("content", contentTemplate.Render hash)
    
    let! layoutTemplate = TemplateCache.get theme "layout-bare" ctx.Conn
    
    // add messages as HTTP headers
    let messages = hash["messages"] :?> UserMessage[]
    let actions = seq {
        yield!
            messages
            |> Array.map (fun m ->
                match m.detail with
                | Some detail -> $"{m.level}|||{m.message}|||{detail}"
                | None -> $"{m.level}|||{m.message}"
                |> setHttpHeader "X-Message")
        withHxNoPush
        htmlString (layoutTemplate.Render hash)
        }
    
    return! (actions |> Seq.reduce (>=>)) next ctx
}

/// Return a view for the web log's default theme
let themedView template next ctx = fun (hash : Hash) -> task {
    return! viewForTheme (deriveWebLogFromHash hash ctx).themePath template next ctx hash
}

/// Redirect after doing some action; commits session and issues a temporary redirect
let redirectToGet url : HttpHandler = fun next ctx -> task {
    do! commitSession ctx
    return! redirectTo false url next ctx
}

open System.Security.Claims

/// Get the user ID for the current request
let userId (ctx : HttpContext) =
    WebLogUserId (ctx.User.Claims |> Seq.find (fun c -> c.Type = ClaimTypes.NameIdentifier)).Value

open Microsoft.AspNetCore.Antiforgery

/// Get the Anti-CSRF service
let private antiForgery (ctx : HttpContext) = ctx.RequestServices.GetRequiredService<IAntiforgery> ()

/// Get the cross-site request forgery token set
let csrfToken (ctx : HttpContext) =
    (antiForgery ctx).GetAndStoreTokens ctx

/// Validate the cross-site request forgery token in the current request
let validateCsrf : HttpHandler = fun next ctx -> task {
    match! (antiForgery ctx).IsRequestValidAsync ctx with
    | true -> return! next ctx
    | false -> return! RequestErrors.BAD_REQUEST "CSRF token invalid" next ctx
}

/// Require a user to be logged on
let requireUser : HttpHandler = requiresAuthentication Error.notAuthorized

open System.Collections.Generic
open System.IO

/// Get the templates available for the current web log's theme (in a key/value pair list)
let templatesForTheme (ctx : HttpContext) (typ : string) =
    seq {
        KeyValuePair.Create ("", $"- Default (single-{typ}) -")
        yield!
            Path.Combine ("themes", ctx.WebLog.themePath)
            |> Directory.EnumerateFiles
            |> Seq.filter (fun it -> it.EndsWith $"{typ}.liquid")
            |> Seq.map (fun it ->
                let parts    = it.Split Path.DirectorySeparatorChar
                let template = parts[parts.Length - 1].Replace (".liquid", "")
                KeyValuePair.Create (template, template))
    }
    |> Array.ofSeq

/// Get all authors for a list of posts as metadata items
let getAuthors (webLog : WebLog) (posts : Post list) conn =
    posts
    |> List.map (fun p -> p.authorId)
    |> List.distinct
    |> Data.WebLogUser.findNames webLog.id conn

/// Get all tag mappings for a list of posts as metadata items
let getTagMappings (webLog : WebLog) (posts : Post list) =
    posts
    |> List.map (fun p -> p.tags)
    |> List.concat
    |> List.distinct
    |> fun tags -> Data.TagMap.findMappingForTags tags webLog.id

/// Get all category IDs for the given slug (includes owned subcategories)   
let getCategoryIds slug ctx =
    let allCats = CategoryCache.get ctx
    let cat     = allCats |> Array.find (fun cat -> cat.slug = slug)
    // Category pages include posts in subcategories
    allCats
    |> Seq.ofArray
    |> Seq.filter (fun c -> c.id = cat.id || Array.contains cat.name c.parentNames)
    |> Seq.map (fun c -> CategoryId c.id)
    |> List.ofSeq

open Microsoft.Extensions.Logging

/// Log level for debugging
let mutable private debugEnabled : bool option = None

/// Is debug enabled for handlers?
let private isDebugEnabled (ctx : HttpContext) =
    match debugEnabled with
    | Some flag -> flag
    | None ->
        let fac = ctx.RequestServices.GetRequiredService<ILoggerFactory> ()
        let log = fac.CreateLogger "MyWebLog.Handlers"
        debugEnabled <- Some (log.IsEnabled LogLevel.Debug)
        debugEnabled.Value

/// Log a debug message
let debug (name : string) ctx msg =
    if isDebugEnabled ctx then
        let fac = ctx.RequestServices.GetRequiredService<ILoggerFactory> ()
        let log = fac.CreateLogger $"MyWebLog.Handlers.{name}"
        log.LogDebug (msg ())

/// Log a warning message
let warn (name : string) (ctx : HttpContext) msg =
    let fac = ctx.RequestServices.GetRequiredService<ILoggerFactory> ()
    let log = fac.CreateLogger $"MyWebLog.Handlers.{name}"
    log.LogWarning msg
    