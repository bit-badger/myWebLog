module myWebLog.Data.Rethink

open RethinkDb.Driver.Ast
open RethinkDb.Driver.Net

let await task = task |> Async.AwaitTask |> Async.RunSynchronously

type ReqlExpr with
  /// Run a SUCCESS_ATOM response that returns multiple values
  member this.RunListAsync<'T> (conn : IConnection) = this.RunAtomAsync<System.Collections.Generic.List<'T>> conn
