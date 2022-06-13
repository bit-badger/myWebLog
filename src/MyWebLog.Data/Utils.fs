/// Utility functions for manipulating data
[<RequireQualifiedAccess>]
module internal MyWebLog.Data.Utils

open MyWebLog
open MyWebLog.ViewModels

/// Create a category hierarchy from the given list of categories
let rec orderByHierarchy (cats : Category list) parentId slugBase parentNames = seq {
    for cat in cats |> List.filter (fun c -> c.parentId = parentId) do
        let fullSlug = (match slugBase with Some it -> $"{it}/" | None -> "") + cat.slug
        { id          = CategoryId.toString cat.id
          slug        = fullSlug
          name        = cat.name
          description = cat.description
          parentNames = Array.ofList parentNames
          // Post counts are filled on a second pass
          postCount   = 0
        }
        yield! orderByHierarchy cats (Some cat.id) (Some fullSlug) ([ cat.name ] |> List.append parentNames)
}

