module MyWebLog.Data.RethinkDB.Category

open MyWebLog.Entities
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Shorthand to get a category by Id and filter by web log Id
let private category (webLogId : string) (catId : string) =
  r.Table(Table.Category)
    .Get(catId)
    .Filter(ReqlFunction1 (fun c -> upcast c.["WebLogId"].Eq webLogId))

/// Get all categories for a web log
let getAllCategories conn (webLogId : string) =
  async {
    return! r.Table(Table.Category)
              .GetAll(webLogId).OptArg("index", "WebLogId")
              .OrderBy("Name")
              .RunResultAsync<Category list> conn
    }
  |> Async.RunSynchronously

/// Get a specific category by its Id
let tryFindCategory conn webLogId catId : Category option =
  async {
    let! catt = (category webLogId catId).RunResultAsync<Category> conn
    return catt
    |> box
    |> function null -> None | cat -> Some <| unbox cat
    }
  |> Async.RunSynchronously

/// Add a category
let addCategory conn (cat : Category) =
  async {
    do! r.Table(Table.Category)
          .Insert(cat)
          .RunResultAsync conn
    }
  |> Async.RunSynchronously

type CategoryUpdateRecord =
  { Name : string
    Slug : string
    Description : string option
    ParentId : string option
  }
/// Update a category
let updateCategory conn (cat : Category) =
  async {
    do! (category cat.WebLogId cat.Id)
          .Update({ CategoryUpdateRecord.Name = cat.Name
                    Slug        = cat.Slug
                    Description = cat.Description
                    ParentId    = cat.ParentId
                    })
          .RunResultAsync conn
    }
  |> Async.RunSynchronously

type CategoryChildrenUpdateRecord =
  { Children : string list }
/// Update a category's children
let updateChildren conn webLogId parentId (children : string list) =
  async {
    do! (category webLogId parentId)
          .Update({ CategoryChildrenUpdateRecord.Children = children })
          .RunResultAsync conn
    }
  |> Async.RunSynchronously

type CategoryParentUpdateRecord =
  { ParentId : string option }
type PostCategoriesUpdateRecord =
  { CategoryIds : string list }
/// Delete a category
let deleteCategory conn (cat : Category) =
  async {
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
    |> List.map  (fun childId ->
        async {
          do! (category cat.WebLogId childId)
                .Update(newParent)
                .RunResultAsync conn
          })
    |> List.iter Async.RunSynchronously
    // Remove the category from posts where it is assigned
    let! posts =
      r.Table(Table.Post)
        .GetAll(cat.WebLogId).OptArg("index", "WebLogId")
        .Filter(ReqlFunction1 (fun p -> upcast p.["CategoryIds"].Contains cat.Id))
        .RunResultAsync<Post list> conn
      |> Async.AwaitTask
    posts
    |> List.map (fun post ->
        async {
          let newCats = 
            { PostCategoriesUpdateRecord.CategoryIds = post.CategoryIds
                                                       |> List.filter (fun c -> c <> cat.Id)
            }
          do! r.Table(Table.Post)
                .Get(post.Id)
                .Update(newCats)
                .RunResultAsync conn
          })
    |> List.iter Async.RunSynchronously
    // Now, delete the category
    do! r.Table(Table.Category)
          .Get(cat.Id)
          .Delete()
          .RunResultAsync conn
    }
  |> Async.RunSynchronously

/// Get a category by its slug
let tryFindCategoryBySlug conn (webLogId : string) (slug : string) =
  async {
    let! cat = r.Table(Table.Category)
                .GetAll(r.Array (webLogId, slug)).OptArg("index", "Slug")
                .RunResultAsync<Category list> conn
    return cat |> List.tryHead
    }
  |> Async.RunSynchronously
