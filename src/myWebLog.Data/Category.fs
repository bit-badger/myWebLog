module MyWebLog.Data.Category

open FSharp.Interop.Dynamic
open MyWebLog.Entities
open Rethink
open RethinkDb.Driver.Ast
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to get a category by Id and filter by web log Id
let private category (webLogId : string) (catId : string) =
  r.Table(Table.Category)
    .Get(catId)
    .Filter(fun c -> c.["WebLogId"].Eq(webLogId))

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

/// Get all categories for a web log
let getAllCategories conn (webLogId : string) =
  r.Table(Table.Category)
    .GetAll(webLogId).OptArg("index", "WebLogId")
    .OrderBy("Name")
    .RunListAsync<Category>(conn)
  |> await
  |> Seq.toList
  |> sortCategories

/// Get a specific category by its Id
let tryFindCategory conn webLogId catId : Category option =
  match (category webLogId catId)
          .RunAtomAsync<Category>(conn) |> await |> box with
  | null -> None
  | cat  -> Some <| unbox cat

/// Save a category
let saveCategory conn webLogId (cat : Category) =
  match cat.Id with
  | "new" -> let newCat = { cat with Id       = string <| System.Guid.NewGuid()
                                     WebLogId = webLogId }
             r.Table(Table.Category)
               .Insert(newCat)
               .RunResultAsync(conn) |> await |> ignore
             newCat.Id
  | _     -> let upd8 = ExpandoObject()
             upd8?Name        <- cat.Name
             upd8?Slug        <- cat.Slug
             upd8?Description <- cat.Description
             upd8?ParentId    <- cat.ParentId
             (category webLogId cat.Id)
               .Update(upd8)
               .RunResultAsync(conn) |> await |> ignore
             cat.Id

/// Remove a category from a given parent
let removeCategoryFromParent conn webLogId parentId catId =
  match tryFindCategory conn webLogId parentId with
  | Some parent -> let upd8 = ExpandoObject()
                   upd8?Children <- parent.Children
                                    |> List.filter (fun childId -> childId <> catId)
                   (category webLogId parentId)
                     .Update(upd8)
                     .RunResultAsync(conn) |> await |> ignore
  | None        -> ()

/// Add a category to a given parent
let addCategoryToParent conn webLogId parentId catId =
  match tryFindCategory conn webLogId parentId with
  | Some parent -> let upd8 = ExpandoObject()
                   upd8?Children <- catId :: parent.Children
                   (category webLogId parentId)
                     .Update(upd8)
                     .RunResultAsync(conn) |> await |> ignore
  | None        -> ()

/// Delete a category
let deleteCategory conn cat =
  // Remove the category from its parent
  match cat.ParentId with
  | Some parentId -> removeCategoryFromParent conn cat.WebLogId parentId cat.Id
  | None          -> ()
  // Move this category's children to its parent
  let newParent = ExpandoObject()
  newParent?ParentId <- cat.ParentId
  cat.Children
  |> List.iter (fun childId -> (category cat.WebLogId childId)
                                 .Update(newParent)
                                 .RunResultAsync(conn) |> await |> ignore)
  // Remove the category from posts where it is assigned
  r.Table(Table.Post)
    .GetAll(cat.WebLogId).OptArg("index", "WebLogId")
    .Filter(ReqlFunction1(fun p -> upcast p.["CategoryIds"].Contains(cat.Id)))
    .RunCursorAsync<Post>(conn)
  |> await
  |> Seq.toList
  |> List.iter (fun post -> let newCats = ExpandoObject()
                            newCats?CategoryIds <- post.CategoryIds
                                                   |> List.filter (fun c -> c <> cat.Id)
                            r.Table(Table.Post)
                              .Get(post.Id)
                              .Update(newCats)
                              .RunResultAsync(conn) |> await |> ignore)
  // Now, delete the category
  r.Table(Table.Category)
    .Get(cat.Id)
    .Delete()
    .RunResultAsync(conn) |> await |> ignore

/// Get a category by its slug
let tryFindCategoryBySlug conn (webLogId : string) (slug : string) =
  r.Table(Table.Category)
    .GetAll(r.Array(webLogId, slug)).OptArg("index", "Slug")
    .RunCursorAsync<Category>(conn)
  |> await
  |> Seq.tryHead
