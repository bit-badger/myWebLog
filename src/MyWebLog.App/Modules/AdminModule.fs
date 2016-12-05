namespace MyWebLog

open MyWebLog.Data
open MyWebLog.Entities
open MyWebLog.Logic.WebLog
open MyWebLog.Resources
open Nancy
open RethinkDb.Driver.Net

/// Handle /admin routes
type AdminModule (data : IMyWebLogData) as this =
  inherit NancyModule ("/admin")
  
  do
    this.Get ("/", fun _ -> this.Dashboard ())

  /// Admin dashboard
  member this.Dashboard () : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = DashboardModel (this.Context, this.WebLog, findDashboardCounts data this.WebLog.Id)
    model.PageTitle <- Strings.get "Dashboard"
    upcast this.View.["admin/dashboard", model]
