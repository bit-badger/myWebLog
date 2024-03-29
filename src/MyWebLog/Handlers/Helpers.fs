[<AutoOpen>]
module private MyWebLog.Handlers.Helpers

open System.Text.Json
open Microsoft.AspNetCore.Http
open MyWebLog.Views

/// Session extensions to get and set objects
type ISession with
    
    /// Set an item in the session
    member this.Set<'T>(key, item: 'T) =
        this.SetString(key, JsonSerializer.Serialize item)
    
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
    
    /// The unified application view context
    [<Literal>]
    let AppViewContext = "app"
    
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
let private loadSession (ctx: HttpContext) = task {
    if not (ctx.Items.ContainsKey sessionLoadedKey) then
        do! ctx.Session.LoadAsync()
        ctx.Items.Add(sessionLoadedKey, "yes")
}

/// Ensure that the session is committed
let private commitSession (ctx: HttpContext) = task {
    if ctx.Items.ContainsKey sessionLoadedKey then do! ctx.Session.CommitAsync()
}

open MyWebLog.ViewModels

/// Add a message to the user's session
let addMessage (ctx: HttpContext) message = task {
    do! loadSession ctx
    let msg = match ctx.Session.TryGet<UserMessage list> ViewContext.Messages with Some it -> it | None -> []
    ctx.Session.Set(ViewContext.Messages, message :: msg)
}

