/// <summary>
/// Integration tests for <see cref="ICategoryData" /> implementations
/// </summary> 
module CategoryDataTests

open Expecto
open MyWebLog
open MyWebLog.Data

/// The ID of the root web log
let rootId = WebLogId "uSitJEuD3UyzWC9jgOHc8g"

/// The ID of the Favorites category
let favoritesId = CategoryId "S5JflPsJ9EG7gA2LD4m92A"

let ``Add succeeds`` (data: IData) = task {
    let category =
        { Category.Empty with Id = CategoryId "added-cat"; WebLogId = WebLogId "test"; Name = "Added"; Slug = "added" }
    do! data.Category.Add category 
    let! stored = data.Category.FindById (CategoryId "added-cat") (WebLogId "test")
    Expect.isSome stored "The category should have been added"
}

let ``CountAll succeeds when categories exist`` (data: IData) = task {
    let! count = data.Category.CountAll rootId
    Expect.equal count 3 "There should have been 3 categories"
}

let ``CountAll succeeds when categories do not exist`` (data: IData) = task {
    let! count = data.Category.CountAll WebLogId.Empty
    Expect.equal count 0 "There should have been no categories"
}

let ``CountTopLevel succeeds when top-level categories exist`` (data: IData) = task {
    let! count = data.Category.CountTopLevel rootId
    Expect.equal count 2 "There should have been 2 top-level categories"
}

let ``CountTopLevel succeeds when no top-level categories exist`` (data: IData) = task {
    let! count = data.Category.CountTopLevel WebLogId.Empty
    Expect.equal count 0 "There should have been no top-level categories"
}

let ``FindAllForView succeeds`` (data: IData) = task {
    let! all = data.Category.FindAllForView rootId
    Expect.equal all.Length 3 "There should have been 3 categories returned"
    Expect.equal all[0].Name "Favorites" "The first category is incorrect"
    Expect.equal all[0].PostCount 1 "There should be one post in this category"
    Expect.equal all[1].Name "Spitball" "The second category is incorrect"
    Expect.equal all[1].PostCount 2 "There should be two posts in this category"
    Expect.equal all[2].Name "Moonshot" "The third category is incorrect"
    Expect.equal all[2].PostCount 1 "There should be one post in this category"
}

let ``FindById succeeds when a category is found`` (data: IData) = task {
    let! cat = data.Category.FindById favoritesId rootId
    Expect.isSome cat "There should have been a category returned"
    Expect.equal cat.Value.Name "Favorites" "The category retrieved is incorrect"
    Expect.equal cat.Value.Slug "favorites" "The slug is incorrect"
    Expect.equal cat.Value.Description (Some "Favorite posts") "The description is incorrect"
    Expect.isNone cat.Value.ParentId "There should have been no parent ID"
}

let ``FindById succeeds when a category is not found`` (data: IData) = task {
    let! cat = data.Category.FindById CategoryId.Empty rootId
    Expect.isNone cat "There should not have been a category returned"
}

let ``FindByWebLog succeeds when categories exist`` (data: IData) = task {
    let! cats = data.Category.FindByWebLog rootId
    Expect.equal cats.Length 3 "There should be 3 categories"
    Expect.exists cats (fun it -> it.Name = "Favorites") "Favorites category not found"
    Expect.exists cats (fun it -> it.Name = "Spitball") "Spitball category not found"
    Expect.exists cats (fun it -> it.Name = "Moonshot") "Moonshot category not found"
}

let ``FindByWebLog succeeds when no categories exist`` (data: IData) = task {
    let! cats = data.Category.FindByWebLog WebLogId.Empty
    Expect.isEmpty cats "There should have been no categories returned"
}

let ``Update succeeds`` (data: IData) = task {
    match! data.Category.FindById favoritesId rootId with
    | Some cat ->
        do! data.Category.Update { cat with Name = "My Favorites"; Slug = "my-favorites"; Description = None }
        match! data.Category.FindById favoritesId rootId with
        | Some updated ->
            Expect.equal updated.Name "My Favorites" "Name not updated properly"
            Expect.equal updated.Slug "my-favorites" "Slug not updated properly"
            Expect.isNone updated.Description "Description should have been removed"
        | None -> Expect.isTrue false "The updated favorites category could not be retrieved"
    | None -> Expect.isTrue false "The favorites category could not be retrieved"
}

let ``Delete succeeds when the category is deleted (no posts)`` (data: IData) = task {
    let! result = data.Category.Delete (CategoryId "added-cat") (WebLogId "test")
    Expect.equal result CategoryDeleted "The category should have been deleted"
    let! cat = data.Category.FindById (CategoryId "added-cat") (WebLogId "test")
    Expect.isNone cat "The deleted category should not still exist"
}

let ``Delete succeeds when the category does not exist`` (data: IData) = task {
    let! result = data.Category.Delete CategoryId.Empty (WebLogId "none")
    Expect.equal result CategoryNotFound "The category should not have been found"
}

let ``Delete succeeds when reassigning parent category to None`` (data: IData) = task {
    let moonshotId = CategoryId "ScVpyu1e7UiP7bDdge3ZEw"
    let spitballId = CategoryId "jw6N69YtTEWVHAO33jHU-w"
    let! result = data.Category.Delete spitballId rootId
    Expect.equal result ReassignedChildCategories "Child categories should have been reassigned"
    match! data.Category.FindById moonshotId rootId with
    | Some cat -> Expect.isNone cat.ParentId "Parent ID should have been cleared"
    | None -> Expect.isTrue false "Unable to find former child category"
}

let ``Delete succeeds when reassigning parent category to Some`` (data: IData) = task {
    do! data.Category.Add { Category.Empty with Id = CategoryId "a"; WebLogId = WebLogId "test"; Name = "A" }
    do! data.Category.Add
            { Category.Empty with
                Id       = CategoryId "b"
                WebLogId = WebLogId "test"
                Name     = "B"
                ParentId = Some (CategoryId "a") }
    do! data.Category.Add
            { Category.Empty with
                Id       = CategoryId "c"
                WebLogId = WebLogId "test"
                Name     = "C"
                ParentId = Some (CategoryId "b") }
    let! result = data.Category.Delete (CategoryId "b") (WebLogId "test")
    Expect.equal result ReassignedChildCategories "Child categories should have been reassigned"
    match! data.Category.FindById (CategoryId "c") (WebLogId "test") with
    | Some cat -> Expect.equal cat.ParentId (Some (CategoryId "a")) "Parent category ID not reassigned properly"
    | None -> Expect.isTrue false "Expected former child category not found"
}

let ``Delete succeeds and removes category from posts`` (data: IData) = task {
    let moonshotId = CategoryId "ScVpyu1e7UiP7bDdge3ZEw"
    let postId     = PostId "RCsCU2puYEmkpzotoi8p4g"
    match! data.Post.FindById postId rootId with
    | Some post ->
        Expect.equal post.CategoryIds [ moonshotId ] "Post category IDs are not as expected"
        let! result = data.Category.Delete moonshotId rootId
        Expect.equal result CategoryDeleted "The category should have been deleted (no children)"
        match! data.Post.FindById postId rootId with
        | Some p -> Expect.isEmpty p.CategoryIds "Category ID was not removed"
        | None -> Expect.isTrue false "The expected updated post was not found"
    | None -> Expect.isTrue false "The expected test post was not found"
}
