module myWebLog.Data.Page

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open System.Dynamic

/// Shorthand to get the page by its Id, filtering on web log Id
let private page webLogId pageId =
  table Table.Page
  |> get pageId
  |> filter (fun p -> upcast p.["webLogId"].Eq(webLogId))

/// Get a page by its Id
let tryFindPage conn webLogId pageId : Page option =
  match page webLogId pageId
        |> runAtomAsync<Page> conn
        |> box with
  | null -> None
  | page -> Some <| unbox page

/// Get a page by its Id (excluding revisions)
let tryFindPageWithoutRevisions conn webLogId pageId : Page option =
  match page webLogId pageId
        |> without [| "revisions" |]
        |> runAtomAsync<Page> conn
        |> box with
  | null -> None
  | page -> Some <| unbox page

/// Find a page by its permalink
let tryFindPageByPermalink conn webLogId permalink =
  table Table.Page
  |> getAll [| webLogId, permalink |]
  |> optArg "index" "permalink"
  |> without [| "revisions" |]
  |> runCursorAsync<Page> conn
  |> Seq.tryHead

/// Count pages for a web log
let countPages conn webLogId =
  table Table.Page
  |> getAll [| webLogId |]
  |> optArg "index" "webLogId"
  |> count
  |> runAtomAsync<int> conn

/// Get a list of all pages (excludes page text and revisions)
let findAllPages conn webLogId =
  table Table.Page
  |> getAll [| webLogId |]
  |> orderBy (fun p -> upcast p.["title"])
  |> without [| "text"; "revisions" |]
  |> runCursorAsync<Page> conn
  |> Seq.toList

/// Save a page
let savePage conn (pg : Page) =
  match pg.id with
  | "new" -> let newPage = { pg with id = string <| System.Guid.NewGuid() }
             table Table.Page
             |> insert page
             |> runResultAsync conn
             |> ignore
             newPage.id
  | _     -> let upd8 = ExpandoObject()
             upd8?title         <- pg.title
             upd8?permalink     <- pg.permalink
             upd8?publishedOn   <- pg.publishedOn
             upd8?lastUpdatedOn <- pg.lastUpdatedOn
             upd8?text          <- pg.text
             upd8?revisions     <- pg.revisions
             page pg.webLogId pg.id
             |> update upd8
             |> runResultAsync conn
             |> ignore
             pg.id

/// Delete a page
let deletePage conn webLogId pageId =
  page webLogId pageId
  |> delete
  |> runResultAsync conn
  |> ignore
