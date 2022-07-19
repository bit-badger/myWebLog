/// Utility functions for manipulating data
[<RequireQualifiedAccess>]
module internal MyWebLog.Data.Utils

open MyWebLog
open MyWebLog.ViewModels

/// Create a category hierarchy from the given list of categories
let rec orderByHierarchy (cats : Category list) parentId slugBase parentNames = seq {
    for cat in cats |> List.filter (fun c -> c.ParentId = parentId) do
        let fullSlug = (match slugBase with Some it -> $"{it}/" | None -> "") + cat.Slug
        { Id          = CategoryId.toString cat.Id
          Slug        = fullSlug
          Name        = cat.Name
          Description = cat.Description
          ParentNames = Array.ofList parentNames
          // Post counts are filled on a second pass
          PostCount   = 0
        }
        yield! orderByHierarchy cats (Some cat.Id) (Some fullSlug) ([ cat.Name ] |> List.append parentNames)
}

