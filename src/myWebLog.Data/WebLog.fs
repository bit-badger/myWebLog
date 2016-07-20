module myWebLog.Data.WebLog

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

type PageList = { pageList : obj }

/// Detemine the web log by the URL base
let tryFindWebLogByUrlBase conn (urlBase : string) =
  r.Table(Table.WebLog)
    .GetAll(urlBase).OptArg("index", "urlBase")
    .Merge(fun webLog -> { pageList =
                             r.Table(Table.Page)
                              .GetAll(webLog.["id"], true).OptArg("index", "pageList")
                              .OrderBy("title")
                              .Pluck("title", "permalink")
                              .CoerceTo("array") })
    .RunCursorAsync<WebLog>(conn)
  |> await
  |> Seq.tryHead
