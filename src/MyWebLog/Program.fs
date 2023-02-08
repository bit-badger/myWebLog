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
            if isDebug then log.LogDebug $"Resolved web log {WebLogId.toString webLog.Id} for {path}"
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
open Newtonsoft.Json
open Npgsql

/// Logic to obtain a data connection and implementation based on configured values
module DataImplementation =
    
    open MyWebLog.Converters
    open RethinkDb.Driver.FSharp
    open RethinkDb.Driver.Net

    /// Create an NpgsqlDataSource from the connection string, configuring appropriately
    let createNpgsqlDataSource (cfg : IConfiguration) =
        let builder = NpgsqlDataSourceBuilder (cfg.GetConnectionString "PostgreSQL")
        let _ = builder.UseNodaTime ()
        let _ = builder.UseLoggerFactory(LoggerFactory.Create(fun it -> it.AddConsole () |> ignore))
        builder.Build ()

    /// Get the configured data implementation
    let get (sp : IServiceProvider) : IData =
        let config   = sp.GetRequiredService<IConfiguration> ()
        let await it = (Async.AwaitTask >> Async.RunSynchronously) it
        let connStr    name = config.GetConnectionString name
        let hasConnStr name = (connStr >> isNull >> not) name
        let createSQLite connStr : IData =
            let log  = sp.GetRequiredService<ILogger<SQLiteData>> ()
            let conn = new SqliteConnection (connStr)
            log.LogInformation $"Using SQLite database {conn.DataSource}"
            await (SQLiteData.setUpConnection conn)
            SQLiteData (conn, log, Json.configure (JsonSerializer.CreateDefault ()))
        
        if hasConnStr "SQLite" then
            createSQLite (connStr "SQLite")
        elif hasConnStr "RethinkDB" then
            let log        = sp.GetRequiredService<ILogger<RethinkDbData>> ()
            let _          = Json.configure Converter.Serializer 
            let rethinkCfg = DataConfig.FromUri (connStr "RethinkDB")
            let conn       = await (rethinkCfg.CreateConnectionAsync log)
            RethinkDbData (conn, rethinkCfg, log)
        elif hasConnStr "PostgreSQL" then
            let source = createNpgsqlDataSource config
            use conn = source.CreateConnection ()
            let log  = sp.GetRequiredService<ILogger<PostgresData>> ()
            log.LogWarning (sprintf "%s %s" conn.DataSource conn.Database)
            log.LogInformation $"Using PostgreSQL database {conn.Host}:{conn.Port}/{conn.Database}"
            PostgresData (source, log, Json.configure (JsonSerializer.CreateDefault ()))
        else
            createSQLite "Data Source=./myweblog.db;Cache=Shared"


open System.Threading.Tasks

/// Show a list of valid command-line interface commands
let showHelp () =
    printfn " "
    printfn "COMMAND       WHAT IT DOES"
    printfn "-----------   ------------------------------------------------------"
    printfn "backup        Create a JSON file backup of a web log"
    printfn "do-restore    Restore a JSON file backup (overwrite data silently)"
    printfn "help          Display this information"
    printfn "import-links  Import prior permalinks"
    printfn "init          Initializes a new web log"
    printfn "load-theme    Load a theme"
    printfn "restore       Restore a JSON file backup (prompt before overwriting)"
    printfn "set-password  Set a password for a specific user"
    printfn "upgrade-user  Upgrade a WebLogAdmin user to a full Administrator"
    printfn " "
    printfn "For more information on a particular command, run it with no options."
    Task.FromResult ()


open System.IO
open Giraffe
open Giraffe.EndpointRouting
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.Extensions.Caching.Distributed
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
    let data = DataImplementation.get sp
    let _ = builder.Services.AddSingleton<JsonSerializer> data.Serializer
    
    task {
        do! data.StartUp ()
        do! WebLogCache.fill data
        do! ThemeAssetCache.fill data
    } |> Async.AwaitTask |> Async.RunSynchronously
    
    // Define distributed cache implementation based on data implementation
    match data with
    | :? RethinkDbData as rethink ->
        // A RethinkDB connection is designed to work as a singleton
        let _ = builder.Services.AddSingleton<IData> data
        let _ =
            builder.Services.AddDistributedRethinkDBCache (fun opts ->
                opts.TableName  <- "Session"
                opts.Connection <- rethink.Conn)
        ()
    | :? SQLiteData as sql ->
        // ADO.NET connections are designed to work as per-request instantiation
        let cfg  = sp.GetRequiredService<IConfiguration> ()
        let _ =
            builder.Services.AddScoped<SqliteConnection> (fun sp ->
                let conn = new SqliteConnection (sql.Conn.ConnectionString)
                SQLiteData.setUpConnection conn |> Async.AwaitTask |> Async.RunSynchronously
                conn)
        let _ = builder.Services.AddScoped<IData, SQLiteData> () |> ignore
        // Use SQLite for caching as well
        let cachePath = defaultArg (Option.ofObj (cfg.GetConnectionString "SQLiteCachePath")) "./session.db"
        let _ = builder.Services.AddSqliteCache (fun o -> o.CachePath <- cachePath)
        ()
    | :? PostgresData as postgres ->
        // ADO.NET Data Sources are designed to work as singletons
        let _ =
            builder.Services.AddSingleton<NpgsqlDataSource> (fun sp ->
                DataImplementation.createNpgsqlDataSource (sp.GetRequiredService<IConfiguration> ()))
        let _ = builder.Services.AddSingleton<IData> postgres
        let _ =
            builder.Services.AddSingleton<IDistributedCache> (fun sp ->
                Postgres.DistributedCache ((sp.GetRequiredService<IConfiguration> ()).GetConnectionString "PostgreSQL")
                :> IDistributedCache)
        ()
    | _ -> ()
    
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
    | Some it when it = "do-restore"   -> Maintenance.Backup.restoreFromBackup args app.Services
    | Some it when it = "upgrade-user" -> Maintenance.upgradeUser              args app.Services
    | Some it when it = "set-password" -> Maintenance.setPassword              args app.Services
    | Some it when it = "help"         -> showHelp ()
    | Some it ->
        printfn $"""Unrecognized command "{it}" - valid commands are:"""
        showHelp ()
    | None -> task {
        // Load all themes in the application directory
        for themeFile in Directory.EnumerateFiles (".", "*-theme.zip") do
            do! Maintenance.loadTheme [| ""; themeFile |] app.Services
            
        let _ = app.UseForwardedHeaders ()
        let _ = app.UseCookiePolicy (CookiePolicyOptions (MinimumSameSitePolicy = SameSiteMode.Strict))
        let _ = app.UseMiddleware<WebLogMiddleware> ()
        let _ = app.UseAuthentication ()
        let _ = app.UseStaticFiles ()
        let _ = app.UseRouting ()
        let _ = app.UseSession ()
        let _ = app.UseGiraffe Handlers.Routes.endpoint

        app.Run ()
    }
    |> Async.AwaitTask |> Async.RunSynchronously
    
    0 // Exit code
