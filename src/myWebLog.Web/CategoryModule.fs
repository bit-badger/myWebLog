namespace myWebLog

open myWebLog.Data.Category
open myWebLog.Entities
open Nancy
open Nancy.ModelBinding
open Nancy.Security
open RethinkDb.Driver.Net

/// Handle /category and /categories URLs
type CategoryModule(conn : IConnection) as this =
  inherit NancyModule()

  do
    this.Get   .["/categories"          ] <- fun _     -> this.CategoryList   ()
    this.Get   .["/category/{id}/edit"  ] <- fun parms -> this.EditCategory   (downcast parms)
    this.Post  .["/category/{id}/edit"  ] <- fun parms -> this.SaveCategory   (downcast parms)
    this.Delete.["/category/{id}/delete"] <- fun parms -> this.DeleteCategory (downcast parms)

  /// Display a list of categories
  member this.CategoryList () =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = CategoryListModel(this.Context, this.WebLog,
                                  (getAllCategories conn this.WebLog.id
                                   |> List.map (fun cat -> IndentedCategory.create cat (fun _ -> false))))
    upcast this.View.["/admin/category/list", model]
  
  /// Edit a category
  member this.EditCategory (parameters : DynamicDictionary) =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let catId : string = downcast parameters.["id"]
    match (match catId with
           | "new" -> Some Category.empty
           | _     -> tryFindCategory conn this.WebLog.id catId) with
    | Some cat -> let model = CategoryEditModel(this.Context, this.WebLog, cat)
                  let cats  = getAllCategories conn this.WebLog.id
                              |> List.map (fun cat -> IndentedCategory.create cat
                                                        (fun c -> c = defaultArg (fst cat).parentId ""))
                  model.categories <- getAllCategories conn this.WebLog.id
                                      |> List.map (fun cat -> IndentedCategory.create cat
                                                                (fun c -> c = defaultArg (fst cat).parentId ""))
                  upcast this.View.["admin/category/edit", model]
    | None     -> this.NotFound ()

  /// Save a category
  member this.SaveCategory (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let catId : string = downcast parameters.["id"]
    let form   = this.Bind<CategoryForm> ()
    let oldCat = match catId with
                 | "new" -> Some Category.empty
                 | _     -> tryFindCategory conn this.WebLog.id catId
    match oldCat with
    | Some old -> let cat = { old with name        = form.name
                                       slug        = form.slug
                                       description = match form.description with | "" -> None | d -> Some d
                                       parentId    = match form.parentId    with | "" -> None | p -> Some p }
                  let newCatId = saveCategory conn this.WebLog.id cat
                  match old.parentId = cat.parentId with
                  | true -> ()
                  | _    -> match old.parentId with
                            | Some parentId -> removeCategoryFromParent conn this.WebLog.id parentId newCatId
                            | None          -> ()
                            match cat.parentId with
                            | Some parentId -> addCategoryToParent conn this.WebLog.id parentId newCatId
                            | None          -> ()
                  let model = MyWebLogModel(this.Context, this.WebLog)
                  { level   = Level.Info
                    message = System.String.Format
                                (Resources.MsgCategoryEditSuccess,
                                  (match catId with | "new" -> Resources.Added | _ -> Resources.Updated))
                    details = None }
                  |> model.addMessage
                  this.Redirect (sprintf "/category/%s/edit" newCatId) model
    | None     -> this.NotFound ()

  /// Delete a category
  member this.DeleteCategory (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let catId : string = downcast parameters.["id"]
    match tryFindCategory conn this.WebLog.id catId with
    | Some cat -> deleteCategory conn cat
                  let model = MyWebLogModel(this.Context, this.WebLog)
                  { level  = Level.Info
                    message = System.String.Format(Resources.MsgCategoryDeleted, cat.name)
                    details = None }
                  |> model.addMessage
                  this.Redirect "/categories" model
    | None     -> this.NotFound ()
