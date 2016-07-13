module myWebLog.Data.User

open myWebLog.Entities
open Rethink

/// Log on a user
let tryUserLogOn conn email passwordHash =
  table Table.User
  |> getAll [| email, passwordHash |]
  |> optArg "index" "logOn"
  |> runCursorAsync<User> conn
  |> Seq.tryHead
