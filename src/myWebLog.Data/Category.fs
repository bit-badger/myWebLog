module myWebLog.Data.Category

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to get a category by Id and filter by web log Id
let private category (webLogId : string) (catId : string) =
  r.Table(Table.Category)
    .Get(catId)
    .Filter(fun c -> c.["webLogId"].Eq(webLogId))

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
let getAllCategories conn (webLogId : string) =
  r.Table(Table.Category)
    .GetAll(webLogId).OptArg("index", "webLogId")
    .OrderBy("name")
    .RunCursorAsync<Category>(conn)
  |> await
  |> Seq.toList
  |> sortCategories

/// Count categories for a web log
let countCategories conn (webLogId : string) =
  r.Table(Table.Category)
    .GetAll(webLogId).OptArg("index", "webLogId")
    .Count()
    .RunAtomAsync<int>(conn) |> await

/// Get a specific category by its Id
let tryFindCategory conn webLogId catId : Category option =
  match (category webLogId catId)
          .RunAtomAsync<Category>(conn) |> await |> box with
  | null -> None
  | cat  -> Some <| unbox cat

/// Save a category
let saveCategory conn webLogId (cat : Category) =
  match cat.id with
  | "new" -> let newCat = { cat with id       = string <| System.Guid.NewGuid()
                                     webLogId = webLogId }
             r.Table(Table.Category)
               .Insert(newCat)
               .RunResultAsync(conn) |> await |> ignore
             newCat.id
  | _     -> let upd8 = ExpandoObject()
             upd8?name        <- cat.name
             upd8?slug        <- cat.slug
             upd8?description <- cat.description
             upd8?parentId    <- cat.parentId
             (category webLogId cat.id)
               .Update(upd8)
               .RunResultAsync(conn) |> await |> ignore
             cat.id

/// Remove a category from a given parent
let removeCategoryFromParent conn webLogId parentId catId =
  match tryFindCategory conn webLogId parentId with
  | Some parent -> let upd8 = ExpandoObject()
                   upd8?children <- parent.children
                                    |> List.filter (fun childId -> childId <> catId)
                   (category webLogId parentId)
                     .Update(upd8)
                     .RunResultAsync(conn) |> await |> ignore
  | None        -> ()

/// Add a category to a given parent
let addCategoryToParent conn webLogId parentId catId =
  match tryFindCategory conn webLogId parentId with
  | Some parent -> let upd8 = ExpandoObject()
                   upd8?children <- catId :: parent.children
                   (category webLogId parentId)
                     .Update(upd8)
                     .RunResultAsync(conn) |> await |> ignore
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
  |> List.iter (fun childId -> (category cat.webLogId childId)
                                 .Update(newParent)
                                 .RunResultAsync(conn) |> await |> ignore)
  // Remove the category from posts where it is assigned
  r.Table(Table.Post)
    .GetAll(cat.webLogId).OptArg("index", "webLogId")
    .Filter(fun p -> p.["categoryIds"].Contains(cat.id))
    .RunCursorAsync<Post>(conn)
  |> await
  |> Seq.toList
  |> List.iter (fun post -> let newCats = ExpandoObject()
                            newCats?categoryIds <- post.categoryIds
                                                   |> List.filter (fun c -> c <> cat.id)
                            r.Table(Table.Post)
                              .Get(post.id)
                              .Update(newCats)
                              .RunResultAsync(conn) |> await |> ignore)
  // Now, delete the category
  r.Table(Table.Category)
    .Get(cat.id)
    .Delete()
    .RunResultAsync(conn) |> await |> ignore

/// Get a category by its slug
let tryFindCategoryBySlug conn (webLogId : string) (slug : string) =
  r.Table(Table.Category)
    .GetAll(webLogId, slug).OptArg("index", "slug")
    .RunCursorAsync<Category>(conn)
  |> await
  |> Seq.tryHead
