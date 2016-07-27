namespace MyWebLog

open MyWebLog.Data.WebLog
open MyWebLog.Entities
open Nancy
open RethinkDb.Driver.Net

/// Handle /admin routes
type AdminModule(conn : IConnection) as this =
  inherit NancyModule("/admin")
  
  do
    this.Get.["/"] <- fun _ -> this.Dashboard ()

  /// Admin dashboard
  member this.Dashboard () =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = DashboardModel(this.Context, this.WebLog, findDashboardCounts conn this.WebLog.Id)
    model.PageTitle  <- Resources.Dashboard
    upcast this.View.["admin/dashboard", model]
