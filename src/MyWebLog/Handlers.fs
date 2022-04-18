[<RequireQualifiedAccess>]
module MyWebLog.Handlers

open System.Collections.Generic
open DotLiquid
open Giraffe
open Microsoft.AspNetCore.Http
open MyWebLog
open MyWebLog.ViewModels
open RethinkDb.Driver.Net
open System
open System.Net
open System.Threading.Tasks

/// Handlers for error conditions
module Error =

    (* open Microsoft.Extensions.Logging *)

    (*/// Handle errors
    let error (ex : Exception) (log : ILogger) =
        log.LogError (EventId(), ex, "An unhandled exception has occurred while executing the request.")
        clearResponse
        >=> setStatusCode 500
        >=> setHttpHeader "X-Toast" (sprintf "error|||%s: %s" (ex.GetType().Name) ex.Message)
        >=> text ex.Message *)

    /// Handle unauthorized actions, redirecting to log on for GETs, otherwise returning a 401 Not Authorized response
    let notAuthorized : HttpHandler =
        fun next ctx ->
            (next, ctx)
            ||> match ctx.Request.Method with
                | "GET" -> redirectTo false $"/user/log-on?returnUrl={WebUtility.UrlEncode ctx.Request.Path}"
                | _ -> setStatusCode 401 >=> fun _ _ -> Task.FromResult<HttpContext option> None

    /// Handle 404s from the API, sending known URL paths to the Vue app so that they can be handled there
    let notFound : HttpHandler =
        setStatusCode 404 >=> text "Not found"


[<AutoOpen>]
module private Helpers =
    
    open Microsoft.AspNetCore.Antiforgery
    open Microsoft.Extensions.DependencyInjection
    open System.Collections.Concurrent
    open System.IO
    
    /// Cache for parsed templates
    module private TemplateCache =
        
        /// Cache of parsed templates
        let private views = ConcurrentDictionary<string, Template> ()
        
        /// Get a template for the given web log
        let get (theme : string) (templateName : string) = task {
            let templatePath = $"themes/{theme}/{templateName}"
            match views.ContainsKey templatePath with
            | true -> ()
            | false ->
                let! file = File.ReadAllTextAsync $"{templatePath}.liquid"
                views[templatePath] <- Template.Parse (file, SyntaxCompatibility.DotLiquid22)
            return views[templatePath]
        }
    
    /// Either get the web log from the hash, or get it from the cache and add it to the hash
    let deriveWebLogFromHash (hash : Hash) ctx =
        match hash.ContainsKey "web_log" with
        | true -> hash["web_log"] :?> WebLog
        | false ->
            let wl = WebLogCache.getByCtx ctx
            hash.Add ("web_log", wl)
            wl
    
    /// Render a view for the specified theme, using the specified template, layout, and hash
    let viewForTheme theme template layout next ctx = fun (hash : Hash) -> task {
        // Don't need the web log, but this adds it to the hash if the function is called directly
        let _ = deriveWebLogFromHash hash ctx
        hash.Add ("logged_on", ctx.User.Identity.IsAuthenticated)
        
        // NOTE: DotLiquid does not support {% render %} or {% include %} in its templates, so we will do a two-pass
        //       render; the net effect is a "layout" capability similar to Razor or Pug
        
        // Render view content...
        let! contentTemplate = TemplateCache.get theme template
        hash.Add ("content", contentTemplate.Render hash)
        
        // ...then render that content with its layout
        let! layoutTemplate = TemplateCache.get theme (defaultArg layout "layout")
        return! htmlString (layoutTemplate.Render hash) next ctx
    }
    
    /// Return a view for the web log's default theme
    let themedView template layout next ctx = fun (hash : Hash) -> task {
        return! viewForTheme (deriveWebLogFromHash hash ctx).themePath template layout next ctx hash
    }
    
    /// The web log ID for the current request
    let webLogId ctx = (WebLogCache.getByCtx ctx).id
    
    let conn (ctx : HttpContext) = ctx.RequestServices.GetRequiredService<IConnection> ()
    
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
    let requireUser = requiresAuthentication Error.notAuthorized