/// Get any messages from the user's session, removing them in the process
let messages (ctx: HttpContext) = task {
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
let makeHash (values: obj) =
    Hash.FromAnonymousObject values

/// Create a hash with the page title filled
let hashForPage (title: string) =
    makeHash {| page_title = title |}

/// Add a key to the hash, returning the modified hash
//    (note that the hash itself is mutated; this is only used to make it pipeable)
let addToHash key (value: obj) (hash: Hash) =
    if hash.ContainsKey key then hash[key] <- value else hash.Add(key, value)
    hash

open System.Security.Claims
open Giraffe
open Giraffe.Htmx
open Giraffe.ViewEngine

/// htmx script tag
let private htmxScript = RenderView.AsString.htmlNode Htmx.Script.minified

/// Get the current user messages, and commit the session so that they are preserved
let private getCurrentMessages ctx = task {
    let! messages = messages ctx
    do! commitSession ctx
    return messages
}

/// Generate the view context for a response
let private generateViewContext pageTitle messages includeCsrf (ctx: HttpContext) =
    { WebLog          = ctx.WebLog
      UserId          = ctx.User.Claims
                        |> Seq.tryFind (fun claim -> claim.Type = ClaimTypes.NameIdentifier)
                        |> Option.map (fun claim -> WebLogUserId claim.Value)
      PageTitle       = pageTitle
      Csrf            = if includeCsrf then Some ctx.CsrfTokenSet else None
      PageList        = PageListCache.get ctx
      Categories      = CategoryCache.get ctx
      CurrentPage     = ctx.Request.Path.Value[1..]
      Messages        = messages
      Generator       = ctx.Generator
      HtmxScript      = htmxScript
      IsAuthor        = ctx.HasAccessLevel Author
      IsEditor        = ctx.HasAccessLevel Editor
      IsWebLogAdmin   = ctx.HasAccessLevel WebLogAdmin
      IsAdministrator = ctx.HasAccessLevel Administrator }


/// Populate the DotLiquid hash with standard information
let addViewContext ctx (hash: Hash) = task {
    let! messages = getCurrentMessages ctx
    if hash.ContainsKey ViewContext.AppViewContext then
        let oldApp = hash[ViewContext.AppViewContext] :?> AppViewContext
        let newApp = { oldApp with Messages = Array.concat [ oldApp.Messages; messages ] }
        return
            hash
            |> addToHash ViewContext.AppViewContext newApp
            |> addToHash ViewContext.Messages       newApp.Messages
    else
        let app =
            generateViewContext (string hash[ViewContext.PageTitle]) messages
                                (hash.ContainsKey ViewContext.AntiCsrfTokens) ctx
        return
            hash
            |> addToHash ViewContext.UserId          (app.UserId |> Option.map string |> Option.defaultValue "")
            |> addToHash ViewContext.WebLog          app.WebLog
            |> addToHash ViewContext.PageList        app.PageList
            |> addToHash ViewContext.Categories      app.Categories
            |> addToHash ViewContext.CurrentPage     app.CurrentPage
            |> addToHash ViewContext.Messages        app.Messages
            |> addToHash ViewContext.Generator       app.Generator
            |> addToHash ViewContext.HtmxScript      app.HtmxScript
            |> addToHash ViewContext.IsLoggedOn      app.IsLoggedOn
            |> addToHash ViewContext.IsAuthor        app.IsAuthor
            |> addToHash ViewContext.IsEditor        app.IsEditor
            |> addToHash ViewContext.IsWebLogAdmin   app.IsWebLogAdmin
            |> addToHash ViewContext.IsAdministrator app.IsAdministrator
}

/// Is the request from htmx?
let isHtmx (ctx: HttpContext) =
    ctx.Request.IsHtmx && not ctx.Request.IsHtmxRefresh

/// Convert messages to headers (used for htmx responses)
let messagesToHeaders (messages: UserMessage array) : HttpHandler =
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

/// Redirect after doing some action; commits session and issues a temporary redirect
let redirectToGet url : HttpHandler = fun _ ctx -> task {
    do! commitSession ctx
    return! redirectTo false (ctx.WebLog.RelativeUrl(Permalink url)) earlyReturn ctx
}

/// The MIME type for podcast episode JSON chapters
let JSON_CHAPTERS = "application/json+chapters"


/// Handlers for error conditions
module Error =

    open System.Net

    /// Handle unauthorized actions, redirecting to log on for GETs, otherwise returning a 401 Not Authorized response
    let notAuthorized : HttpHandler = fun next ctx ->
        if ctx.Request.Method = "GET" then
            let redirectUrl = $"user/log-on?returnUrl={WebUtility.UrlEncode ctx.Request.Path}"
            (next, ctx)
            ||> if isHtmx ctx then withHxRedirect redirectUrl >=> withHxRetarget "body" >=> redirectToGet redirectUrl
                else redirectToGet redirectUrl
        else
            if isHtmx ctx then
                let messages = [|
                    { UserMessage.Error with
                        Message = $"You are not authorized to access the URL {ctx.Request.Path.Value}" }
                |]
                (messagesToHeaders messages >=> setStatusCode 401) earlyReturn ctx
            else setStatusCode 401 earlyReturn ctx

    /// Handle 404s
    let notFound : HttpHandler =
        handleContext (fun ctx ->
            if isHtmx ctx then
                let messages = [|
                    { UserMessage.Error with Message = $"The URL {ctx.Request.Path.Value} was not found" }
                |]
                RequestErrors.notFound (messagesToHeaders messages) earlyReturn ctx
            else RequestErrors.NOT_FOUND "Not found" earlyReturn ctx)
    
    let server message : HttpHandler =
        handleContext (fun ctx ->
            if isHtmx ctx then
                let messages = [| { UserMessage.Error with Message = message } |]
                ServerErrors.internalError (messagesToHeaders messages) earlyReturn ctx
            else ServerErrors.INTERNAL_ERROR message earlyReturn ctx)


/// Render a view for the specified theme, using the specified template, layout, and hash
let viewForTheme themeId template next ctx (hash: Hash) = task {
    let! hash = addViewContext ctx hash
    
    // NOTE: DotLiquid does not support {% render %} or {% include %} in its templates, so we will do a 2-pass render;
    //       the net effect is a "layout" capability similar to Razor or Pug
    
    // Render view content...
    match! TemplateCache.get themeId template ctx.Data with
    | Ok contentTemplate ->
        let _ = addToHash ViewContext.Content (contentTemplate.Render hash) hash
        // ...then render that content with its layout
        match! TemplateCache.get themeId (if isHtmx ctx then "layout-partial" else "layout") ctx.Data with
        | Ok layoutTemplate ->  return! htmlString (layoutTemplate.Render hash) next ctx
        | Error message -> return! Error.server message next ctx
    | Error message -> return! Error.server message next ctx
}

/// Render a bare view for the specified theme, using the specified template and hash
let bareForTheme themeId template next ctx (hash: Hash) = task {
    let! hash        = addViewContext ctx hash
    let  withContent = task {
        if hash.ContainsKey ViewContext.Content then return Ok hash
        else
            match! TemplateCache.get themeId template ctx.Data with
            | Ok contentTemplate -> return Ok(addToHash ViewContext.Content (contentTemplate.Render hash) hash)
            | Error message -> return Error message 
    }
    match! withContent with
    | Ok completeHash ->
        // Bare templates are rendered with layout-bare
        match! TemplateCache.get themeId "layout-bare" ctx.Data with
        | Ok layoutTemplate ->
            return!
                (messagesToHeaders (hash[ViewContext.Messages] :?> UserMessage array)
                 >=> htmlString (layoutTemplate.Render completeHash))
                    next ctx
        | Error message -> return! Error.server message next ctx
    | Error message -> return! Error.server message next ctx
}

/// Return a view for the web log's default theme
let themedView template next ctx hash = task {
    let! hash = addViewContext ctx hash
    return! viewForTheme (hash[ViewContext.WebLog] :?> WebLog).ThemeId template next ctx hash
}

/// Display a page for an admin endpoint
let adminPage pageTitle includeCsrf next ctx (content: AppViewContext -> XmlNode list) = task {
    let! messages = getCurrentMessages ctx
    let  appCtx   = generateViewContext pageTitle messages includeCsrf ctx
    let  layout   = if isHtmx ctx then Layout.partial else Layout.full
    return! htmlString (layout content appCtx |> RenderView.AsString.htmlDocument) next ctx
}

/// Display a bare page for an admin endpoint
let adminBarePage pageTitle includeCsrf next ctx (content: AppViewContext -> XmlNode list) = task {
    let! messages = getCurrentMessages ctx
    let  appCtx   = generateViewContext pageTitle messages includeCsrf ctx
    return!
        (    messagesToHeaders appCtx.Messages
         >=> htmlString (Layout.bare content appCtx |> RenderView.AsString.htmlDocument)) next ctx
}

/// Validate the anti cross-site request forgery token in the current request
let validateCsrf : HttpHandler = fun next ctx -> task {
    match! ctx.AntiForgery.IsRequestValidAsync ctx with
    | true -> return! next ctx
    | false -> return! RequestErrors.BAD_REQUEST "CSRF token invalid" earlyReturn ctx
}

/// Require a user to be logged on
let requireUser : HttpHandler = requiresAuthentication Error.notAuthorized

/// Require a specific level of access for a route
let requireAccess level : HttpHandler = fun next ctx -> task {
    match ctx.UserAccessLevel with
    | Some userLevel when userLevel.HasAccess level -> return! next ctx
    | Some userLevel ->
        do! addMessage ctx
                { UserMessage.Warning with
                    Message = $"The page you tried to access requires {level} privileges"
                    Detail = Some $"Your account only has {userLevel} privileges" }
        return! Error.notAuthorized next ctx
    | None ->
        do! addMessage ctx
                { UserMessage.Warning with Message = "The page you tried to access required you to be logged on" }
        return! Error.notAuthorized next ctx
}

/// Determine if a user is authorized to edit a page or post, given the author        
let canEdit authorId (ctx: HttpContext) =
    ctx.UserId = authorId || ctx.HasAccessLevel Editor

open System.Threading.Tasks

/// Create a Task with a Some result for the given object
let someTask<'T> (it: 'T) = Task.FromResult(Some it)

