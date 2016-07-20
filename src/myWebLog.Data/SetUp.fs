module myWebLog.Data.SetUp

open Rethink
open RethinkDb.Driver.Ast
open System

let private r = RethinkDb.Driver.RethinkDB.R
let private logStep      step = Console.Out.WriteLine (sprintf "[myWebLog] %s"    step)
let private logStepStart text = Console.Out.Write     (sprintf "[myWebLog] %s..." text)
let private logStepDone  ()   = Console.Out.WriteLine (" done.")

/// Ensure the myWebLog database exists
let checkDatabase (cfg : DataConfig) =
  logStep "|> Checking database"
  let dbs = r.DbList().RunListAsync<string>(cfg.conn) |> await
  match dbs.Contains cfg.database with
  | true -> ()
  | _    -> logStepStart (sprintf "  %s database not found - creating" cfg.database)
            r.DbCreate(cfg.database).RunResultAsync(cfg.conn) |> await |> ignore
            logStepDone ()

/// Ensure all required tables exist
let checkTables cfg =
  logStep "|> Checking tables"
  let tables = r.Db(cfg.database).TableList().RunListAsync<string>(cfg.conn) |> await
  [ Table.Category; Table.Comment; Table.Page; Table.Post; Table.User; Table.WebLog ]
  |> List.map (fun tbl -> match tables.Contains tbl with
                          | true -> None
                          | _    -> Some (tbl, r.TableCreate tbl))
  |> List.filter (fun create -> create.IsSome)
  |> List.map    (fun create -> create.Value)
  |> List.iter   (fun (tbl, create) -> logStepStart (sprintf "  Creating table %s" tbl)
                                       create.RunResultAsync(cfg.conn) |> await |> ignore
                                       logStepDone ())

/// Shorthand to get the table
let tbl cfg table = r.Db(cfg.database).Table(table)

/// Create the given index
let createIndex cfg table (index : string * obj) =
  logStepStart (sprintf """  Creating index "%s" on table %s""" (fst index) table)
  (tbl cfg table).IndexCreate(fst index, snd index).RunResultAsync(cfg.conn) |> await |> ignore
  (tbl cfg table).IndexWait(fst index).RunAtomAsync(cfg.conn)                |> await |> ignore
  logStepDone ()

/// Ensure that the given indexes exist, and create them if required
let ensureIndexes cfg (indexes : (string * (string * obj) list) list) =
  indexes
  |> List.iter (fun tabl -> let idx = (tbl cfg (fst tabl)).IndexList().RunListAsync<string>(cfg.conn) |> await
                            snd tabl
                            |> List.iter (fun index -> match idx.Contains (fst index) with
                                                       | true -> ()
                                                       | _    -> createIndex cfg (fst tabl) index))

/// Create an index on a single field
let singleField (name : string) : obj = upcast (fun row -> (row :> ReqlExpr).[name])

/// Create an index on web log Id and the given field
let webLogField (name : string) : obj = upcast (fun row -> r.Array((row :> ReqlExpr).["webLogId"], row.[name]))

/// Ensure all the required indexes exist
let checkIndexes cfg =
  logStep "|> Checking indexes"
  [ Table.Category, [ "webLogId", singleField "webLogId"
                      "slug",     webLogField "slug"
                    ]
    Table.Comment, [ "postId", singleField "postId"
                   ]
    Table.Page, [ "webLogId",  singleField "webLogId"
                  "permalink", webLogField "permalink"
                  "pageList",  webLogField "showInPageList"
                ]
    Table.Post, [ "webLogId",        singleField "webLogId"
                  "webLogAndStatus", webLogField "status"
                  "permalink",       webLogField "permalink"
                ]
    Table.User, [ "logOn", upcast (fun row -> r.Array((row :> ReqlExpr).["userName"], row.["passwordHash"]))
                ]
    Table.WebLog, [ "urlBase", singleField "urlBase"
                  ]
  ]
  |> ensureIndexes cfg

/// Start up checks to ensure the database, tables, and indexes exist
let startUpCheck cfg =
  logStep "Database Start Up Checks Starting"
  checkDatabase cfg
  checkTables   cfg
  checkIndexes  cfg
  logStep "Database Start Up Checks Complete"
