/// Utility functions for manipulating data
[<RequireQualifiedAccess>]
module internal MyWebLog.Data.Utils

open MyWebLog
open MyWebLog.ViewModels

/// Create a category hierarchy from the given list of categories
let rec orderByHierarchy (cats: Category list) parentId slugBase parentNames = seq {
    for cat in cats |> List.filter (fun c -> c.ParentId = parentId) do
        let fullSlug = (match slugBase with Some it -> $"{it}/" | None -> "") + cat.Slug
        { Id          = string cat.Id
          Slug        = fullSlug
          Name        = cat.Name
          Description = cat.Description
          ParentNames = Array.ofList parentNames
          // Post counts are filled on a second pass
          PostCount   = 0 }
        yield! orderByHierarchy cats (Some cat.Id) (Some fullSlug) ([ cat.Name ] |> List.append parentNames)
}

/// Get lists of items removed from and added to the given lists
let diffLists<'T, 'U when 'U: equality> oldItems newItems (f: 'T -> 'U) =
    let diff compList = fun item -> not (compList |> List.exists (fun other -> f item = f other))
    List.filter (diff newItems) oldItems, List.filter (diff oldItems) newItems

/// Find the revisions added and removed
let diffRevisions (oldRevs: Revision list) newRevs =
    diffLists oldRevs newRevs (fun rev -> $"{rev.AsOf.ToUnixTimeTicks()}|{rev.Text}")

open MyWebLog.Converters
open Newtonsoft.Json

/// Serialize an object to JSON
let serialize<'T> ser (item: 'T) =
    JsonConvert.SerializeObject(item, Json.settings ser)

/// Deserialize a JSON string 
let deserialize<'T> (ser: JsonSerializer) value =
    JsonConvert.DeserializeObject<'T>(value, Json.settings ser)

open BitBadger.Documents

/// Create a document serializer using the given JsonSerializer
let createDocumentSerializer ser =
    { new IDocumentSerializer with
        member _.Serialize<'T>(it: 'T) : string = serialize ser it
        member _.Deserialize<'T>(it: string) : 'T = deserialize ser it
    }

/// Data migration utilities
module Migration =
    
    open Microsoft.Extensions.Logging

    /// The current database version
    let currentDbVersion = "v2.1.1"

    /// Log a migration step
    let logStep<'T> (log: ILogger<'T>) migration message =
        log.LogInformation $"Migrating %s{migration}: %s{message}"

    /// Notify the user that a backup/restore
    let backupAndRestoreRequired log oldVersion newVersion webLogs =
        logStep log $"%s{oldVersion} to %s{newVersion}" "Requires Using Action"

        [ "** MANUAL DATABASE UPGRADE REQUIRED **"; ""
          $"The data structure changed between {oldVersion} and {newVersion}."
          "To migrate your data:"
          $" - Use a {oldVersion} executable to back up each web log"
          " - Drop all tables from the database"
          " - Use this executable to restore each backup"; ""
          "Commands to back up all web logs:"
          yield! webLogs |> List.map (fun (url, slug) -> $"./myWebLog backup %s{url} {oldVersion}.%s{slug}.json") ]
        |> String.concat "\n"
        |> log.LogWarning
        
        log.LogCritical "myWebLog will now exit"
        exit 1 |> ignore
        