/// Create an absolute URL from a string that may already be an absolute URL
let absoluteUrl (url: string) (ctx: HttpContext) =
    if url.StartsWith "http" then url else ctx.WebLog.AbsoluteUrl(Permalink url)


open MyWebLog.Data

/// Get the templates available for the current web log's theme (in a meta item list)
let templatesForTheme (ctx: HttpContext) (typ: string) = backgroundTask {
    match! ctx.Data.Theme.FindByIdWithoutText ctx.WebLog.ThemeId with
    | Some theme ->
        return seq {
            { Name = ""; Value = $"- Default (single-{typ}) -" }
            yield!
                theme.Templates
                |> Seq.ofList
                |> Seq.filter (fun it -> it.Name.EndsWith $"-{typ}" && it.Name <> $"single-{typ}")
                |> Seq.map (fun it -> { Name = it.Name; Value = it.Name })
        }
    | None -> return seq { { Name = ""; Value = $"- Default (single-{typ}) -" } }
}

/// Get all authors for a list of posts as metadata items
let getAuthors (webLog: WebLog) (posts: Post list) (data: IData) =
    posts
    |> List.map _.AuthorId
    |> List.distinct
    |> data.WebLogUser.FindNames webLog.Id

/// Get all tag mappings for a list of posts as metadata items
let getTagMappings (webLog: WebLog) (posts: Post list) (data: IData) =
    posts
    |> List.map _.Tags
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

open NodaTime

/// Parse a date/time to UTC 
let parseToUtc (date: string) : Instant =
    let result = roundTrip.Parse date
    if result.Success then result.Value else raise result.Exception

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// Log level for debugging
let mutable private debugEnabled : bool option = None

/// Is debug enabled for handlers?
let private isDebugEnabled (ctx: HttpContext) =
    match debugEnabled with
    | Some flag -> flag
    | None ->
        let fac = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
        let log = fac.CreateLogger "MyWebLog.Handlers"
        debugEnabled <- Some(log.IsEnabled LogLevel.Debug)
        debugEnabled.Value

/// Log a debug message
let debug (name: string) ctx msg =
    if isDebugEnabled ctx then
        let fac = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
        let log = fac.CreateLogger $"MyWebLog.Handlers.{name}"
        log.LogDebug(msg ())

/// Log a warning message
let warn (name: string) (ctx: HttpContext) msg =
    let fac = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
    let log = fac.CreateLogger $"MyWebLog.Handlers.{name}"
    log.LogWarning msg
