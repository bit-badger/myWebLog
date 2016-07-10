module myWebLog.Data.Category

open myWebLog.Entities
open Rethink

/// Sort categories by their name, with their children sorted below them, including an indent level
let sortCategories categories =
  let rec getChildren (cat : Category) indent =
    seq {
      yield cat, indent
      for child in categories |> List.filter (fun c -> c.parentId = Some cat.id) do
        yield! getChildren child (indent + 1)
      }
  categories
  |> List.filter (fun c -> c.parentId.IsNone)
  |> List.map    (fun c -> getChildren c 0)
  |> Seq.collect id
  |> Seq.toList

/// Get all categories for a web log
let getAllCategories conn webLogId =
  table Table.Category
  |> getAll [| webLogId |]
  |> optArg "index" "webLogId"
  |> orderBy (fun c -> upcast c.["name"])
  |> runCursorAsync<Category> conn
  |> Seq.toList
  |> sortCategories
