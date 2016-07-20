module myWebLog.Data.Page

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to get the page by its Id, filtering on web log Id
let private page (webLogId : string) (pageId : string) =
  r.Table(Table.Page)
    .Get(pageId)
    .Filter(fun p -> p.["webLogId"].Eq(webLogId))

/// Get a page by its Id
let tryFindPage conn webLogId pageId : Page option =
  match (page webLogId pageId)
          .RunAtomAsync<Page>(conn) |> await |> box with
  | null -> None
  | page -> Some <| unbox page

/// Get a page by its Id (excluding revisions)
let tryFindPageWithoutRevisions conn webLogId pageId : Page option =
  match (page webLogId pageId)
          .Without("revisions")
          .RunAtomAsync<Page>(conn) |> await |> box with
  | null -> None
  | page -> Some <| unbox page

/// Find a page by its permalink
let tryFindPageByPermalink conn (webLogId : string) (permalink : string) =
  r.Table(Table.Page)
    .GetAll(webLogId, permalink).OptArg("index", "permalink")
    .Without("revisions")
    .RunCursorAsync<Page>(conn)
  |> await
  |> Seq.tryHead

/// Count pages for a web log
let countPages conn (webLogId : string) =
  r.Table(Table.Page)
    .GetAll(webLogId).OptArg("index", "webLogId")
    .Count()
    .RunAtomAsync<int>(conn) |> await

/// Get a list of all pages (excludes page text and revisions)
let findAllPages conn (webLogId : string) =
  r.Table(Table.Page)
    .GetAll(webLogId)
    .OrderBy("title")
    .Without("text", "revisions")
    .RunCursorAsync<Page>(conn)
  |> await
  |> Seq.toList

/// Save a page
let savePage conn (pg : Page) =
  match pg.id with
  | "new" -> let newPage = { pg with id = string <| System.Guid.NewGuid() }
             r.Table(Table.Page)
               .Insert(page)
               .RunResultAsync(conn) |> await |> ignore
             newPage.id
  | _     -> let upd8 = ExpandoObject()
             upd8?title       <- pg.title
             upd8?permalink   <- pg.permalink
             upd8?publishedOn <- pg.publishedOn
             upd8?updatedOn   <- pg.updatedOn
             upd8?text        <- pg.text
             upd8?revisions   <- pg.revisions
             (page pg.webLogId pg.id)
               .Update(upd8)
               .RunResultAsync(conn) |> await |> ignore
             pg.id

/// Delete a page
let deletePage conn webLogId pageId =
  (page webLogId pageId)
    .Delete()
    .RunResultAsync(conn) |> await |> ignore
