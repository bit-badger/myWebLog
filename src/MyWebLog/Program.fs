open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
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
open Microsoft.Extensions.DependencyInjection
open MyWebLog.Data

/// Logic to obtain a data connection and implementation based on configured values
module DataImplementation =
    
    open MyWebLog.Converters
    open RethinkDb.Driver.FSharp
    open RethinkDb.Driver.Net

    /// Get the configured data implementation
    let get (sp : IServiceProvider) : IData option =
        let config = sp.GetRequiredService<IConfiguration> ()
        if (config.GetSection "RethinkDB").Exists () then
            Json.all () |> Seq.iter Converter.Serializer.Converters.Add 
            let rethinkCfg = DataConfig.FromConfiguration (config.GetSection "RethinkDB")
            let conn       = rethinkCfg.CreateConnectionAsync () |> Async.AwaitTask |> Async.RunSynchronously
            Some (upcast RethinkDbData (conn, rethinkCfg, sp.GetRequiredService<ILogger<RethinkDbData>> ()))
        elif (config.GetConnectionString >> isNull >> not) "SQLite" then
            let conn = new SqliteConnection (config.GetConnectionString "SQLite")
            SQLiteData.setUpConnection conn |> Async.AwaitTask |> Async.RunSynchronously
            Some (upcast SQLiteData conn)
        else
            None


open Giraffe
open Giraffe.EndpointRouting
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.HttpOverrides
open NeoSmart.Caching.Sqlite
open RethinkDB.DistributedCache

[<EntryPoint>]
let rec main args =

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
    
    let sp = builder.Services.BuildServiceProvider ()
    match DataImplementation.get sp with
    | Some data ->
        task {
            do! data.startUp ()
            do! WebLogCache.fill data
            do! ThemeAssetCache.fill data
        } |> Async.AwaitTask |> Async.RunSynchronously
        
        // Define distributed cache implementation based on data implementation
        match data with
        | :? RethinkDbData as rethink ->
            // A RethinkDB connection is designed to work as a singleton
            builder.Services.AddSingleton<IData> data |> ignore
            builder.Services.AddDistributedRethinkDBCache (fun opts ->
                opts.TableName  <- "Session"
                opts.Connection <- rethink.Conn)
            |> ignore
        | :? SQLiteData ->
            // ADO.NET connections are designed to work as per-request instantiation
            let cfg  = sp.GetRequiredService<IConfiguration> ()
            builder.Services.AddScoped<SqliteConnection> (fun sp ->
                let conn = new SqliteConnection (cfg.GetConnectionString "SQLite")
                SQLiteData.setUpConnection conn |> Async.AwaitTask |> Async.RunSynchronously
                conn)
            |> ignore
            builder.Services.AddScoped<IData, SQLiteData> () |> ignore
            // Use SQLite for caching as well
            let cachePath = defaultArg (Option.ofObj (cfg.GetConnectionString "SQLiteCachePath")) "./session.db"
            builder.Services.AddSqliteCache (fun o -> o.CachePath <- cachePath) |> ignore
        | _ -> ()
    | None ->
        invalidOp "There is no data configuration present; please add a RethinkDB section or LiteDB connection string"
    
    let _ = builder.Services.AddSession(fun opts ->
        opts.IdleTimeout        <- TimeSpan.FromMinutes 60
        opts.Cookie.HttpOnly    <- true
        opts.Cookie.IsEssential <- true)
    let _ = builder.Services.AddGiraffe ()
    
    // Set up DotLiquid
    DotLiquidBespoke.register ()

    let app = builder.Build ()
    
    match args |> Array.tryHead with
    | Some it when it = "init"         -> Maintenance.createWebLog             args app.Services
    | Some it when it = "import-links" -> Maintenance.importLinks              args app.Services
    | Some it when it = "load-theme"   -> Maintenance.loadTheme                args app.Services
    | Some it when it = "backup"       -> Maintenance.Backup.generateBackup    args app.Services
    | Some it when it = "restore"      -> Maintenance.Backup.restoreFromBackup args app.Services    
    | _ ->
        let _ = app.UseForwardedHeaders ()
        let _ = app.UseCookiePolicy (CookiePolicyOptions (MinimumSameSitePolicy = SameSiteMode.Strict))
        let _ = app.UseMiddleware<WebLogMiddleware> ()
        let _ = app.UseAuthentication ()
        let _ = app.UseStaticFiles ()
        let _ = app.UseRouting ()
        let _ = app.UseSession ()
        let _ = app.UseGiraffe Handlers.Routes.endpoint

        System.Threading.Tasks.Task.FromResult (app.Run ())
    |> Async.AwaitTask |> Async.RunSynchronously
    
    0 // Exit code
