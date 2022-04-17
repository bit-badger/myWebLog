open Microsoft.AspNetCore.Mvc.Razor
open System.Reflection

/// Types to support feature folders
module FeatureSupport =
    
    open Microsoft.AspNetCore.Mvc.ApplicationModels
    open System.Collections.Concurrent

    /// A controller model convention that identifies the feature in which a controller exists
    type FeatureControllerModelConvention () =

        /// A cache of controller types to features
        static let _features = ConcurrentDictionary<string, string> ()

        /// Derive the feature name from the controller's type
        static let getFeatureName (typ : TypeInfo) : string option =
            let cacheKey = Option.ofObj typ.FullName |> Option.defaultValue ""
            match _features.ContainsKey cacheKey with
            | true -> Some _features[cacheKey]
            | false ->
                let tokens = cacheKey.Split '.'
                match tokens |> Array.contains "Features" with
                | true ->
                    let feature = tokens |> Array.skipWhile (fun it -> it <> "Features") |> Array.skip 1 |> Array.tryHead
                    match feature with
                    | Some f ->
                        _features[cacheKey] <- f
                        feature
                    | None -> None
                | false -> None
    
        interface IControllerModelConvention with
            /// <inheritdoc />
            member _.Apply (controller: ControllerModel) =
                controller.Properties.Add("feature", getFeatureName controller.ControllerType)


    open Microsoft.AspNetCore.Mvc.Controllers

    /// Expand the location token with the feature name
    type FeatureViewLocationExpander () =
    
        interface IViewLocationExpander with
        
            /// <inheritdoc />
            member _.ExpandViewLocations
                    (context : ViewLocationExpanderContext, viewLocations : string seq) : string seq =
                if isNull context then nullArg (nameof context)
                if isNull viewLocations then nullArg (nameof viewLocations)
                match context.ActionContext.ActionDescriptor with
                | :? ControllerActionDescriptor as descriptor ->
                    let feature = string descriptor.Properties["feature"]
                    viewLocations |> Seq.map (fun location -> location.Replace ("{2}", feature))
                | _ -> invalidArg "context" "ActionDescriptor not found"

            /// <inheritdoc />
            member _.PopulateValues(_ : ViewLocationExpanderContext) = ()


open MyWebLog

/// Types to support themed views
module ThemeSupport =
    
    /// Expand the location token with the theme path
    type ThemeViewLocationExpander () =
        interface IViewLocationExpander with

            /// <inheritdoc />
            member _.ExpandViewLocations
                    (context : ViewLocationExpanderContext, viewLocations : string seq) : string seq =
                if isNull context then nullArg (nameof context)
                if isNull viewLocations then nullArg (nameof viewLocations)

                viewLocations |> Seq.map (fun location -> location.Replace ("{3}", string context.Values["theme"]))

            /// <inheritdoc />
            member _.PopulateValues (context : ViewLocationExpanderContext) =
                if isNull context then nullArg (nameof context)

                context.Values["theme"] <- (WebLogCache.getByCtx context.ActionContext.HttpContext).themePath


open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

/// Custom middleware for this application
module Middleware =
    
    open RethinkDb.Driver.Net
    open System.Threading.Tasks

    /// Middleware to derive the current web log
    type WebLogMiddleware (next : RequestDelegate) =

        member _.InvokeAsync (context : HttpContext) : Task = task {
            let host = WebLogCache.hostToDb context

            match WebLogCache.exists host with
            | true -> ()
            | false ->
                let conn = context.RequestServices.GetRequiredService<IConnection> ()
                match! Data.WebLog.findByHost (context.Request.Host.ToUriComponent ()) conn with
                | Some details -> WebLogCache.set host details
                | None -> ()

            match WebLogCache.exists host with
            | true -> do! next.Invoke context
            | false -> context.Response.StatusCode <- 404
        }


open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Mvc
open System
open System.IO

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let _ =
        builder.Services
            .AddMvc(fun opts ->
                opts.Conventions.Add (FeatureSupport.FeatureControllerModelConvention ())
                opts.Filters.Add (AutoValidateAntiforgeryTokenAttribute ()))
            .AddRazorOptions(fun opts ->
                opts.ViewLocationFormats.Clear ()
                opts.ViewLocationFormats.Add "/Themes/{3}/{0}.cshtml"
                opts.ViewLocationFormats.Add "/Themes/{3}/Shared/{0}.cshtml"
                opts.ViewLocationFormats.Add "/Themes/Default/{0}.cshtml"
                opts.ViewLocationFormats.Add "/Themes/Default/Shared/{0}.cshtml"
                opts.ViewLocationFormats.Add "/Features/{2}/{1}/{0}.cshtml"
                opts.ViewLocationFormats.Add "/Features/{2}/{0}.cshtml"
                opts.ViewLocationFormats.Add "/Features/Shared/{0}.cshtml"
                opts.ViewLocationExpanders.Add (FeatureSupport.FeatureViewLocationExpander ())
                opts.ViewLocationExpanders.Add (ThemeSupport.ThemeViewLocationExpander ()))
    let _ = 
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(fun opts ->
                opts.ExpireTimeSpan    <- TimeSpan.FromMinutes 20.
                opts.SlidingExpiration <- true
                opts.AccessDeniedPath  <- "/forbidden")
    let _ = builder.Services.AddAuthorization()
    let _ = builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor> ()
    (* builder.Services.AddDbContext<WebLogDbContext>(o =>
    {
        // TODO: can get from DI?
        var db = WebLogCache.HostToDb(new HttpContextAccessor().HttpContext!);
         // "empty";
        o.UseSqlite($"Data Source=Db/{db}.db");
    }); *)

    // Load themes
    Directory.GetFiles (Directory.GetCurrentDirectory (), "MyWebLog.Themes.*.dll")
    |> Array.map Assembly.LoadFile
    |> ignore

    let app = builder.Build ()

    let _ = app.UseCookiePolicy (CookiePolicyOptions (MinimumSameSitePolicy = SameSiteMode.Strict))
    let _ = app.UseMiddleware<Middleware.WebLogMiddleware> ()
    let _ = app.UseAuthentication ()
    let _ = app.UseStaticFiles ()
    let _ = app.UseRouting ()
    let _ = app.UseAuthorization ()
    let _ = app.UseEndpoints (fun endpoints -> endpoints.MapControllers () |> ignore)

    app.Run()

    0 // Exit code

