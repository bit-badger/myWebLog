open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open MyWebLog

/// Middleware to derive the current web log
type WebLogMiddleware (next : RequestDelegate, log : ILogger<WebLogMiddleware>) =
    
    /// Is the debug level enabled on the logger?
    let isDebug = log.IsEnabled LogLevel.Debug
        
    member this.InvokeAsync (ctx : HttpContext) = task {
        /// Create the full path of the request
        let path = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}{ctx.Request.Path.Value}"
        match WebLogCache.tryGet path with
        | Some webLog ->
            if isDebug then log.LogDebug $"Resolved web log {WebLogId.toString webLog.id} for {path}"
            ctx.Items["webLog"] <- webLog
            if PageListCache.exists ctx then () else do! PageListCache.update ctx
            if CategoryCache.exists ctx then () else do! CategoryCache.update ctx
            return! next.Invoke ctx
        | None ->
            if isDebug then log.LogDebug $"No resolved web log for {path}"
            ctx.Response.StatusCode <- 404
    }

open System
open Giraffe
open Giraffe.EndpointRouting
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open RethinkDB.DistributedCache
open RethinkDb.Driver.FSharp
open RethinkDb.Driver.Net

[<EntryPoint>]
let main args =

    let builder = WebApplication.CreateBuilder(args)
    let _ = builder.Services.Configure<ForwardedHeadersOptions>(fun (opts : ForwardedHeadersOptions) ->
        opts.ForwardedHeaders <- ForwardedHeaders.XForwardedFor ||| ForwardedHeaders.XForwardedProto)
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
            do! WebLogCache.fill conn
            do! ThemeAssetCache.fill conn
            return conn
        } |> Async.AwaitTask |> Async.RunSynchronously
    let _ = builder.Services.AddSingleton<IConnection> conn
    
    let _ = builder.Services.AddDistributedRethinkDBCache (fun opts ->
        opts.TableName  <- "Session"
        opts.Connection <- conn)
    let _ = builder.Services.AddSession(fun opts ->
        opts.IdleTimeout        <- TimeSpan.FromMinutes 60
        opts.Cookie.HttpOnly    <- true
        opts.Cookie.IsEssential <- true)
    
    // this needs to be after the session... maybe?
    let _ = builder.Services.AddGiraffe ()
    
    // Set up DotLiquid
    DotLiquidBespoke.register ()

    let app = builder.Build ()
    
    match args |> Array.tryHead with
    | Some it when it = "init" ->
        Maintenance.createWebLog args app.Services |> Async.AwaitTask |> Async.RunSynchronously
    | Some it when it = "import-permalinks" ->
        Maintenance.importPermalinks args app.Services |> Async.AwaitTask |> Async.RunSynchronously
    | _ ->
        let _ = app.UseForwardedHeaders ()
        let _ = app.UseCookiePolicy (CookiePolicyOptions (MinimumSameSitePolicy = SameSiteMode.Strict))
        let _ = app.UseMiddleware<WebLogMiddleware> ()
        let _ = app.UseAuthentication ()
        let _ = app.UseStaticFiles ()
        let _ = app.UseRouting ()
        let _ = app.UseSession ()
        let _ = app.UseGiraffe Handlers.Routes.endpoint

        app.Run()

    0 // Exit code

