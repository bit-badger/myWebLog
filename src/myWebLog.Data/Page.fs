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