/// Handlers to manipulate admin functions
module Admin =
    
    // GET /admin/
    let dashboard : HttpHandler = requireUser >=> fun next ctx -> task {
        let webLogId' = webLogId ctx
        let conn' = conn ctx
        let getCount (f : WebLogId -> IConnection -> Task<int>) = f webLogId' conn'
        let! posts   = Data.Post.countByStatus Published |> getCount
        let! drafts  = Data.Post.countByStatus Draft     |> getCount
        let! pages   = Data.Page.countAll                |> getCount
        let! listed  = Data.Page.countListed             |> getCount
        let! cats    = Data.Category.countAll            |> getCount
        let! topCats = Data.Category.countTopLevel       |> getCount
        return!
            Hash.FromAnonymousObject
                {| page_title = "Dashboard"
                   model =
                       { posts              = posts
                         drafts             = drafts
                         pages              = pages
                         listedPages        = listed
                         categories         = cats
                         topLevelCategories = topCats
                       }
                |}
            |> viewForTheme "admin" "dashboard" None next ctx
    }
    
    // GET /admin/settings
    let settings : HttpHandler = requireUser >=> fun next ctx -> task {
        let webLog = WebLogCache.getByCtx ctx
        let! allPages = Data.Page.findAll webLog.id (conn ctx)
        return!
            Hash.FromAnonymousObject
                {|  csrf  = csrfToken ctx
                    model =
                        { name         = webLog.name
                          subtitle     = defaultArg webLog.subtitle ""
                          defaultPage  = webLog.defaultPage
                          postsPerPage = webLog.postsPerPage
                          timeZone     = webLog.timeZone
                        }
                    pages =
                        seq {
                            KeyValuePair.Create ("posts", "- First Page of Posts -")
                            yield! allPages
                                   |> List.map (fun p -> KeyValuePair.Create (PageId.toString p.id, p.title))
                        }
                        |> Array.ofSeq
                    web_log    = webLog
                    page_title = "Web Log Settings"
                |}
            |> viewForTheme "admin" "settings" None next ctx
    }
    
    // POST /admin/settings
    let saveSettings : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
        let  conn' = conn ctx
        let! model = ctx.BindFormAsync<SettingsModel> ()
        match! Data.WebLog.findByHost (WebLogCache.getByCtx ctx).urlBase conn' with
        | Some webLog ->
            let updated =
                { webLog with
                    name         = model.name
                    subtitle     = match model.subtitle with "" -> None | it -> Some it
                    defaultPage  = model.defaultPage
                    postsPerPage = model.postsPerPage
                    timeZone     = model.timeZone
                }
            do! Data.WebLog.updateSettings updated conn'

            // Update cache
            WebLogCache.set updated.urlBase updated
        
            // TODO: confirmation message

            return! redirectTo false "/admin/" next ctx
        | None -> return! Error.notFound next ctx
    }


/// Handlers to manipulate posts
module Post =
    
    // GET /page/{pageNbr}
    let pageOfPosts (pageNbr : int) : HttpHandler = fun next ctx -> task {
        let webLog = WebLogCache.getByCtx ctx
        let! posts = Data.Post.findPageOfPublishedPosts webLog.id pageNbr webLog.postsPerPage (conn ctx)
        let hash = Hash.FromAnonymousObject {| posts = posts |}
        let title =
            match pageNbr, webLog.defaultPage with
            | 1, "posts" -> None
            | _, "posts" -> Some $"Page {pageNbr}"
            | _, _ -> Some $"Page {pageNbr} &#xab; Posts"
        match title with Some ttl -> hash.Add ("page_title", ttl) | None -> ()
        return! themedView "index" None next ctx hash
    }

    // GET /
    let home : HttpHandler = fun next ctx -> task {
        let webLog = WebLogCache.getByCtx ctx
        match webLog.defaultPage with
        | "posts" -> return! pageOfPosts 1 next ctx
        | pageId ->
            match! Data.Page.findById (PageId pageId) webLog.id (conn ctx) with
            | Some page ->
                return!
                    Hash.FromAnonymousObject {| page = page; page_title = page.title |}
                    |> themedView "single-page" page.template next ctx
            | None -> return! Error.notFound next ctx
    }
    
    // GET *
    let catchAll (link : string) : HttpHandler = fun next ctx -> task {
        let webLog    = WebLogCache.getByCtx ctx
        let conn'     = conn ctx
        let permalink = Permalink link
        match! Data.Post.findByPermalink permalink webLog.id conn' with
        | Some post -> return! Error.notFound next ctx
            // TODO: return via single-post action
        | None ->
            match! Data.Page.findByPermalink permalink webLog.id conn' with
            | Some page ->
                return!
                    Hash.FromAnonymousObject {| page = page; page_title = page.title |}
                    |> themedView "single-page" page.template next ctx
            | None ->

                // TOOD: search prior permalinks for posts and pages

                // We tried, we really tried...
                Console.Write($"Returning 404 for permalink |{permalink}|");
                return! Error.notFound next ctx
    }


