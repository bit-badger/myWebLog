/// Handlers to manipulate categories
module MyWebLog.Handlers.Category

open DotLiquid
open Giraffe
open MyWebLog

// GET /categories
let all : HttpHandler = requireUser >=> fun next ctx -> task {
    return!
        Hash.FromAnonymousObject {|
            categories = CategoryCache.get ctx
            page_title = "Categories"
            csrf       = csrfToken ctx
        |}
        |> viewForTheme "admin" "category-list" next ctx
}

open MyWebLog.ViewModels

// GET /category/{id}/edit
let edit catId : HttpHandler = requireUser >=> fun next ctx -> task {
    let  webLogId = webLogId ctx
    let  conn     = conn     ctx
    let! result   = task {
        match catId with
        | "new" -> return Some ("Add a New Category", { Category.empty with id = CategoryId "new" })
        | _ ->
            match! Data.Category.findById (CategoryId catId) webLogId conn with
            | Some cat -> return Some ("Edit Category", cat)
            | None -> return None
    }
    match result with
    | Some (title, cat) ->
        return!
            Hash.FromAnonymousObject {|
                csrf       = csrfToken ctx
                model      = EditCategoryModel.fromCategory cat
                page_title = title
                categories = CategoryCache.get ctx
            |}
            |> viewForTheme "admin" "category-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /category/save
let save : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let! model    = ctx.BindFormAsync<EditCategoryModel> ()
    let  webLogId = webLogId ctx
    let  conn     = conn     ctx
    let! category = task {
        match model.categoryId with
        | "new" -> return Some { Category.empty with id = CategoryId.create (); webLogId = webLogId }
        | catId -> return! Data.Category.findById (CategoryId catId) webLogId conn
    }
    match category with
    | Some cat ->
        let cat =
            { cat with
                name        = model.name
                slug        = model.slug
                description = if model.description = "" then None else Some model.description
                parentId    = if model.parentId    = "" then None else Some (CategoryId model.parentId)
            }
        do! (match model.categoryId with "new" -> Data.Category.add | _ -> Data.Category.update) cat conn
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Category saved successfully" }
        return! redirectToGet $"/category/{CategoryId.toString cat.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /category/{id}/delete
let delete catId : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let webLogId = webLogId ctx
    let conn     = conn     ctx
    match! Data.Category.delete (CategoryId catId) webLogId conn with
    | true ->
        do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Category deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Category not found; cannot delete" }
    return! redirectToGet "/categories" next ctx
}
