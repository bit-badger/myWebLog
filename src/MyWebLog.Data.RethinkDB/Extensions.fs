[<AutoOpen>]
module MyWebLog.Data.RethinkDB.Extensions

open RethinkDb.Driver.Ast
open RethinkDb.Driver.Net

let await task = task |> Async.AwaitTask |> Async.RunSynchronously
