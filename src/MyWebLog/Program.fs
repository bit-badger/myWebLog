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
                do! CategoryCache.update ctx
                return! next.Invoke ctx
            | None -> ctx.Response.StatusCode <- 404
    }


/// DotLiquid filters
module DotLiquidBespoke =
    
    open System.IO
    open DotLiquid

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
    
    /// A filter to retrieve the value of a meta item from a list
    //    (shorter than `{% assign item = list | where: "name", [name] | first %}{{ item.value }}`)
    type ValueFilter () =
        static member Value (_ : Context, items : MetaItem list, name : string) =
            match items |> List.tryFind (fun it -> it.name = name) with
            | Some item -> item.value
            | None -> $"-- {name} not found --"


/// Create the default information for a new web log
module NewWebLog =
    
    /// Create the web log information
    let private createWebLog (args : string[]) (sp : IServiceProvider) = task {
        
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
                        { asOf = DateTime.UtcNow
                          text = Html "<p>This is your default home page.</p>"
                        }
                    ]
                } conn

        Console.WriteLine($"Successfully initialized database for {args[2]} with URL base {args[1]}");
    }

    /// Create a new web log
    let create args sp = task {
        match args |> Array.length with
        | 5 -> return! createWebLog args sp
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
open RethinkDB.DistributedCache
open RethinkDb.Driver.FSharp

[<EntryPoint>]
let main args =

    let builder = WebApplication.CreateBuilder(args)
    let _ = 
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(fun opts ->
                opts.ExpireTimeSpan    <- TimeSpan.FromMinutes 60.
                opts.SlidingExpiration <- true
                opts.AccessDeniedPath  <- "/forbidden")
    let _ = builder.Services.AddLogging ()
    let _ = builder.Services.AddAuthorization ()
    let _ = builder.Services.AddAntiforgery ()
    
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
    
    let _ = builder.Services.AddDistributedRethinkDBCache (fun opts ->
        opts.TableName  <- "Session"
        opts.Connection <- conn)
    let _ = builder.Services.AddSession(fun opts ->
        opts.IdleTimeout        <- TimeSpan.FromMinutes 30
        opts.Cookie.HttpOnly    <- true
        opts.Cookie.IsEssential <- true)
    
    // this needs to be after the session... maybe?
    let _ = builder.Services.AddGiraffe ()
    
    // Set up DotLiquid
    Template.RegisterFilter typeof<DotLiquidBespoke.NavLinkFilter>
    Template.RegisterFilter typeof<DotLiquidBespoke.ValueFilter>
    Template.RegisterTag<DotLiquidBespoke.UserLinksTag> "user_links"
    
    [   // Domain types
        typeof<MetaItem>; typeof<Page>; typeof<WebLog>
        // View models
        typeof<DashboardModel>; typeof<DisplayCategory>; typeof<DisplayPage>; typeof<EditCategoryModel>
        typeof<EditPageModel>;  typeof<EditPostModel>;   typeof<LogOnModel>;  typeof<PostDisplay>
        typeof<PostListItem>;   typeof<SettingsModel>;   typeof<UserMessage>
        // Framework types
        typeof<AntiforgeryTokenSet>; typeof<KeyValuePair>; typeof<MetaItem list>; typeof<string list>
        typeof<string option>
    ]
    |> List.iter (fun it -> Template.RegisterSafeType (it, [| "*" |]))

    let app = builder.Build ()
    
    match args |> Array.tryHead with
    | Some it when it = "init" -> NewWebLog.create args app.Services |> Async.AwaitTask |> Async.RunSynchronously
    | _ ->
        let _ = app.UseCookiePolicy (CookiePolicyOptions (MinimumSameSitePolicy = SameSiteMode.Strict))
        let _ = app.UseMiddleware<WebLogMiddleware> ()
        let _ = app.UseAuthentication ()
        let _ = app.UseStaticFiles ()
        let _ = app.UseRouting ()
        let _ = app.UseSession ()
        let _ = app.UseGiraffe Handlers.endpoints

        app.Run()

    0 // Exit code

