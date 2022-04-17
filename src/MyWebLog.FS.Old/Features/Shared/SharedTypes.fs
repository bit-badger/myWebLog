namespace MyWebLog.Features.Shared

open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.DependencyInjection
open MyWebLog
open RethinkDb.Driver.Net
open System.Security.Claims

/// Base class for myWebLog controllers
type MyWebLogController () =
    inherit Controller ()

    /// The data context to use to fulfil this request
    member this.Db with get () = this.HttpContext.RequestServices.GetRequiredService<IConnection> ()

    /// The details for the current web log
    member this.WebLog with get () = WebLogCache.getByCtx this.HttpContext

    /// The ID of the currently authenticated user
    member this.UserId with get () =
        this.User.Claims
        |> Seq.tryFind (fun c -> c.Type = ClaimTypes.NameIdentifier)
        |> Option.map (fun c -> c.Value)
        |> Option.defaultValue ""
    
    /// Retern a themed view
    member this.ThemedView (template : string, model : obj) : IActionResult =
        // TODO: get actual version
        this.ViewData["Version"] <- "2"
        this.View (template, model)

    /// Return a 404 response
    member _.NotFound () : IActionResult =
        base.NotFound ()

    /// Redirect to an action in this controller
    member _.RedirectToAction action : IActionResult =
        base.RedirectToAction action


/// Base model class for myWebLog views
type MyWebLogModel (webLog : WebLog) =
    
    /// The details for the web log
    member _.WebLog with get () = webLog
