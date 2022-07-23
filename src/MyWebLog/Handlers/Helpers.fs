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
    member this.TryGet<'T> key =
        match this.GetString key with
        | null -> None
        | item -> Some (JsonSerializer.Deserialize<'T> item)


/// Keys used in the myWebLog-standard DotLiquid hash
module ViewContext =
    
    /// The anti cross-site request forgery (CSRF) token set to use for form submissions
    [<Literal>]
    let AntiCsrfTokens = "csrf"
    
    /// The categories for this web log
    [<Literal>]
    let Categories = "categories"
    
    /// The main content of the view
    [<Literal>]
    let Content = "content"
    
    /// The current page URL
    [<Literal>]
    let CurrentPage = "current_page"
    
    /// The generator string for the current version of myWebLog
    [<Literal>]
    let Generator = "generator"
    
    /// The HTML to load htmx from the unpkg CDN
    [<Literal>]
    let HtmxScript = "htmx_script"
    
    /// Whether the current user has Administrator privileges
    [<Literal>]
    let IsAdministrator = "is_administrator"
    
    /// Whether the current user has Author (or above) privileges
    [<Literal>]
    let IsAuthor = "is_author"
    
    /// Whether the current view is displaying a category archive page
    [<Literal>]
    let IsCategory = "is_category"
    
    /// Whether the current view is displaying the first page of a category archive
    [<Literal>]
    let IsCategoryHome = "is_category_home"
    
    /// Whether the current user has Editor (or above) privileges
    [<Literal>]
    let IsEditor = "is_editor"
    
    /// Whether the current view is the home page for the web log
    [<Literal>]
    let IsHome = "is_home"
    
    /// Whether there is a user logged on
    [<Literal>]
    let IsLoggedOn = "is_logged_on"
    
    /// Whether the current view is displaying a page
    [<Literal>]
    let IsPage = "is_page"
    
    /// Whether the current view is displaying a post
    [<Literal>]
    let IsPost = "is_post"
    
    /// Whether the current view is a tag archive page
    [<Literal>]
    let IsTag = "is_tag"
    
    /// Whether the current view is the first page of a tag archive
    [<Literal>]
    let IsTagHome = "is_tag_home"
    
    /// Whether the current user has Web Log Admin (or above) privileges
    [<Literal>]
    let IsWebLogAdmin = "is_web_log_admin"
    
    /// Messages to be displayed to the user
    [<Literal>]
    let Messages = "messages"
    
    /// The view model / form for the page
    [<Literal>]
    let Model = "model"
    
    /// The listed pages for the web log
    [<Literal>]
    let PageList = "page_list"
    
    /// The title of the page being displayed
    [<Literal>]
    let PageTitle = "page_title"
    
    /// The slug for category or tag archive pages
    [<Literal>]
    let Slug = "slug"
    
    /// The ID of the current user
    [<Literal>]
    let UserId = "user_id"
    
    /// The current web log
    [<Literal>]
    let WebLog = "web_log"
    


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
    let msg = match ctx.Session.TryGet<UserMessage list> ViewContext.Messages with Some it -> it | None -> []
    ctx.Session.Set (ViewContext.Messages, message :: msg)
}

/// Get any messages from the user's session, removing them in the process
let messages (ctx : HttpContext) = task {
    do! loadSession ctx
    match ctx.Session.TryGet<UserMessage list> ViewContext.Messages with
    | Some msg ->
        ctx.Session.Remove ViewContext.Messages
        return msg |> (List.rev >> Array.ofList)
    | None -> return [||]
}

open MyWebLog
open DotLiquid

/// Shorthand for creating a DotLiquid hash from an anonymous object
let makeHash (values : obj) =
    Hash.FromAnonymousObject values

/// Create a hash with the page title filled
let hashForPage (title : string) =
    makeHash {| page_title = title |}

/// Add a key to the hash, returning the modified hash
//    (note that the hash itself is mutated; this is only used to make it pipeable)
let addToHash key (value : obj) (hash : Hash) =
    if hash.ContainsKey key then hash[key] <- value else hash.Add (key, value)
    hash

/// Add anti-CSRF tokens to the given hash
let withAntiCsrf (ctx : HttpContext) =
    addToHash ViewContext.AntiCsrfTokens ctx.CsrfTokenSet 

open System.Security.Claims
open Giraffe
open Giraffe.Htmx
open Giraffe.ViewEngine

/// htmx script tag
let private htmxScript = RenderView.AsString.htmlNode Htmx.Script.minified

