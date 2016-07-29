module MyWebLog.Data.WebLog

open MyWebLog.Entities
open Rethink
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Counts of items displayed on the admin dashboard
type DashboardCounts =
  { /// The number of pages for the web log
    Pages : int
    /// The number of pages for the web log
    Posts : int
    /// The number of categories for the web log
    Categories : int }

/// Detemine the web log by the URL base
let tryFindWebLogByUrlBase conn (urlBase : string) =
  r.Table(Table.WebLog)
    .GetAll(urlBase).OptArg("index", "UrlBase")
    .Merge(fun w -> r.HashMap("PageList", r.Table(Table.Page)
                                            .GetAll(w.["Id"]).OptArg("index", "WebLogId")
                                            .Filter(ReqlFunction1(fun pg -> upcast pg.["ShowInPageList"].Eq(true)))
                                            .OrderBy("Title")
                                            .Pluck("Title", "Permalink")
                                            .CoerceTo("array")))
    .RunCursorAsync<WebLog>(conn)
  |> await
  |> Seq.tryHead

/// Get counts for the admin dashboard
let findDashboardCounts conn (webLogId : string) =
  r.Expr(  r.HashMap("Pages",      r.Table(Table.Page    ).GetAll(webLogId).OptArg("index", "WebLogId").Count()))
    .Merge(r.HashMap("Posts",      r.Table(Table.Post    ).GetAll(webLogId).OptArg("index", "WebLogId").Count()))
    .Merge(r.HashMap("Categories", r.Table(Table.Category).GetAll(webLogId).OptArg("index", "WebLogId").Count()))
    .RunAtomAsync<DashboardCounts>(conn)
  |> await
    