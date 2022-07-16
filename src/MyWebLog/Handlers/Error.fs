/// Handlers for error conditions
module MyWebLog.Handlers.Error

open System.Net
open Giraffe
open MyWebLog

/// Handle unauthorized actions, redirecting to log on for GETs, otherwise returning a 401 Not Authorized response
let notAuthorized : HttpHandler =
    handleContext (fun ctx ->
        if ctx.Request.Method = "GET" then
            let returnUrl = WebUtility.UrlEncode ctx.Request.Path
            redirectTo false (WebLog.relativeUrl ctx.WebLog (Permalink $"user/log-on?returnUrl={returnUrl}"))
                earlyReturn ctx
        else
            setStatusCode 401 earlyReturn ctx)


/// Handle 404s from the API, sending known URL paths to the Vue app so that they can be handled there
let notFound : HttpHandler = fun _ ->
    (setStatusCode 404 >=> text "Not found") earlyReturn
