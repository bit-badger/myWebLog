module myWebLog.Data.Page

open myWebLog.Entities
open Rethink

/// Get a page by its Id
let tryFindPage conn webLogId pageId : Page option =
  match table Table.Page
        |> get pageId
        |> filter (fun p -> upcast p.["webLogId"].Eq(webLogId))
        |> runAtomAsync<Page> conn
        |> box with
  | null -> None
  | page -> Some <| unbox page

/// Get a page by its Id (excluding revisions)
let tryFindPageWithoutRevisions conn webLogId pageId : Page option =
  match table Table.Page
        |> get pageId
        |> filter (fun p -> upcast p.["webLogId"].Eq(webLogId))
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
