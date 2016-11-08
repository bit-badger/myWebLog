module MyWebLog.Data.RethinkDB.WebLog

open MyWebLog.Entities
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Detemine the web log by the URL base
let tryFindWebLogByUrlBase conn (urlBase : string) =
  async {
    let! cursor = 
      r.Table(Table.WebLog)
        .GetAll(urlBase).OptArg("index", "UrlBase")
        .Merge(ReqlFunction1(fun w ->
          upcast r.HashMap("PageList", r.Table(Table.Page)
                                        .GetAll(w.G("id")).OptArg("index", "WebLogId")
                                        .Filter(ReqlFunction1(fun pg -> upcast pg.["ShowInPageList"].Eq(true)))
                                        .OrderBy("Title")
                                        .Pluck("Title", "Permalink")
                                        .CoerceTo("array"))))
        .RunCursorAsync<WebLog> conn
    return cursor |> Seq.tryHead
    }
  |> Async.RunSynchronously
  
/// Get counts for the admin dashboard
let findDashboardCounts conn (webLogId : string) =
  async {
    return!
      r.Expr(  r.HashMap("Pages",      r.Table(Table.Page    ).GetAll(webLogId).OptArg("index", "WebLogId").Count()))
        .Merge(r.HashMap("Posts",      r.Table(Table.Post    ).GetAll(webLogId).OptArg("index", "WebLogId").Count()))
        .Merge(r.HashMap("Categories", r.Table(Table.Category).GetAll(webLogId).OptArg("index", "WebLogId").Count()))
        .RunResultAsync<DashboardCounts> conn
    }
  |> Async.RunSynchronously
    