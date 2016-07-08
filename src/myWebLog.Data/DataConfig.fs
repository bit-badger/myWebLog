namespace myWebLog.Data

open RethinkDb.Driver.Net

type DataConfig = {
  database : string
  conn : IConnection
  }

