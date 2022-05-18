/// Handlers for error conditions
module MyWebLog.Handlers.Error

open System.Net
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Giraffe

/// Handle unauthorized actions, redirecting to log on for GETs, otherwise returning a 401 Not Authorized response
let notAuthorized : HttpHandler = fun next ctx ->
    (next, ctx)
    ||> match ctx.Request.Method with
        | "GET" -> redirectTo false $"/user/log-on?returnUrl={WebUtility.UrlEncode ctx.Request.Path}"
        | _ -> setStatusCode 401 >=> fun _ _ -> Task.FromResult<HttpContext option> None

/// Handle 404s from the API, sending known URL paths to the Vue app so that they can be handled there
let notFound : HttpHandler =
    setStatusCode 404 >=> text "Not found"