/// Handlers to manipulate users
module User =
    
    open Microsoft.AspNetCore.Authentication;
    open Microsoft.AspNetCore.Authentication.Cookies
    open System.Security.Claims
    open System.Security.Cryptography
    open System.Text
    
    /// Hash a password for a given user
    let hashedPassword (plainText : string) (email : string) (salt : Guid) =
        let allSalt = Array.concat [ salt.ToByteArray(); (Encoding.UTF8.GetBytes email) ] 
        use alg = new Rfc2898DeriveBytes (plainText, allSalt, 2_048)
        Convert.ToBase64String(alg.GetBytes(64))
    
    // GET /user/log-on
    let logOn : HttpHandler = fun next ctx -> task {
        return!
            Hash.FromAnonymousObject {| page_title = "Log On"; csrf = (csrfToken ctx) |}
            |> viewForTheme "admin" "log-on" None next ctx
    }
    
    // POST /user/log-on
    let doLogOn : HttpHandler = validateCsrf >=> fun next ctx -> task {
        let! model = ctx.BindFormAsync<LogOnModel> ()
        match! Data.WebLogUser.findByEmail model.emailAddress (webLogId ctx) (conn ctx) with 
        | Some user when user.passwordHash = hashedPassword model.password user.userName user.salt ->
            let claims = seq {
                Claim (ClaimTypes.NameIdentifier, WebLogUserId.toString user.id)
                Claim (ClaimTypes.Name, $"{user.firstName} {user.lastName}")
                Claim (ClaimTypes.GivenName, user.preferredName)
                Claim (ClaimTypes.Role, user.authorizationLevel.ToString ())
            }
            let identity = ClaimsIdentity (claims, CookieAuthenticationDefaults.AuthenticationScheme)

            do! ctx.SignInAsync (identity.AuthenticationType, ClaimsPrincipal identity,
                AuthenticationProperties (IssuedUtc = DateTimeOffset.UtcNow))

            // TODO: confirmation message

            return! redirectTo false "/admin/" next ctx
        | _ ->
            // TODO: make error, not 404
            return! Error.notFound next ctx
    }

    let logOff : HttpHandler = fun next ctx -> task {
        do! ctx.SignOutAsync CookieAuthenticationDefaults.AuthenticationScheme

        // TODO: confirmation message

        return! redirectTo false "/" next ctx
    }
    

open Giraffe.EndpointRouting

/// The endpoints defined in the above handlers
let endpoints = [
    GET [
        route "/" Post.home
    ]
    subRoute "/admin" [
        GET [
            route "/"         Admin.dashboard
            route "/settings" Admin.settings
        ]
        POST [
            route "/settings" Admin.saveSettings
        ]
    ]
    subRoute "/page" [
        GET [
            routef "/%d" Post.pageOfPosts
        ]
    ]
    subRoute "/user" [
        GET [
            route "/log-on"  User.logOn
            route "/log-off" User.logOff
        ]
        POST [
            route "/log-on" User.doLogOn
        ]
    ]
]
