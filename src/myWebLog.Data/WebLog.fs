module myWebLog.Data.WebLog

open myWebLog.Entities
open Rethink
open RethinkDb.Driver

let private r = RethinkDB.R

type PageList = { pageList : Ast.CoerceTo }

/// Detemine the web log by the URL base
let tryFindWebLogByUrlBase (cfg : DataConfig) (urlBase : string) =
  r.Table(Table.WebLog).GetAll([| urlBase |]).OptArg("index", "urlBase")
    .Merge(fun webLog -> { pageList = r.Table(Table.Page)
                                        .GetAll([| webLog.["id"], true |]).OptArg("index", "pageList")
                                        .OrderBy("title")
                                        .Pluck([| "title", "permalink" |])
                                        .CoerceTo("array") })
  |> runCursorAsync<WebLog> cfg.conn
  |> Seq.tryHead
