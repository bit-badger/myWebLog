namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json

/// SQLite myWebLog category data implementation
type SQLiteCategoryData(conn: SqliteConnection, ser: JsonSerializer, log: ILogger) =
    
    /// The name of the parent ID field
    let parentIdField = nameof Category.Empty.ParentId
    
    /// Add a category
    let add (cat: Category) =
        log.LogTrace "Category.add"
        Document.insert conn ser Table.Category cat
    
    /// Count all categories for the given web log
    let countAll webLogId =
        log.LogTrace "Category.countAll"
        Document.countByWebLog conn Table.Category webLogId
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId = backgroundTask {
        log.LogTrace "Category.countTopLevel"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"{Query.countByWebLog} AND data ->> '{parentIdField}' IS NULL"
        addWebLogId cmd webLogId
        return! count cmd
    }
    
    /// Find all categories for the given web log
    let findByWebLog webLogId =
        log.LogTrace "Category.findByWebLog"
        Document.findByWebLog<Category> conn ser Table.Category webLogId
    
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
                use cmd = conn.CreateCommand()
                cmd.CommandText <- $"
                    SELECT COUNT(DISTINCT data ->> '{nameof Post.Empty.Id}')
                      FROM {Table.Post}
                     WHERE {Query.whereByWebLog}
                       AND data ->> '{nameof Post.Empty.Status}' = '{string Published}'
                       AND {catSql}"
                addWebLogId cmd webLogId
                cmd.Parameters.AddRange catParams
                let! postCount = count cmd
                return it.Id, postCount
            })
            |> Task.WhenAll
        return
            ordered
            |> Seq.map (fun cat ->
                { cat with
                    PostCount =
                        counts
                        |> Array.tryFind (fun c -> fst c = cat.Id)
                        |> Option.map snd
                        |> Option.defaultValue 0 })
            |> Array.ofSeq
    }
    
    /// Find a category by its ID for the given web log
    let findById catId webLogId =
        log.LogTrace "Category.findById"
        Document.findByIdAndWebLog<CategoryId, Category> conn ser Table.Category catId webLogId
    
    /// Delete a category
    let delete catId webLogId = backgroundTask {
        log.LogTrace "Category.delete"
        match! findById catId webLogId with
        | Some cat ->
            use cmd = conn.CreateCommand()
            // Reassign any children to the category's parent category
            cmd.CommandText <- $"SELECT COUNT(*) FROM {Table.Category} WHERE data ->> '{parentIdField}' = @parentId"
            addParam cmd "@parentId" (string catId)
            let! children = count cmd
            if children > 0 then
                cmd.CommandText <- $"
                    UPDATE {Table.Category}
                       SET data = json_set(data, '$.{parentIdField}', @newParentId)
                     WHERE data ->> '{parentIdField}' = @parentId"
                addParam cmd "@newParentId" (maybe (cat.ParentId |> Option.map string))
                do! write cmd
            // Delete the category off all posts where it is assigned, and the category itself
            let catIdField = Post.Empty.CategoryIds
            cmd.CommandText <- $"
                SELECT data ->> '{Post.Empty.Id}' AS id, data -> '{catIdField}' AS cat_ids
                  FROM {Table.Post}
                 WHERE {Query.whereByWebLog}
                   AND EXISTS
                         (SELECT 1 FROM json_each({Table.Post}.data -> '{catIdField}') WHERE json_each.value = @id)"
            cmd.Parameters.Clear()
            addDocId cmd catId
            addWebLogId cmd webLogId
            use! postRdr = cmd.ExecuteReaderAsync()
            if postRdr.HasRows then
                let postIdAndCats =
                    toList
                        (fun rdr ->
                            Map.getString "id" rdr, Utils.deserialize<string list> ser (Map.getString "cat_ids" rdr))
                        postRdr
                do! postRdr.CloseAsync()
                for postId, cats in postIdAndCats do
                    cmd.CommandText <- $"
                        UPDATE {Table.Post}
                           SET data = json_set(data, '$.{catIdField}', json(@catIds))
                         WHERE {Query.whereById}"
                    cmd.Parameters.Clear()
                    addDocId cmd postId
                    addParam cmd "@catIds" (cats |> List.filter (fun it -> it <> string catId) |> Utils.serialize ser)
                    do! write cmd
            do! Document.delete conn Table.Category catId
            return if children = 0 then CategoryDeleted else ReassignedChildCategories
        | None -> return CategoryNotFound
    }
    
    /// Restore categories from a backup
    let restore cats = backgroundTask {
        for cat in cats do
            do! add cat
    }
    
    /// Update a category
    let update (cat: Category) = backgroundTask {
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"{Query.updateById} AND {Query.whereByWebLog}"
        addDocId cmd cat.Id
        addDocParam cmd cat ser
        addWebLogId cmd cat.WebLogId
        do! write cmd
    }
    
    interface ICategoryData with
        member _.Add cat = add cat
        member _.CountAll webLogId = countAll webLogId
        member _.CountTopLevel webLogId = countTopLevel webLogId
        member _.FindAllForView webLogId = findAllForView webLogId
        member _.FindById catId webLogId = findById catId webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.Delete catId webLogId = delete catId webLogId
        member _.Restore cats = restore cats
        member _.Update cat = update cat
