[<AutoOpen>]
module myWebLog.ModuleExtensions

open myWebLog
open myWebLog.Entities
open Nancy
open Nancy.Security

/// Parent class for all myWebLog Nancy modules
type NancyModule with

  /// Strongly-typed access to the web log for the current request
  member this.WebLog = this.Context.Items.[Keys.WebLog] :?> WebLog

  /// Display a view using the theme specified for the web log 
  member this.ThemedView view model = this.View.[(sprintf "%s/%s" this.WebLog.themePath view), model]

  /// Return a 404
  member this.NotFound () = this.Negotiate.WithStatusCode 404

  /// Redirect a request, storing messages in the session if they exist
  member this.Redirect url (model : MyWebLogModel) =
    match List.length model.messages with
    | 0 -> ()
    | _ -> this.Session.[Keys.Messages] <- model.messages
    this.Negotiate.WithHeader("Location", url).WithStatusCode(HttpStatusCode.TemporaryRedirect)

  /// Require a specific level of access for the current web log
  member this.RequiresAccessLevel level =
    this.RequiresAuthentication()
    this.RequiresClaims [| sprintf "%s|%s" this.WebLog.id level |]
