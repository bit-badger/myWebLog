/// Utility functions for manipulating data
[<RequireQualifiedAccess>]
module internal MyWebLog.Data.Utils

open MyWebLog
open MyWebLog.ViewModels

/// The current database version
let currentDbVersion = "v2.1"

/// Create a category hierarchy from the given list of categories
let rec orderByHierarchy (cats : Category list) parentId slugBase parentNames = seq {
    for cat in cats |> List.filter (fun c -> c.ParentId = parentId) do
        let fullSlug = (match slugBase with Some it -> $"{it}/" | None -> "") + cat.Slug
        { Id          = cat.Id.Value
          Slug        = fullSlug
          Name        = cat.Name
          Description = cat.Description
          ParentNames = Array.ofList parentNames
          // Post counts are filled on a second pass
          PostCount   = 0
        }
        yield! orderByHierarchy cats (Some cat.Id) (Some fullSlug) ([ cat.Name ] |> List.append parentNames)
}

/// Get lists of items removed from and added to the given lists
let diffLists<'T, 'U when 'U: equality> oldItems newItems (f: 'T -> 'U) =
    let diff compList = fun item -> not (compList |> List.exists (fun other -> f item = f other))
    List.filter (diff newItems) oldItems, List.filter (diff oldItems) newItems

/// Find meta items added and removed
let diffMetaItems (oldItems : MetaItem list) newItems =
    diffLists oldItems newItems (fun item -> $"{item.Name}|{item.Value}")

/// Find the permalinks added and removed
let diffPermalinks oldLinks newLinks =
    diffLists oldLinks newLinks (fun (it: Permalink) -> it.Value)

/// Find the revisions added and removed
let diffRevisions oldRevs newRevs =
    diffLists oldRevs newRevs (fun (rev: Revision) -> $"{rev.AsOf.ToUnixTimeTicks()}|{rev.Text.Value}")

open MyWebLog.Converters
open Newtonsoft.Json

/// Serialize an object to JSON
let serialize<'T> ser (item: 'T) =
    JsonConvert.SerializeObject(item, Json.settings ser)

/// Deserialize a JSON string 
let deserialize<'T> (ser: JsonSerializer) value =
    JsonConvert.DeserializeObject<'T>(value, Json.settings ser)

open Microsoft.Extensions.Logging

/// Log a migration step
let logMigrationStep<'T> (log: ILogger<'T>) migration message =
    log.LogInformation $"Migrating %s{migration}: %s{message}"
