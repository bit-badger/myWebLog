module myWebLog.Data.SetUp

open RethinkDb.Driver
open System
open Rethink
open RethinkDb.Driver.Ast

let private r = RethinkDB.R
let private logStep      step = Console.Out.WriteLine(sprintf "[myWebLog] %s"    step)
let private logStepStart text = Console.Out.Write    (sprintf "[myWebLog] %s..." text)
let private logStepDone  ()   = Console.Out.WriteLine(" done.")

let private result task = task |> Async.AwaitTask |> Async.RunSynchronously

let checkDatabase (cfg : DataConfig) =
  logStep "|> Checking database"
  let dbs = r.DbList()
            |> runListAsync<string> cfg.conn
  match dbs.Contains cfg.database with
  | true -> ()
  | _    -> logStepStart (sprintf "  %s database not found - creating" cfg.database)
            r.DbCreate cfg.database
            |> runResultAsync cfg.conn
            |> ignore
            logStepDone ()

let checkTables cfg =
  logStep "|> Checking tables"
  let tables = r.Db(cfg.database).TableList()
               |> runListAsync<string> cfg.conn
  [ Table.Category; Table.Comment; Table.Page; Table.Post; Table.User; Table.WebLog ]
  |> List.map (fun tbl -> match tables.Contains tbl with
                          | true -> None
                          | _    -> Some (tbl, r.TableCreate tbl))
  |> List.filter (fun create -> create.IsSome)
  |> List.map    (fun create -> create.Value)
  |> List.iter   (fun (tbl, create) -> logStepStart (sprintf "  Creating table %s" tbl)
                                       create
                                       |> runResultAsync cfg.conn
                                       |> ignore
                                       logStepDone ())

let tbl cfg table = r.Db(cfg.database).Table(table)

let createIndex cfg table index =
  logStepStart (sprintf """  Creating index "%s" on table %s""" index table)
  (tbl cfg table).IndexCreate(index)
  |> runResultAsync cfg.conn
  |> ignore
  (tbl cfg table).IndexWait(index)
  |> runResultAsync cfg.conn
  |> ignore
  logStepDone ()

let chkIndexes cfg table (indexes : string list) =
  let idx = (tbl cfg table).IndexList()
            |> runListAsync<string> cfg.conn
  indexes
  |> List.iter (fun index -> match idx.Contains index with
                             | true -> ()
                             | _    -> createIndex cfg table index)
  idx

let checkCategoryIndexes cfg =
  chkIndexes cfg Table.Category [ "webLogId"; "slug" ]
  |> ignore

let checkCommentIndexes cfg =
  chkIndexes cfg Table.Comment  [ "postId" ]
  |> ignore

let checkPageIndexes cfg =
  let idx = chkIndexes cfg Table.Page [ "webLogId" ]
  match idx.Contains "permalink" with
  | true -> ()
  | _    -> logStepStart (sprintf """  Creating index "permalink" on table %s""" Table.Page)
            (tbl cfg Table.Page)
              .IndexCreate("permalink", ReqlFunction1(fun row -> upcast r.Array(row.["webLogId"], row.["permalink"])))
            |> runResultAsync cfg.conn
            |> ignore
            (tbl cfg Table.Page).IndexWait "permalink"
            |> runResultAsync cfg.conn
            |> ignore
            logStepDone ()
  match idx.Contains "pageList" with
  | true -> ()
  | _    -> logStepStart (sprintf """  Creating index "pageList" on table %s""" Table.Page)
            (tbl cfg Table.Page)
              .IndexCreate("pageList", ReqlFunction1(fun row -> upcast r.Array(row.["webLogId"], row.["showInPageList"])))
            |> runResultAsync cfg.conn
            |> ignore
            (tbl cfg Table.Page).IndexWait "pageList"
            |> runResultAsync cfg.conn
            |> ignore
            logStepDone ()

let checkPostIndexes cfg =
  let idx = chkIndexes cfg Table.Post [ "webLogId" ]
  match idx.Contains "webLogAndStatus" with
  | true -> ()
  | _    -> logStepStart (sprintf """  Creating index "webLogAndStatus" on table %s""" Table.Post)
            (tbl cfg Table.Post)
              .IndexCreate("webLogAndStatus", ReqlFunction1(fun row -> upcast r.Array(row.["webLogId"], row.["status"])))
            |> runResultAsync cfg.conn
            |> ignore
            (tbl cfg Table.Post).IndexWait "webLogAndStatus"
            |> runResultAsync cfg.conn
            |> ignore
            logStepDone ()
  match idx.Contains "permalink" with
  | true -> ()
  | _    -> logStepStart (sprintf """  Creating index "permalink" on table %s""" Table.Post)
            (tbl cfg Table.Post)
              .IndexCreate("permalink", ReqlFunction1(fun row -> upcast r.Array(row.["webLogId"], row.["permalink"])))
            |> runResultAsync cfg.conn
            |> ignore
            (tbl cfg Table.Post).IndexWait "permalink"
            |> runResultAsync cfg.conn
            |> ignore
            logStepDone ()

let checkUserIndexes cfg =
  let idx = chkIndexes cfg Table.User [ ]
  match idx.Contains "logOn" with
  | true -> ()
  | _    -> logStepStart (sprintf """  Creating index "logOn" on table %s""" Table.User)
            (tbl cfg Table.User)
              .IndexCreate("logOn", ReqlFunction1(fun row -> upcast r.Array(row.["userName"], row.["passwordHash"])))
            |> runResultAsync cfg.conn
            |> ignore
            (tbl cfg Table.User).IndexWait "logOn"
            |> runResultAsync cfg.conn
            |> ignore
            logStepDone ()

let checkWebLogIndexes cfg =
  chkIndexes cfg Table.WebLog [ "urlBase" ]
  |> ignore

let checkIndexes cfg =
  logStep "|> Checking indexes"
  checkCategoryIndexes cfg
  checkCommentIndexes  cfg
  checkPageIndexes     cfg
  checkPostIndexes     cfg
  checkUserIndexes     cfg
  checkWebLogIndexes   cfg

let startUpCheck cfg =
  logStep "Database Start Up Checks Starting"
  checkDatabase cfg
  checkTables   cfg
  checkIndexes  cfg
  logStep "Database Start Up Checks Complete"
