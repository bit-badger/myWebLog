module myWebLog.Data.User

open myWebLog.Entities
open Rethink

let private r = RethinkDb.Driver.RethinkDB.R

/// Log on a user
// FIXME: the password hash may be longer than the significant size of a RethinkDB index
let tryUserLogOn conn (email : string) (passwordHash : string) =
  r.Table(Table.User)
    .GetAll(email, passwordHash).OptArg("index", "logOn")
    .RunCursorAsync<User>(conn)
  |> await
  |> Seq.tryHead
