/// Handlers for error conditions
module MyWebLog.Handlers.Error

open System.Net
open System.Threading.Tasks
open Giraffe
open Microsoft.AspNetCore.Http
open MyWebLog

/// Handle unauthorized actions, redirecting to log on for GETs, otherwise returning a 401 Not Authorized response
let notAuthorized : HttpHandler = fun next ctx -> task {
    if ctx.Request.Method = "GET" then
        let returnUrl = WebUtility.UrlEncode ctx.Request.Path
        return!
            redirectTo false (WebLog.relativeUrl ctx.WebLog (Permalink $"user/log-on?returnUrl={returnUrl}")) next ctx
    else
        return! (setStatusCode 401 >=> fun _ _ -> Task.FromResult<HttpContext option> None) next ctx
}

/// Handle 404s from the API, sending known URL paths to the Vue app so that they can be handled there
let notFound : HttpHandler =
    setStatusCode 404 >=> text "Not found"
