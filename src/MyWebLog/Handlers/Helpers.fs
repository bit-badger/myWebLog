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

open MyWebLog
open DotLiquid

/// Add a key to the hash, returning the modified hash
//    (note that the hash itself is mutated; this is only used to make it pipeable)
let addToHash key (value : obj) (hash : Hash) =
    if hash.ContainsKey key then hash[key] <- value else hash.Add (key, value)
    hash

open System.Security.Claims
open Giraffe
open Giraffe.Htmx
open Giraffe.ViewEngine

/// htmx script tag
let private htmxScript = RenderView.AsString.htmlNode Htmx.Script.minified

/// Populate the DotLiquid hash with standard information
let private populateHash hash ctx = task {
    let! messages = messages ctx
    do! commitSession ctx
    
    let accessLevel = ctx.UserAccessLevel
    let hasLevel lvl = accessLevel |> Option.map (AccessLevel.hasAccess lvl) |> Option.defaultValue false
    
    ctx.User.Claims
    |> Seq.tryFind (fun claim -> claim.Type = ClaimTypes.NameIdentifier)
    |> Option.map (fun claim -> claim.Value)
    |> Option.iter (fun userId -> addToHash "user_id" userId hash |> ignore)
    
    return
        addToHash    "web_log"          ctx.WebLog hash
        |> addToHash "page_list"        (PageListCache.get ctx)
        |> addToHash "current_page"     ctx.Request.Path.Value[1..]
        |> addToHash "messages"         messages
        |> addToHash "generator"        ctx.Generator
        |> addToHash "htmx_script"      htmxScript
        |> addToHash "is_logged_on"     ctx.User.Identity.IsAuthenticated
        |> addToHash "is_author"        (hasLevel Author)
        |> addToHash "is_editor"        (hasLevel Editor)
        |> addToHash "is_web_log_admin" (hasLevel WebLogAdmin)
        |> addToHash "is_administrator" (hasLevel Administrator)
}

/// Is the request from htmx?
let isHtmx (ctx : HttpContext) =
    ctx.Request.IsHtmx && not ctx.Request.IsHtmxRefresh

/// Render a view for the specified theme, using the specified template, layout, and hash
let viewForTheme themeId template next ctx (hash : Hash) = task {
    if not (hash.ContainsKey "htmx_script") then
        let! _ = populateHash hash ctx
        ()
    let (ThemeId theme) = themeId
    // NOTE: DotLiquid does not support {% render %} or {% include %} in its templates, so we will do a 2-pass render;
    //       the net effect is a "layout" capability similar to Razor or Pug
    
    // Render view content...
    let! contentTemplate = TemplateCache.get theme template ctx.Data
    let _ = addToHash "content" (contentTemplate.Render hash) hash
    
    // ...then render that content with its layout
    let! layoutTemplate = TemplateCache.get theme (if isHtmx ctx then "layout-partial" else "layout") ctx.Data
    
    return! htmlString (layoutTemplate.Render hash) next ctx
}

/// Convert messages to headers (used for htmx responses)
let messagesToHeaders (messages : UserMessage array) : HttpHandler =
    seq {
        yield!
            messages
            |> Array.map (fun m ->
                match m.Detail with
                | Some detail -> $"{m.Level}|||{m.Message}|||{detail}"
                | None -> $"{m.Level}|||{m.Message}"
                |> setHttpHeader "X-Message")
        withHxNoPushUrl
    }
    |> Seq.reduce (>=>)

/// Render a bare view for the specified theme, using the specified template and hash
let bareForTheme themeId template next ctx (hash : Hash) = task {
    let! hash = populateHash hash ctx
    let (ThemeId theme) = themeId
    
    if not (hash.ContainsKey "content") then
        let! contentTemplate = TemplateCache.get theme template ctx.Data
        addToHash "content" (contentTemplate.Render hash) hash |> ignore
    
    // Bare templates are rendered with layout-bare
    let! layoutTemplate = TemplateCache.get theme "layout-bare" ctx.Data
    
    return!
        (messagesToHeaders (hash["messages"] :?> UserMessage[]) >=> htmlString (layoutTemplate.Render hash)) next ctx
}

/// Return a view for the web log's default theme
let themedView template next ctx hash = task {
    let! hash = populateHash hash ctx
    return! viewForTheme (hash["web_log"] :?> WebLog).ThemeId template next ctx hash
}

/// Display a view for the admin theme
let adminView template =
    viewForTheme (ThemeId "admin") template

/// Display a bare view for the admin theme
let adminBareView template =
    bareForTheme (ThemeId "admin") template

/// Redirect after doing some action; commits session and issues a temporary redirect
let redirectToGet url : HttpHandler = fun _ ctx -> task {
    do! commitSession ctx
    return! redirectTo false (WebLog.relativeUrl ctx.WebLog (Permalink url)) earlyReturn ctx
}

