module CategoryDataTests

open Expecto
open MyWebLog
open MyWebLog.Data

/// Tests for the Add method
let addTests (data: IData) = task {
    let category =
        { Category.Empty with Id = CategoryId "added-cat"; WebLogId = WebLogId "test"; Name = "Added"; Slug = "added" }
    do! data.Category.Add category 
    let! stored = data.Category.FindById (CategoryId "added-cat") (WebLogId "test")
    Expect.isSome stored "The category should have been added"
}

