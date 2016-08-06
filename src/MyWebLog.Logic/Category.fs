module MyWebLog.Logic.Category

open MyWebLog.Data
open MyWebLog.Entities

/// Sort categories by their name, with their children sorted below them, including an indent level
let sortCategories categories =
  let rec getChildren (cat : Category) indent =
    seq {
      yield cat, indent
      for child in categories |> List.filter (fun c -> c.ParentId = Some cat.Id) do
        yield! getChildren child (indent + 1)
      }
  categories
  |> List.filter (fun c -> c.ParentId.IsNone)
  |> List.map    (fun c -> getChildren c 0)
  |> Seq.collect id
  |> Seq.toList

/// Find all categories for a given web log
let findAllCategories (data : IMyWebLogData) webLogId =
  data.AllCategories webLogId
  |> sortCategories

/// Try to find a category for a given web log Id and category Id
let tryFindCategory (data : IMyWebLogData) webLogId catId = data.CategoryById webLogId catId

/// Try to find a category by its slug for a given web log
let tryFindCategoryBySlug (data : IMyWebLogData) webLogId slug = data.CategoryBySlug webLogId slug

/// Save a category
let saveCategory (data : IMyWebLogData) (cat : Category) =
  match cat.Id with
  | "new" -> let newCat = { cat with Id = string <| System.Guid.NewGuid() }
             data.AddCategory newCat
             newCat.Id
  | _ -> data.UpdateCategory cat
         cat.Id

/// Remove a category from its parent
let removeCategoryFromParent (data : IMyWebLogData) webLogId parentId catId =
  match tryFindCategory data webLogId parentId with
  | Some parent -> parent.Children
                   |> List.filter (fun childId -> childId <> catId) 
                   |> data.UpdateChildren webLogId parentId
  | None -> ()

/// Add a category to a given parent
let addCategoryToParent (data : IMyWebLogData) webLogId parentId catId =
  match tryFindCategory data webLogId parentId with
  | Some parent -> catId :: parent.Children
                   |> data.UpdateChildren webLogId parentId
  | None -> ()

/// Delete a category
let deleteCategory (data : IMyWebLogData) cat = data.DeleteCategory cat
