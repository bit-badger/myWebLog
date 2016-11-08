module MyWebLog.Data.RethinkDB.SetUp

open RethinkDb.Driver.Ast
open System

let private r = RethinkDb.Driver.RethinkDB.R
let private logStep      step = Console.Out.WriteLine (sprintf "[myWebLog] %s"    step)
let private logStepStart text = Console.Out.Write     (sprintf "[myWebLog] %s..." text)
let private logStepDone  ()   = Console.Out.WriteLine (" done.")

/// Ensure the myWebLog database exists
let private checkDatabase (cfg : DataConfig) =
  async {
    logStep "|> Checking database"
    let! dbs = r.DbList().RunResultAsync<string list> cfg.Conn
    match List.contains cfg.Database dbs with
    | true -> ()
    | _ -> logStepStart (sprintf "  %s database not found - creating" cfg.Database)
           do! r.DbCreate(cfg.Database).RunResultAsync cfg.Conn
           logStepDone ()
    }
    

/// Ensure all required tables exist
let private checkTables cfg =
  async {
    logStep "|> Checking tables"
    let! tables = r.Db(cfg.Database).TableList().RunResultAsync<string list> cfg.Conn
    [ Table.Category; Table.Comment; Table.Page; Table.Post; Table.User; Table.WebLog ]
    |> List.filter (fun tbl -> not (List.contains tbl tables))
    |> List.iter   (fun tbl -> logStepStart (sprintf "  Creating table %s" tbl)
                               async { do! (r.TableCreate tbl).RunResultAsync cfg.Conn } |> Async.RunSynchronously
                               logStepDone ())
    }

/// Shorthand to get the table
let private tbl cfg table = r.Db(cfg.Database).Table table

/// Create the given index
let private createIndex cfg table (index : string * (ReqlExpr -> obj) option) =
  async {
    let idxName, idxFunc = index
    logStepStart (sprintf """  Creating index "%s" on table %s""" idxName table)
    do! (match idxFunc with
         | Some f -> (tbl cfg table).IndexCreate(idxName, f)
         | None -> (tbl cfg table).IndexCreate(idxName))
           .RunResultAsync cfg.Conn
    do! (tbl cfg table).IndexWait(idxName).RunResultAsync cfg.Conn
    logStepDone ()
    }

/// Ensure that the given indexes exist, and create them if required
let private ensureIndexes cfg (indexes : (string * (string * (ReqlExpr -> obj) option) list) list) =
  let ensureForTable (tblName, idxs) =
    async {
      let! idx = (tbl cfg tblName).IndexList().RunResultAsync<string list> cfg.Conn
      idxs
      |> List.filter (fun (idxName, _) -> not (List.contains idxName idx))
      |> List.map    (fun index -> createIndex cfg tblName index)
      |> List.iter   Async.RunSynchronously
      }
    |> Async.RunSynchronously
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
  async {
    logStep "Database Start Up Checks Starting"
    do! checkDatabase cfg
    do! checkTables   cfg
    checkIndexes cfg
    logStep "Database Start Up Checks Complete"
    }
  |> Async.RunSynchronously
