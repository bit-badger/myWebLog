/// Utility functions for manipulating data
[<RequireQualifiedAccess>]
module internal MyWebLog.Data.Utils

open MyWebLog
open MyWebLog.ViewModels

/// Create a category hierarchy from the given list of categories
let rec orderByHierarchy (cats : Category list) parentId slugBase parentNames = seq {
    for cat in cats |> List.filter (fun c -> c.parentId = parentId) do
        let fullSlug = (match slugBase with Some it -> $"{it}/" | None -> "") + cat.slug
        { Id          = CategoryId.toString cat.id
          Slug        = fullSlug
          Name        = cat.name
          Description = cat.description
          ParentNames = Array.ofList parentNames
          // Post counts are filled on a second pass
          PostCount   = 0
        }
        yield! orderByHierarchy cats (Some cat.id) (Some fullSlug) ([ cat.name ] |> List.append parentNames)
}

