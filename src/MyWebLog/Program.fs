open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open MyWebLog
open RethinkDb.Driver.Net
open System

/// Middleware to derive the current web log
type WebLogMiddleware (next : RequestDelegate) =

    member this.InvokeAsync (ctx : HttpContext) = task {
        match WebLogCache.exists ctx with
        | true -> return! next.Invoke ctx
        | false ->
            let conn = ctx.RequestServices.GetRequiredService<IConnection> ()
            match! Data.WebLog.findByHost (Cache.makeKey ctx) conn with
            | Some webLog ->
                WebLogCache.set ctx webLog
                do! PageListCache.update ctx
                return! next.Invoke ctx
            | None -> ctx.Response.StatusCode <- 404
    }


/// DotLiquid filters
module DotLiquidBespoke =
    
    open DotLiquid
    open System.IO

    /// A filter to generate nav links, highlighting the active link (exact match)
    type NavLinkFilter () =
        static member NavLink (ctx : Context, url : string, text : string) =
            seq {
                "<li class=\"nav-item\"><a class=\"nav-link"
                if url = string ctx.Environments[0].["current_page"] then " active"
                "\" href=\"/"
                url
                "\">"
                text
                "</a></li>"
            }
            |> Seq.fold (+) ""
    
    /// Create links for a user to log on or off, and a dashboard link if they are logged off
    type UserLinksTag () =
        inherit Tag ()
        
        override this.Render (context : Context, result : TextWriter) =
            seq {
                """<ul class="navbar-nav flex-grow-1 justify-content-end">"""
                match Convert.ToBoolean context.Environments[0].["logged_on"] with
                | true ->
                    """<li class="nav-item"><a class="nav-link" href="/admin">Dashboard</a></li>"""
                    """<li class="nav-item"><a class="nav-link" href="/user/log-off">Log Off</a></li>"""
                | false ->
                    """<li class="nav-item"><a class="nav-link" href="/user/log-on">Log On</a></li>"""
                "</ul>"
            }
            |> Seq.iter result.WriteLine


/// Initialize a new database
let initDbValidated (args : string[]) (sp : IServiceProvider) = task {
    
    let conn = sp.GetRequiredService<IConnection> ()
    
    let timeZone =
        let local = TimeZoneInfo.Local.Id
        match TimeZoneInfo.Local.HasIanaId with
        | true -> local
        | false ->
            match TimeZoneInfo.TryConvertWindowsIdToIanaId local with
            | true, ianaId -> ianaId
            | false, _ -> raise <| TimeZoneNotFoundException $"Cannot find IANA timezone for {local}"
    
    // Create the web log
    let webLogId   = WebLogId.create ()
    let userId     = WebLogUserId.create ()
    let homePageId = PageId.create ()
    
    do! Data.WebLog.add
            { WebLog.empty with
                id          = webLogId
                name        = args[2]
                urlBase     = args[1]
                defaultPage = PageId.toString homePageId
                timeZone    = timeZone
            } conn
    
    // Create the admin user
    let salt = Guid.NewGuid ()
    
    do! Data.WebLogUser.add 
            { WebLogUser.empty with
                id                 = userId
                webLogId           = webLogId
                userName           = args[3]
                firstName          = "Admin"
                lastName           = "User"
                preferredName      = "Admin"
                passwordHash       = Handlers.User.hashedPassword args[4] args[3] salt
                salt               = salt
                authorizationLevel = Administrator
            } conn

    // Create the default home page
    do! Data.Page.add
            { Page.empty with
                id          = homePageId
                webLogId    = webLogId
                authorId    = userId
                title       = "Welcome to myWebLog!"
                permalink   = Permalink "welcome-to-myweblog.html"
                publishedOn = DateTime.UtcNow
                updatedOn   = DateTime.UtcNow
                text        = "<p>This is your default home page.</p>"
                revisions   = [
                    { asOf       = DateTime.UtcNow
                      sourceType = Html
                      text       = "<p>This is your default home page.</p>"
                    }
                ]
            } conn

    Console.WriteLine($"Successfully initialized database for {args[2]} with URL base {args[1]}");
}

/// Initialize a new database
let initDb args sp = task {
    match args |> Array.length with
    | 5 -> return! initDbValidated args sp
    | _ ->
        Console.WriteLine "Usage: MyWebLog init [url] [name] [admin-email] [admin-pw]"
        return! System.Threading.Tasks.Task.CompletedTask
}


open DotLiquid
open Giraffe
open Giraffe.EndpointRouting
open Microsoft.AspNetCore.Antiforgery
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open MyWebLog.ViewModels
open RethinkDb.Driver.FSharp

[<EntryPoint>]
let main args =

    let builder = WebApplication.CreateBuilder(args)
    let _ = 
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(fun opts ->
                opts.ExpireTimeSpan    <- TimeSpan.FromMinutes 20.
                opts.SlidingExpiration <- true
                opts.AccessDeniedPath  <- "/forbidden")
    let _ = builder.Services.AddLogging ()
    let _ = builder.Services.AddAuthorization ()
    let _ = builder.Services.AddAntiforgery ()
    let _ = builder.Services.AddGiraffe ()
    
    // Configure RethinkDB's connection
    JsonConverters.all () |> Seq.iter Converter.Serializer.Converters.Add 
    let sp         = builder.Services.BuildServiceProvider ()
    let config     = sp.GetRequiredService<IConfiguration> ()
    let loggerFac  = sp.GetRequiredService<ILoggerFactory> ()
    let rethinkCfg = DataConfig.FromConfiguration (config.GetSection "RethinkDB")
    let conn =
        task {
            let! conn = rethinkCfg.CreateConnectionAsync ()
            do! Data.Startup.ensureDb rethinkCfg (loggerFac.CreateLogger (nameof Data.Startup)) conn
            return conn
        } |> Async.AwaitTask |> Async.RunSynchronously
    let _ = builder.Services.AddSingleton<IConnection> conn
    
    // Set up DotLiquid
    Template.RegisterFilter typeof<DotLiquidBespoke.NavLinkFilter>
    Template.RegisterTag<DotLiquidBespoke.UserLinksTag> "user_links"
    
    let all = [| "*" |]
    Template.RegisterSafeType (typeof<Page>, all)
    Template.RegisterSafeType (typeof<WebLog>, all)
    
    Template.RegisterSafeType (typeof<DashboardModel>, all)
    Template.RegisterSafeType (typeof<DisplayPage>, all)
    Template.RegisterSafeType (typeof<SettingsModel>, all)
    Template.RegisterSafeType (typeof<EditPageModel>, all)
    
    Template.RegisterSafeType (typeof<AntiforgeryTokenSet>, all)
    Template.RegisterSafeType (typeof<string option>, all)
    Template.RegisterSafeType (typeof<KeyValuePair>, all)

    let app = builder.Build ()
    
    match args |> Array.tryHead with
    | Some it when it = "init" -> initDb args app.Services |> Async.AwaitTask |> Async.RunSynchronously
    | _ ->
        let _ = app.UseCookiePolicy (CookiePolicyOptions (MinimumSameSitePolicy = SameSiteMode.Strict))
        let _ = app.UseMiddleware<WebLogMiddleware> ()
        let _ = app.UseAuthentication ()
        let _ = app.UseStaticFiles ()
        let _ = app.UseRouting ()
        let _ = app.UseGiraffe Handlers.endpoints

        app.Run()

    0 // Exit code

