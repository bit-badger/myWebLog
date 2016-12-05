[<AutoOpen>]
module MyWebLog.ModuleExtensions

open MyWebLog.Entities
open Nancy
open Nancy.Security
open System
open System.Security.Claims

/// Parent class for all myWebLog Nancy modules
type NancyModule with

  /// Strongly-typed access to the web log for the current request
  member this.WebLog = this.Context.Items.[Keys.WebLog] :?> WebLog

  /// Display a view using the theme specified for the web log 
  member this.ThemedView view (model : MyWebLogModel) : obj =
    upcast this.View.[(sprintf "themes/%s/%s" this.WebLog.ThemePath view), model]

  /// Return a 404
  member this.NotFound () : obj = upcast HttpStatusCode.NotFound

  /// Redirect a request, storing messages in the session if they exist
  member this.Redirect url (model : MyWebLogModel) : obj =
    match List.length model.Messages with
    | 0 -> ()
    | _ -> this.Session.[Keys.Messages] <- model.Messages
    // Temp (307) redirects don't reset the HTTP method; this allows POST-process-REDIRECT workflow
    upcast this.Response.AsRedirect(url).WithStatusCode HttpStatusCode.MovedPermanently

  /// Require a specific level of access for the current web log
  member this.RequiresAccessLevel level =
    let findClaim = Predicate<Claim> (fun claim ->
        claim.Type = ClaimTypes.Role && claim.Value = sprintf "%s|%s" this.WebLog.Id level)
    this.RequiresAuthentication ()
    this.RequiresClaims [| findClaim |]
