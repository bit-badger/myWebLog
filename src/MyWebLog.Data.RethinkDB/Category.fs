module MyWebLog.Data.RethinkDB.Category

open MyWebLog.Entities
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to get a category by Id and filter by web log Id
let private category (webLogId : string) (catId : string) =
  r.Table(Table.Category)
    .Get(catId)
    .Filter(fun c -> c.["WebLogId"].Eq(webLogId))

/// Get all categories for a web log
let getAllCategories conn (webLogId : string) =
  r.Table(Table.Category)
    .GetAll(webLogId).OptArg("index", "WebLogId")
    .OrderBy("Name")
    .RunListAsync<Category>(conn)
  |> await
  |> Seq.toList

/// Get a specific category by its Id
let tryFindCategory conn webLogId catId : Category option =
  match (category webLogId catId)
          .RunAtomAsync<Category>(conn) |> await |> box with
  | null -> None
  | cat -> Some <| unbox cat

/// Add a category
let addCategory conn (cat : Category) =
  r.Table(Table.Category)
    .Insert(cat)
    .RunResultAsync(conn) |> await |> ignore

type CategoryUpdateRecord =
  { Name : string
    Slug : string
    Description : string option
    ParentId : string option
  }

/// Update a category
let updateCategory conn (cat : Category) =
  (category cat.WebLogId cat.Id)
    .Update({ CategoryUpdateRecord.Name = cat.Name
              Slug        = cat.Slug
              Description = cat.Description
              ParentId    = cat.ParentId })
    .RunResultAsync(conn) |> await |> ignore

type CategoryChildrenUpdateRecord =
  { Children : string list }
/// Update a category's children
let updateChildren conn webLogId parentId (children : string list) =
  (category webLogId parentId)
    .Update({ CategoryChildrenUpdateRecord.Children = children })
    .RunResultAsync(conn) |> await |> ignore

type CategoryParentUpdateRecord =
  { ParentId : string option }
type PostCategoriesUpdateRecord =
  { CategoryIds : string list }
/// Delete a category
let deleteCategory conn (cat : Category) =
  // Remove the category from its parent
  match cat.ParentId with
  | Some parentId -> match tryFindCategory conn cat.WebLogId parentId with
                     | Some parent -> parent.Children
                                      |> List.filter (fun childId -> childId <> cat.Id)
                                      |> updateChildren conn cat.WebLogId parentId
                     | _ -> ()
  | _ -> ()
  // Move this category's children to its parent
  let newParent = { CategoryParentUpdateRecord.ParentId = cat.ParentId }
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
  |> List.iter (fun post -> let newCats = 
                              { PostCategoriesUpdateRecord.CategoryIds = post.CategoryIds
                                                                         |> List.filter (fun c -> c <> cat.Id) }
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