/// Validate the cross-site request forgery token in the current request
let validateCsrf : HttpHandler = fun next ctx -> task {
    match! ctx.AntiForgery.IsRequestValidAsync ctx with
    | true -> return! next ctx
    | false -> return! RequestErrors.BAD_REQUEST "CSRF token invalid" earlyReturn ctx
}


/// Handlers for error conditions
module Error =

    open System.Net

    /// Handle unauthorized actions, redirecting to log on for GETs, otherwise returning a 401 Not Authorized response
    let notAuthorized : HttpHandler = fun next ctx ->
        if ctx.Request.Method = "GET" then
            let redirectUrl = $"user/log-on?returnUrl={WebUtility.UrlEncode ctx.Request.Path}"
            if isHtmx ctx then (withHxRedirect redirectUrl >=> redirectToGet redirectUrl) next ctx
            else redirectToGet redirectUrl next ctx
        else
            if isHtmx ctx then
                let messages = [|
                    { UserMessage.error with
                        Message = $"You are not authorized to access the URL {ctx.Request.Path.Value}"
                    }
                |]
                (messagesToHeaders messages >=> setStatusCode 401) earlyReturn ctx
            else setStatusCode 401 earlyReturn ctx

    /// Handle 404s from the API, sending known URL paths to the Vue app so that they can be handled there
    let notFound : HttpHandler =
        handleContext (fun ctx ->
            if isHtmx ctx then
                let messages = [|
                    { UserMessage.error with Message = $"The URL {ctx.Request.Path.Value} was not found" }
                |]
                (messagesToHeaders messages >=> setStatusCode 404) earlyReturn ctx
            else
                (setStatusCode 404 >=> text "Not found") earlyReturn ctx)


/// Require a user to be logged on
let requireUser : HttpHandler = requiresAuthentication Error.notAuthorized

/// Require a specific level of access for a route
let requireAccess level : HttpHandler = fun next ctx -> task {
    let userLevel = ctx.UserAccessLevel
    if defaultArg (userLevel |> Option.map (AccessLevel.hasAccess level)) false then
        return! next ctx
    else
        let message =
            match userLevel with
            | Some lvl ->
                $"The page you tried to access requires {AccessLevel.toString level} privileges; your account only has {AccessLevel.toString lvl} privileges"
            | None -> "The page you tried to access required you to be logged on"
        do! addMessage ctx { UserMessage.warning with Message = message }
        printfn "Added message to context"
        do! commitSession ctx
        return! Error.notAuthorized next ctx
}

/// Determine if a user is authorized to edit a page or post, given the author        
let canEdit authorId (ctx : HttpContext) =
    if ctx.UserId = authorId then true
    else defaultArg (ctx.UserAccessLevel |> Option.map (AccessLevel.hasAccess Editor)) false

open System.Collections.Generic
open MyWebLog.Data

/// Get the templates available for the current web log's theme (in a key/value pair list)
let templatesForTheme (ctx : HttpContext) (typ : string) = backgroundTask {
    match! ctx.Data.Theme.FindByIdWithoutText ctx.WebLog.ThemeId with
    | Some theme ->
        return seq {
            KeyValuePair.Create ("", $"- Default (single-{typ}) -")
            yield!
                theme.Templates
                |> Seq.ofList
                |> Seq.filter (fun it -> it.Name.EndsWith $"-{typ}" && it.Name <> $"single-{typ}")
                |> Seq.map (fun it -> KeyValuePair.Create (it.Name, it.Name))
        }
        |> Array.ofSeq
    | None -> return [| KeyValuePair.Create ("", $"- Default (single-{typ}) -") |]
}

/// Get all authors for a list of posts as metadata items
let getAuthors (webLog : WebLog) (posts : Post list) (data : IData) =
    posts
    |> List.map (fun p -> p.AuthorId)
    |> List.distinct
    |> data.WebLogUser.FindNames webLog.Id

/// Get all tag mappings for a list of posts as metadata items
let getTagMappings (webLog : WebLog) (posts : Post list) (data : IData) =
    posts
    |> List.map (fun p -> p.Tags)
    |> List.concat
    |> List.distinct
    |> fun tags -> data.TagMap.FindMappingForTags tags webLog.Id

/// Get all category IDs for the given slug (includes owned subcategories)   
let getCategoryIds slug ctx =
    let allCats = CategoryCache.get ctx
    let cat     = allCats |> Array.find (fun cat -> cat.Slug = slug)
    // Category pages include posts in subcategories
    allCats
    |> Seq.ofArray
    |> Seq.filter (fun c -> c.Id = cat.Id || Array.contains cat.Name c.ParentNames)
    |> Seq.map (fun c -> CategoryId c.Id)
    |> List.ofSeq

open System
open System.Globalization

/// Parse a date/time to UTC 
let parseToUtc (date : string) =
    DateTime.Parse (date, null, DateTimeStyles.AdjustToUniversal)

open Microsoft.Extensions.DependencyInjection
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
    