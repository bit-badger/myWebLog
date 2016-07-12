module myWebLog.Data.Category

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open System.Dynamic

/// Shorthand to get a category by Id and filter by web log Id
let private category webLogId catId =
  table Table.Category
  |> get catId
  |> filter (fun c -> upcast c.["webLogId"].Eq(webLogId))

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

/// Count categories for a web log
let countCategories conn webLogId =
  table Table.Category
  |> getAll [| webLogId |]
  |> optArg "index" "webLogId"
  |> count
  |> runAtomAsync<int> conn

/// Get a specific category by its Id
let tryFindCategory conn webLogId catId : Category option =
  match category webLogId catId
        |> runAtomAsync<Category> conn
        |> box with
  | null -> None
  | cat  -> Some <| unbox cat

/// Save a category
let saveCategory conn webLogId (cat : Category) =
  match cat.id with
  | "new" -> let newCat = { cat with id       = string <| System.Guid.NewGuid()
                                     webLogId = webLogId }
             table Table.Category
             |> insert newCat
             |> runResultAsync conn
             |> ignore
             newCat.id
  | _     -> let upd8 = ExpandoObject()
             upd8?name        <- cat.name
             upd8?slug        <- cat.slug
             upd8?description <- cat.description
             upd8?parentId    <- cat.parentId
             category webLogId cat.id
             |> update upd8
             |> runResultAsync conn
             |> ignore
             cat.id

/// Remove a category from a given parent
let removeCategoryFromParent conn webLogId parentId catId =
  match tryFindCategory conn webLogId parentId with
  | Some parent -> let upd8 = ExpandoObject()
                   upd8?children <- parent.children
                                    |> List.filter (fun ch -> ch <> catId)
                   category webLogId parentId
                   |> update upd8
                   |> runResultAsync conn
                   |> ignore
  | None        -> ()

/// Add a category to a given parent
let addCategoryToParent conn webLogId parentId catId =
  match tryFindCategory conn webLogId parentId with
  | Some parent -> let upd8 = ExpandoObject()
                   upd8?children <- catId :: parent.children
                   category webLogId parentId
                   |> update upd8
                   |> runResultAsync conn
                   |> ignore
  | None        -> ()

/// Delete a category
let deleteCategory conn cat =
  // Remove the category from its parent
  match cat.parentId with
  | Some parentId -> removeCategoryFromParent conn cat.webLogId parentId cat.id
  | None          -> ()
  // Move this category's children to its parent
  let newParent = ExpandoObject()
  newParent?parentId <- cat.parentId
  cat.children
  |> List.iter (fun childId -> category cat.webLogId childId
                               |> update newParent
                               |> runResultAsync conn
                               |> ignore)
  // Remove the category from posts where it is assigned
  table Table.Post
  |> getAll [| cat.webLogId |]
  |> optArg "index" "webLogId"
  |> filter (fun p -> upcast p.["categoryIds"].Contains(cat.id))
  |> runCursorAsync<Post> conn
  |> Seq.toList
  |> List.iter (fun post -> let newCats = ExpandoObject()
                            newCats?categoryIds <- post.categoryIds
                                                   |> List.filter (fun c -> c <> cat.id)
                            table Table.Post
                            |> get post.id
                            |> update newCats
                            |> runResultAsync conn
                            |> ignore)
  // Now, delete the category
  table Table.Category
  |> get cat.id
  |> delete
  |> runResultAsync conn
  |> ignore
