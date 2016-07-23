module myWebLog.Data.WebLog

open myWebLog.Entities
open Rethink

let private r = RethinkDb.Driver.RethinkDB.R

/// Counts of items displayed on the admin dashboard
type DashboardCounts = {
  /// The number of pages for the web log
  pages : int
  /// The number of pages for the web log
  posts : int
  /// The number of categories for the web log
  categories : int
  }

/// Detemine the web log by the URL base
// TODO: see if we can make .Merge work for page list even though the attribute is ignored
//       (needs to be ignored for serialization, but included for deserialization)
let tryFindWebLogByUrlBase conn (urlBase : string) =
  let webLog = r.Table(Table.WebLog)
                .GetAll(urlBase).OptArg("index", "urlBase")
                .RunCursorAsync<WebLog>(conn)
              |> await
              |> Seq.tryHead
  match webLog with
  | Some w -> Some { w with pageList = r.Table(Table.Page)
                                        .GetAll(w.id).OptArg("index", "webLogId")
                                        .Filter(fun pg -> pg.["showInPageList"].Eq(true))
                                        .OrderBy("title")
                                        .Pluck("title", "permalink")
                                        .RunListAsync<PageListEntry>(conn) |> await |> Seq.toList }
  | None   -> None

/// Get counts for the admin dashboard
let findDashboardCounts conn (webLogId : string) =
  r.Expr(  r.HashMap("pages",      r.Table(Table.Page    ).GetAll(webLogId).OptArg("index", "webLogId").Count()))
    .Merge(r.HashMap("posts",      r.Table(Table.Post    ).GetAll(webLogId).OptArg("index", "webLogId").Count()))
    .Merge(r.HashMap("categories", r.Table(Table.Category).GetAll(webLogId).OptArg("index", "webLogId").Count()))
    .RunAtomAsync<DashboardCounts>(conn)
  |> await
    