module MyWebLog.Data.Page

open FSharp.Interop.Dynamic
open MyWebLog.Entities
open Rethink
open RethinkDb.Driver.Ast
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to get the page by its Id, filtering on web log Id
let private page (webLogId : string) (pageId : string) =
  r.Table(Table.Page)
    .Get(pageId)
    .Filter(ReqlFunction1(fun p -> upcast p.["WebLogId"].Eq(webLogId)))

/// Get a page by its Id
let tryFindPage conn webLogId pageId =
  match r.Table(Table.Page)
          .Get(pageId)
          .RunAtomAsync<Page>(conn) |> await |> box with
  | null -> None
  | page -> let pg : Page = unbox page
            match pg.WebLogId = webLogId with
            | true -> Some pg
            | _    -> None

/// Get a page by its Id (excluding revisions)
let tryFindPageWithoutRevisions conn webLogId pageId : Page option =
  match (page webLogId pageId)
          .Without("Revisions")
          .RunAtomAsync<Page>(conn) |> await |> box with
  | null -> None
  | page -> Some <| unbox page

/// Find a page by its permalink
let tryFindPageByPermalink conn (webLogId : string) (permalink : string) =
  r.Table(Table.Page)
    .GetAll(r.Array(webLogId, permalink)).OptArg("index", "Permalink")
    .Without("Revisions")
    .RunCursorAsync<Page>(conn)
  |> await
  |> Seq.tryHead

/// Get a list of all pages (excludes page text and revisions)
let findAllPages conn (webLogId : string) =
  r.Table(Table.Page)
    .GetAll(webLogId).OptArg("index", "WebLogId")
    .OrderBy("Title")
    .Without("Text", "Revisions")
    .RunListAsync<Page>(conn)
  |> await
  |> Seq.toList

/// Save a page
let savePage conn (pg : Page) =
  match pg.Id with
  | "new" -> let newPage = { pg with Id = string <| System.Guid.NewGuid() }
             r.Table(Table.Page)
               .Insert(page)
               .RunResultAsync(conn) |> await |> ignore
             newPage.Id
  | _     -> let upd8 = ExpandoObject()
             upd8?Title       <- pg.Title
             upd8?Permalink   <- pg.Permalink
             upd8?PublishedOn <- pg.PublishedOn
             upd8?UpdatedOn   <- pg.UpdatedOn
             upd8?Text        <- pg.Text
             upd8?Revisions   <- pg.Revisions
             (page pg.WebLogId pg.Id)
               .Update(upd8)
               .RunResultAsync(conn) |> await |> ignore
             pg.Id

/// Delete a page
let deletePage conn webLogId pageId =
  (page webLogId pageId)
    .Delete()
    .RunResultAsync(conn) |> await |> ignore
