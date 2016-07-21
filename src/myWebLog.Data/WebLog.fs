module myWebLog.Data.WebLog

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

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
