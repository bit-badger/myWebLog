module MyWebLog.Data.RethinkDB.User

open MyWebLog.Entities
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Log on a user
// NOTE: The significant length of a RethinkDB index is 238 - [PK size]; as we're storing 1,024 characters of password,
//       including it in an index does not get any performance gain, and would unnecessarily bloat the index. See
//       http://rethinkdb.com/docs/secondary-indexes/java/ for more information.
let tryUserLogOn conn (email : string) (passwordHash : string) =
  async {
    let! user =
      r.Table(Table.User)
        .GetAll(email).OptArg("index", "UserName")
        .Filter(ReqlFunction1(fun u -> upcast u.["PasswordHash"].Eq(passwordHash)))
        .RunResultAsync<User list> conn
    return user |> List.tryHead
    }
  |> Async.RunSynchronously
