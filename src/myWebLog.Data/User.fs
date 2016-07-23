module myWebLog.Data.User

open myWebLog.Entities
open Rethink

let private r = RethinkDb.Driver.RethinkDB.R

/// Log on a user
// NOTE: The significant length of a RethinkDB index is 238 - [PK size]; as we're storing 1,024 characters of password,
//       including it in an index does not get any performance gain, and would unnecessarily bloat the index. See
//       http://rethinkdb.com/docs/secondary-indexes/java/ for more information.
let tryUserLogOn conn (email : string) (passwordHash : string) =
  r.Table(Table.User)
    .GetAll(email).OptArg("index", "userName")
    .Filter(fun u -> u.["passwordHash"].Eq(passwordHash))
    .RunCursorAsync<User>(conn)
  |> await
  |> Seq.tryHead
