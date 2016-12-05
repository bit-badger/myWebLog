namespace MyWebLog

open MyWebLog.Data
open MyWebLog.Logic.Category
open MyWebLog.Entities
open MyWebLog.Resources
open Nancy
open Nancy.ModelBinding
open Nancy.Security
open RethinkDb.Driver.Net

/// Handle /category and /categories URLs
type CategoryModule (data : IMyWebLogData) as this =
  inherit NancyModule ()

  do
    this.Get  ("/categories",           fun _ -> this.CategoryList   ())
    this.Get  ("/category/{id}/edit",   fun p -> this.EditCategory   (downcast p))
    this.Post ("/category/{id}/edit",   fun p -> this.SaveCategory   (downcast p))
    this.Post ("/category/{id}/delete", fun p -> this.DeleteCategory (downcast p))

  /// Display a list of categories
  member this.CategoryList () : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model =
      CategoryListModel (
        this.Context, this.WebLog, findAllCategories data this.WebLog.Id
                                   |> List.map (fun cat -> IndentedCategory.Create cat (fun _ -> false)))
    model.PageTitle <- Strings.get "Categories"
    upcast this.View.["admin/category/list", model]
  
  /// Edit a category
  member this.EditCategory (parameters : DynamicDictionary) : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let catId = parameters.["id"].ToString ()
    match catId with "new" -> Some Category.Empty | _ -> tryFindCategory data this.WebLog.Id catId
    |> function
    | Some cat ->
        let model = CategoryEditModel (this.Context, this.WebLog, cat)
        model.Categories <- findAllCategories data this.WebLog.Id
                            |> List.map (fun c ->
                                IndentedCategory.Create c (fun catId -> catId = defaultArg cat.ParentId ""))
        model.PageTitle <- Strings.get <| match catId with "new" -> "AddNewCategory" | _ -> "EditCategory"
        upcast this.View.["admin/category/edit", model]
    | _ -> this.NotFound ()

  /// Save a category
  member this.SaveCategory (parameters : DynamicDictionary) : obj =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let catId = parameters.["id"].ToString ()
    let form  = this.Bind<CategoryForm> ()
    match catId with
    | "new" -> Some { Category.Empty with WebLogId = this.WebLog.Id }
    | _ -> tryFindCategory data this.WebLog.Id catId
    |> function
    | Some old ->
        let cat =
          { old with
              Name        = form.Name
              Slug        = form.Slug
              Description = match form.Description with "" -> None | d -> Some d
              ParentId    = match form.ParentId    with "" -> None | p -> Some p
              }
        let newCatId = saveCategory data cat
        match old.ParentId = cat.ParentId with
        | true -> ()
        | _ ->
            match old.ParentId with
            | Some parentId -> removeCategoryFromParent data this.WebLog.Id parentId newCatId
            | _ -> ()
            match cat.ParentId with
            | Some parentId -> addCategoryToParent data this.WebLog.Id parentId newCatId
            | _ -> ()
        let model = MyWebLogModel (this.Context, this.WebLog)
        model.AddMessage
          { UserMessage.Empty with
              Message = System.String.Format
                          (Strings.get "MsgCategoryEditSuccess",
                            Strings.get (match catId with "new" -> "Added" | _ -> "Updated"))
            }
        this.Redirect (sprintf "/category/%s/edit" newCatId) model
    | _ -> this.NotFound ()

  /// Delete a category
  member this.DeleteCategory (parameters : DynamicDictionary) : obj =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let catId = parameters.["id"].ToString ()
    match tryFindCategory data this.WebLog.Id catId with
    | Some cat ->
        deleteCategory data cat
        let model = MyWebLogModel (this.Context, this.WebLog)
        model.AddMessage
          { UserMessage.Empty with Message = System.String.Format(Strings.get "MsgCategoryDeleted", cat.Name) }
        this.Redirect "/categories" model
    | _ -> this.NotFound ()