/// Populate the DotLiquid hash with standard information
let addViewContext ctx (hash : Hash) = task {
    let! messages = messages ctx
    do! commitSession ctx
    return
        if hash.ContainsKey ViewContext.HtmxScript && hash.ContainsKey ViewContext.Messages then
            // We have already populated everything; just update messages
            hash[ViewContext.Messages] <- Array.concat [ hash[ViewContext.Messages] :?> UserMessage[]; messages ]
            hash
        else
            ctx.User.Claims
            |> Seq.tryFind (fun claim -> claim.Type = ClaimTypes.NameIdentifier)
            |> Option.map (fun claim -> addToHash ViewContext.UserId claim.Value hash)
            |> Option.defaultValue hash
            |> addToHash ViewContext.WebLog          ctx.WebLog
            |> addToHash ViewContext.PageList        (PageListCache.get ctx)
            |> addToHash ViewContext.Categories      (CategoryCache.get ctx)
            |> addToHash ViewContext.CurrentPage     ctx.Request.Path.Value[1..]
            |> addToHash ViewContext.Messages        messages
            |> addToHash ViewContext.Generator       ctx.Generator
            |> addToHash ViewContext.HtmxScript      htmxScript
            |> addToHash ViewContext.IsLoggedOn      ctx.User.Identity.IsAuthenticated
            |> addToHash ViewContext.IsAuthor        (ctx.HasAccessLevel Author)
            |> addToHash ViewContext.IsEditor        (ctx.HasAccessLevel Editor)
            |> addToHash ViewContext.IsWebLogAdmin   (ctx.HasAccessLevel WebLogAdmin)
            |> addToHash ViewContext.IsAdministrator (ctx.HasAccessLevel Administrator)
}

/// Is the request from htmx?
let isHtmx (ctx : HttpContext) =
    ctx.Request.IsHtmx && not ctx.Request.IsHtmxRefresh

/// Render a view for the specified theme, using the specified template, layout, and hash
let viewForTheme themeId template next ctx (hash : Hash) = task {
    let! hash = addViewContext ctx hash
    
    // NOTE: DotLiquid does not support {% render %} or {% include %} in its templates, so we will do a 2-pass render;
    //       the net effect is a "layout" capability similar to Razor or Pug
    
    // Render view content...
    let! contentTemplate = TemplateCache.get themeId template ctx.Data
    let _ = addToHash ViewContext.Content (contentTemplate.Render hash) hash
    
    // ...then render that content with its layout
    let! layoutTemplate = TemplateCache.get themeId (if isHtmx ctx then "layout-partial" else "layout") ctx.Data
    
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
    let! hash = addViewContext ctx hash
    
    if not (hash.ContainsKey ViewContext.Content) then
        let! contentTemplate = TemplateCache.get themeId template ctx.Data
        addToHash ViewContext.Content (contentTemplate.Render hash) hash |> ignore
    
    // Bare templates are rendered with layout-bare
    let! layoutTemplate = TemplateCache.get themeId "layout-bare" ctx.Data
    return!
        (messagesToHeaders (hash[ViewContext.Messages] :?> UserMessage[])
         >=> htmlString (layoutTemplate.Render hash))
            next ctx
}

/// Return a view for the web log's default theme
let themedView template next ctx hash = task {
    let! hash = addViewContext ctx hash
    return! viewForTheme (hash[ViewContext.WebLog] :?> WebLog).ThemeId template next ctx hash
}

/// The ID for the admin theme
let adminTheme = ThemeId "admin"

/// Display a view for the admin theme
let adminView template =
    viewForTheme adminTheme template

/// Display a bare view for the admin theme
let adminBareView template =
    bareForTheme adminTheme template

/// Redirect after doing some action; commits session and issues a temporary redirect
let redirectToGet url : HttpHandler = fun _ ctx -> task {
    do! commitSession ctx
    return! redirectTo false (WebLog.relativeUrl ctx.WebLog (Permalink url)) earlyReturn ctx
}

/// Validate the anti cross-site request forgery token in the current request
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
    match ctx.UserAccessLevel with
    | Some userLevel when AccessLevel.hasAccess level userLevel -> return! next ctx
    | Some userLevel ->
        do! addMessage ctx
                { UserMessage.warning with
                    Message = $"The page you tried to access requires {AccessLevel.toString level} privileges"
                    Detail = Some $"Your account only has {AccessLevel.toString userLevel} privileges"
                }
        return! Error.notAuthorized next ctx
    | None ->
        do! addMessage ctx
                { UserMessage.warning with Message = "The page you tried to access required you to be logged on" }
        return! Error.notAuthorized next ctx
}

/// Determine if a user is authorized to edit a page or post, given the author        
let canEdit authorId (ctx : HttpContext) =
    ctx.UserId = authorId || ctx.HasAccessLevel Editor

open System.Threading.Tasks

/// Create a Task with a Some result for the given object
let someTask<'T> (it : 'T) = Task.FromResult (Some it)

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
    