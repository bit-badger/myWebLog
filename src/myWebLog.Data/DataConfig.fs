namespace myWebLog.Data

open RethinkDb.Driver
open RethinkDb.Driver.Net
open Newtonsoft.Json

/// Data configuration
type DataConfig = {
  /// The hostname for the RethinkDB server
  hostname : string
  /// The port for the RethinkDB server
  port : int
  /// The authorization key to use when connecting to the server
  authKey : string
  /// How long an attempt to connect to the server should wait before giving up
  timeout : int
  /// The name of the default database to use on the connection
  database : string
  /// A connection to the RethinkDB server using the configuration in this object
  conn : IConnection
  }
with
  /// Create a data configuration from JSON
  static member fromJson json =
    let mutable cfg = JsonConvert.DeserializeObject<DataConfig> json
    cfg <- match cfg.hostname with
           | null -> { cfg with hostname = RethinkDBConstants.DefaultHostname }
           | _    -> cfg
    cfg <- match cfg.port with
           | 0 -> { cfg with port = RethinkDBConstants.DefaultPort }
           | _ -> cfg
    cfg <- match cfg.authKey with
           | null -> { cfg with authKey = RethinkDBConstants.DefaultAuthkey }
           | _    -> cfg
    cfg <- match cfg.timeout with
           | 0 -> { cfg with timeout = RethinkDBConstants.DefaultTimeout }
           | _ -> cfg
    cfg <- match cfg.database with
           | null -> { cfg with database = RethinkDBConstants.DefaultDbName }
           | _    -> cfg
    { cfg with conn = RethinkDB.R.Connection()
                        .Hostname(cfg.hostname)
                        .Port(cfg.port)
                        .AuthKey(cfg.authKey)
                        .Db(cfg.database)
                        .Timeout(cfg.timeout)
                        .Connect() }
