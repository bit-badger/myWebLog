namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open BitBadger.Sqlite.FSharp.Documents
open BitBadger.Sqlite.FSharp.Documents.WithConn
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json

/// SQLite myWebLog category data implementation
type SQLiteCategoryData(conn: SqliteConnection, ser: JsonSerializer, log: ILogger) =
    
    /// The name of the parent ID field
    let parentIdField = nameof Category.Empty.ParentId
    
    /// Count all categories for the given web log
    let countAll webLogId =
        log.LogTrace "Category.countAll"
        Document.countByWebLog Table.Category webLogId conn
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId =
        log.LogTrace "Category.countTopLevel"
        Custom.scalar
            $"{Document.Query.countByWebLog} AND data ->> '{parentIdField}' IS NULL"
            [ webLogParam webLogId ]
            (fun rdr -> int (rdr.GetInt64(0)))
            conn
    
    /// Find all categories for the given web log
    let findByWebLog webLogId =
        log.LogTrace "Category.findByWebLog"
        Document.findByWebLog<Category> Table.Category webLogId conn
    
    /// Retrieve all categories for the given web log in a DotLiquid-friendly format
    let findAllForView webLogId = backgroundTask {
        log.LogTrace "Category.findAllForView"
        let! cats    = findByWebLog webLogId
        let  ordered = Utils.orderByHierarchy (cats |> List.sortBy _.Name.ToLowerInvariant()) None None []
        let! counts  =
            ordered
            |> Seq.map (fun it -> backgroundTask {
                // Parent category post counts include posts in subcategories
                let catSql, catParams =
                    ordered
                    |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
                    |> Seq.map _.Id
                    |> Seq.append (Seq.singleton it.Id)
                    |> List.ofSeq
                    |> inJsonArray Table.Post (nameof Post.Empty.CategoryIds) "catId"
                let query = $"""
                    SELECT COUNT(DISTINCT data ->> '{nameof Post.Empty.Id}')
                      FROM {Table.Post}
                     WHERE {Document.Query.whereByWebLog}
                       AND {Query.whereFieldEquals (nameof Post.Empty.Status) $"'{string Published}'"}
                       AND {catSql}"""
                let! postCount = Custom.scalar query (webLogParam webLogId :: catParams) (_.GetInt64(0)) conn
                return it.Id, int postCount
            })
            |> Task.WhenAll
        return
            ordered
            |> Seq.map (fun cat ->
                { cat with
                    PostCount = defaultArg (counts |> Array.tryFind (fun c -> fst c = cat.Id) |> Option.map snd) 0
                })
            |> Array.ofSeq
    }
    
    /// Find a category by its ID for the given web log
    let findById catId webLogId =
        log.LogTrace "Category.findById"
        Document.findByIdAndWebLog<CategoryId, Category> Table.Category catId webLogId conn
    
    /// Delete a category
    let delete catId webLogId = backgroundTask {
        log.LogTrace "Category.delete"
        match! findById catId webLogId with
        | Some cat ->
            // Reassign any children to the category's parent category
            let! children = Count.byFieldEquals Table.Category parentIdField catId conn
            if children > 0 then
                do! Update.partialByFieldEquals Table.Category parentIdField catId {| ParentId = cat.ParentId |} conn
            // Delete the category off all posts where it is assigned, and the category itself
            let catIdField = Post.Empty.CategoryIds
            let! posts =
                Custom.list
                    $"SELECT data ->> '{Post.Empty.Id}', data -> '{catIdField}'
                        FROM {Table.Post}
                       WHERE {Document.Query.whereByWebLog}
                         AND EXISTS
                               (SELECT 1
                                  FROM json_each({Table.Post}.data -> '{catIdField}')
                                 WHERE json_each.value = @id)"
                    [ idParam catId; webLogParam webLogId ]
                    (fun rdr -> rdr.GetString(0), Utils.deserialize<string list> ser (rdr.GetString(1)))
                    conn
            for postId, cats in posts do
                do! Update.partialById
                        Table.Post postId {| CategoryIds = cats |> List.filter (fun it -> it <> string catId) |} conn
            do! Delete.byId Table.Category catId conn
            return if children = 0L then CategoryDeleted else ReassignedChildCategories
        | None -> return CategoryNotFound
    }
    
    /// Save a category
    let save cat =
        log.LogTrace "Category.save"
        save<Category> Table.Category cat conn
    
    /// Restore categories from a backup
    let restore cats = backgroundTask {
        log.LogTrace "Category.restore"
        for cat in cats do do! save cat
    }
    
    interface ICategoryData with
        member _.Add cat = save cat
        member _.CountAll webLogId = countAll webLogId
        member _.CountTopLevel webLogId = countTopLevel webLogId
        member _.FindAllForView webLogId = findAllForView webLogId
        member _.FindById catId webLogId = findById catId webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.Delete catId webLogId = delete catId webLogId
        member _.Restore cats = restore cats
        member _.Update cat = save cat
