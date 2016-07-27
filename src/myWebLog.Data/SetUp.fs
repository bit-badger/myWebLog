module MyWebLog.Data.SetUp

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
  let dbs = r.DbList().RunListAsync<string>(cfg.Conn) |> await
  match dbs.Contains cfg.Database with
  | true -> ()
  | _    -> logStepStart (sprintf "  %s database not found - creating" cfg.Database)
            r.DbCreate(cfg.Database).RunResultAsync(cfg.Conn) |> await |> ignore
            logStepDone ()

/// Ensure all required tables exist
let checkTables cfg =
  logStep "|> Checking tables"
  let tables = r.Db(cfg.Database).TableList().RunListAsync<string>(cfg.Conn) |> await
  [ Table.Category; Table.Comment; Table.Page; Table.Post; Table.User; Table.WebLog ]
  |> List.map (fun tbl -> match tables.Contains tbl with
                          | true -> None
                          | _    -> Some (tbl, r.TableCreate tbl))
  |> List.filter (fun create -> create.IsSome)
  |> List.map    (fun create -> create.Value)
  |> List.iter   (fun (tbl, create) -> logStepStart (sprintf "  Creating table %s" tbl)
                                       create.RunResultAsync(cfg.Conn) |> await |> ignore
                                       logStepDone ())

/// Shorthand to get the table
let tbl cfg table = r.Db(cfg.Database).Table(table)

/// Create the given index
let createIndex cfg table (index : string * (ReqlExpr -> obj) option) =
  let idxName, idxFunc = index
  logStepStart (sprintf """  Creating index "%s" on table %s""" idxName table)
  match idxFunc with
  | Some f -> (tbl cfg table).IndexCreate(idxName, f).RunResultAsync(cfg.Conn)
  | None   -> (tbl cfg table).IndexCreate(idxName   ).RunResultAsync(cfg.Conn)
  |> await |> ignore
  (tbl cfg table).IndexWait(idxName).RunAtomAsync(cfg.Conn) |> await |> ignore
  logStepDone ()

/// Ensure that the given indexes exist, and create them if required
let ensureIndexes cfg (indexes : (string * (string * (ReqlExpr -> obj) option) list) list) =
  let ensureForTable tabl =
    let idx = (tbl cfg (fst tabl)).IndexList().RunListAsync<string>(cfg.Conn) |> await
    snd tabl
    |> List.iter (fun index -> match idx.Contains (fst index) with
                                | true -> ()
                                | _    -> createIndex cfg (fst tabl) index)
  indexes
  |> List.iter ensureForTable

/// Create an index on a single field
let singleField (name : string) : obj = upcast (fun row -> (row :> ReqlExpr).[name])

/// Create an index on web log Id and the given field
let webLogField (name : string) : (ReqlExpr -> obj) option =
  Some <| fun row -> upcast r.Array(row.["webLogId"], row.[name])

/// Ensure all the required indexes exist
let checkIndexes cfg =
  logStep "|> Checking indexes"
  [ Table.Category, [ "WebLogId", None
                      "Slug",     webLogField "Slug"
                    ]
    Table.Comment, [ "PostId", None
                   ]
    Table.Page, [ "WebLogId",  None
                  "Permalink", webLogField "Permalink"
                ]
    Table.Post, [ "WebLogId",        None
                  "WebLogAndStatus", webLogField "Status"
                  "Permalink",       webLogField "Permalink"
                ]
    Table.User, [ "UserName", None
                ]
    Table.WebLog, [ "UrlBase", None
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
