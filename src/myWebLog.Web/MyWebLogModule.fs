namespace myWebLog

open myWebLog.Entities
open Nancy
open Nancy.Security

/// Parent class for all myWebLog Nancy modules
[<AbstractClass>]
type MyWebLogModule() =
  inherit NancyModule()

  /// Strongly-typed access to the web log for the current request
  member this.WebLog = this.Context.Items.[Keys.WebLog] :?> WebLog

  /// Display a view using the theme specified for the web log 
  member this.ThemedRender view model = this.View.[(sprintf "%s/%s" this.WebLog.themePath view), model]

  /// Return a 404
  member this.NotFound () = this.Negotiate.WithStatusCode 404

  /// Require a specific level of access for the current web log
  member this.RequiresAccessLevel level =
    this.RequiresAuthentication()
    this.RequiresClaims [| sprintf "%s|%s" this.WebLog.id level |]
