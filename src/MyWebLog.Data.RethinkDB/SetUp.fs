module MyWebLog.Data.RethinkDB.SetUp

open RethinkDb.Driver.Ast
open System

let private r = RethinkDb.Driver.RethinkDB.R
let private logStep      step = Console.Out.WriteLine (sprintf "[myWebLog] %s"    step)
let private logStepStart text = Console.Out.Write     (sprintf "[myWebLog] %s..." text)
let private logStepDone  ()   = Console.Out.WriteLine (" done.")

/// Ensure the myWebLog database exists
let private checkDatabase (cfg : DataConfig) =
  logStep "|> Checking database"
  let dbs = r.DbList().RunResultAsync<string list>(cfg.Conn) |> await
  match List.contains cfg.Database dbs with
  | true -> ()
  | _ -> logStepStart (sprintf "  %s database not found - creating" cfg.Database)
         r.DbCreate(cfg.Database).RunResultAsync(cfg.Conn) |> await |> ignore
         logStepDone ()

/// Ensure all required tables exist
let private checkTables cfg =
  logStep "|> Checking tables"
  let tables = r.Db(cfg.Database).TableList().RunResultAsync<string list>(cfg.Conn) |> await
  [ Table.Category; Table.Comment; Table.Page; Table.Post; Table.User; Table.WebLog ]
  |> List.filter (fun tbl -> not (List.contains tbl tables))
  |> List.iter   (fun tbl -> logStepStart (sprintf "  Creating table %s" tbl)
                             (r.TableCreate tbl).RunResultAsync(cfg.Conn) |> await |> ignore
                             logStepDone ())

/// Shorthand to get the table
let private tbl cfg table = r.Db(cfg.Database).Table(table)

/// Create the given index
let private createIndex cfg table (index : string * (ReqlExpr -> obj) option) =
  let idxName, idxFunc = index
  logStepStart (sprintf """  Creating index "%s" on table %s""" idxName table)
  (match idxFunc with
   | Some f -> (tbl cfg table).IndexCreate(idxName, f)
   | None -> (tbl cfg table).IndexCreate(idxName))
    .RunResultAsync(cfg.Conn)
  |> await |> ignore
  (tbl cfg table).IndexWait(idxName).RunResultAsync(cfg.Conn) |> await |> ignore
  logStepDone ()

/// Ensure that the given indexes exist, and create them if required
let private ensureIndexes cfg (indexes : (string * (string * (ReqlExpr -> obj) option) list) list) =
  let ensureForTable (tblName, idxs) =
    let idx = (tbl cfg tblName).IndexList().RunResultAsync<string list>(cfg.Conn) |> await
    idxs
    |> List.iter (fun index -> match List.contains (fst index) idx with true -> () | _ -> createIndex cfg tblName index)
  indexes
  |> List.iter ensureForTable

/// Create an index on web log Id and the given field
let private webLogField (name : string) : (ReqlExpr -> obj) option =
  Some <| fun row -> upcast r.Array(row.["WebLogId"], row.[name])

/// Ensure all the required indexes exist
let private checkIndexes cfg =
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
