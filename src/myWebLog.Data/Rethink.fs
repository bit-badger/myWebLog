module myWebLog.Data.Rethink

open RethinkDb.Driver.Ast
open RethinkDb.Driver.Net

let private r = RethinkDb.Driver.RethinkDB.R
let private await task = task |> Async.AwaitTask |> Async.RunSynchronously

let delete (expr : ReqlExpr) = expr.Delete ()
let get (expr : obj) (table : Table) = table.Get expr
let getAll (exprs : obj[]) (table : Table) = table.GetAll exprs
let insert (expr : obj) (table : Table) = table.Insert expr
let optArg key (value : obj) (expr : GetAll) = expr.OptArg (key, value)
let orderBy (exprA : obj) (expr : ReqlExpr) = expr.OrderBy exprA
let replace (exprA : obj) (expr : ReqlExpr) = expr.Replace exprA
let runAtomAsync<'T> (conn : IConnection) (ast : ReqlAst) = ast.RunAtomAsync<'T> conn |> await
let runCursorAsync<'T> (conn : IConnection) (ast : ReqlAst) = ast.RunCursorAsync<'T> conn |> await
let runListAsync<'T> (conn : IConnection) (ast : ReqlAst) = ast.RunAtomAsync<System.Collections.Generic.List<'T>> conn
                                                            |> await
let runResultAsync (conn : IConnection) (ast : ReqlAst) = ast.RunResultAsync conn |> await
let table (expr : obj) = r.Table expr
let update (exprA : obj) (expr : ReqlExpr) = expr.Update exprA 
let without (exprs : obj[]) (expr : ReqlExpr) = expr.Without exprs