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
    open MyWebLog.ViewModels

    /// A filter to generate a link with posts categorized under the given category
    type CategoryLinkFilter () =
        static member CategoryLink (_ : Context, catObj : obj) =
            match catObj with
            | :? DisplayCategory as cat -> $"/category/{cat.slug}/"
            | :? DropProxy as proxy -> $"""/category/{proxy["slug"]}/"""
            | _ -> $"alert('unknown category object type {catObj.GetType().Name}')"
    
    /// A filter to generate a link that will edit a page
    type EditPageLinkFilter () =
        static member EditPageLink (_ : Context, postId : string) =
            $"/admin/page/{postId}/edit"
        
    /// A filter to generate a link that will edit a post
    type EditPostLinkFilter () =
        static member EditPostLink (_ : Context, postId : string) =
            $"/admin/post/{postId}/edit"
        
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
    
    /// A filter to generate a link with posts tagged with the given tag
    type TagLinkFilter () =
        static member TagLink (ctx : Context, tag : string) =
            match ctx.Environments[0].["tag_mappings"] :?> TagMap list
                  |> List.tryFind (fun it -> it.tag = tag) with
            | Some tagMap -> $"/tag/{tagMap.urlValue}/"
            | None -> $"""/tag/{tag.Replace (" ", "+")}/"""
                
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
    
    open System.IO
    open RethinkDb.Driver.FSharp
    
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

        printfn $"Successfully initialized database for {args[2]} with URL base {args[1]}"
    }

    /// Create a new web log
    let create args sp = task {
        match args |> Array.length with
        | 5 -> return! createWebLog args sp
        | _ ->
            printfn "Usage: MyWebLog init [url] [name] [admin-email] [admin-pw]"
            return! System.Threading.Tasks.Task.CompletedTask
    }
    
    /// Import prior permalinks from a text files with lines in the format "[old] [new]"
    let importPriorPermalinks urlBase file (sp : IServiceProvider) = task {
        let conn = sp.GetRequiredService<IConnection> ()

        match! Data.WebLog.findByHost urlBase conn with
        | Some webLog ->
            
            let mapping =
                File.ReadAllLines file
                |> Seq.ofArray
                |> Seq.map (fun it ->
                    let parts = it.Split " "
                    Permalink parts[0], Permalink parts[1])
            
            for old, current in mapping do
                match! Data.Post.findByPermalink current webLog.id conn with
                | Some post ->
                    let! withLinks = rethink<Post> {
                        withTable Data.Table.Post
                        get post.id
                        result conn
                    }
                    do! rethink {
                        withTable Data.Table.Post
                        get post.id
                        update [ "priorPermalinks", old :: withLinks.priorPermalinks :> obj]
                        write; ignoreResult conn
                    }
                    printfn $"{Permalink.toString old} -> {Permalink.toString current}"
                | None -> printfn $"Cannot find current post for {Permalink.toString current}"
            printfn "Done!"
        | None -> printfn $"No web log found at {urlBase}"
    }
    
    /// Import permalinks if all is well
    let importPermalinks args sp = task {
        match args |> Array.length with
        | 3 -> return! importPriorPermalinks args[1] args[2] sp
        | _ ->
            printfn "Usage: MyWebLog import-permalinks [url] [file-name]"
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
    [ typeof<DotLiquidBespoke.CategoryLinkFilter>; typeof<DotLiquidBespoke.EditPageLinkFilter>
      typeof<DotLiquidBespoke.EditPostLinkFilter>; typeof<DotLiquidBespoke.NavLinkFilter>
      typeof<DotLiquidBespoke.TagLinkFilter>;      typeof<DotLiquidBespoke.ValueFilter>
    ]
    |> List.iter Template.RegisterFilter
    
    Template.RegisterTag<DotLiquidBespoke.UserLinksTag> "user_links"
    
    [   // Domain types
        typeof<MetaItem>; typeof<Page>; typeof<TagMap>; typeof<WebLog>
        // View models
        typeof<DashboardModel>; typeof<DisplayCategory>;       typeof<DisplayPage>;     typeof<EditCategoryModel>
        typeof<EditPageModel>;  typeof<EditPostModel>;         typeof<EditTagMapModel>; typeof<EditUserModel>
        typeof<LogOnModel>;     typeof<ManagePermalinksModel>; typeof<PostDisplay>;     typeof<PostListItem>
        typeof<SettingsModel>;  typeof<UserMessage>
        // Framework types
        typeof<AntiforgeryTokenSet>; typeof<KeyValuePair>; typeof<MetaItem list>; typeof<string list>
        typeof<string option>;       typeof<TagMap list>
    ]
    |> List.iter (fun it -> Template.RegisterSafeType (it, [| "*" |]))

    let app = builder.Build ()
    
    match args |> Array.tryHead with
    | Some it when it = "init" ->
        NewWebLog.create args app.Services |> Async.AwaitTask |> Async.RunSynchronously
    | Some it when it = "import-permalinks" ->
        NewWebLog.importPermalinks args app.Services |> Async.AwaitTask |> Async.RunSynchronously
    | _ ->
        let _ = app.UseCookiePolicy (CookiePolicyOptions (MinimumSameSitePolicy = SameSiteMode.Strict))
        let _ = app.UseMiddleware<WebLogMiddleware> ()
        let _ = app.UseAuthentication ()
        let _ = app.UseStaticFiles ()
        let _ = app.UseRouting ()
        let _ = app.UseSession ()
        let _ = app.UseGiraffe Handlers.Routes.endpoints

        app.Run()

    0 // Exit code

