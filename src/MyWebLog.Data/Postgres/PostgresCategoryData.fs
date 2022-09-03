namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog category data implementation
type PostgresCategoryData (conn : NpgsqlConnection, ser : JsonSerializer) =
    
    /// Convert a data row to a category
    let toCategory = Map.fromDoc<Category> ser
    
    /// Count all categories for the given web log
    let countAll webLogId =
        Document.countByWebLog conn Table.Category webLogId None
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId =
        Document.countByWebLog conn Table.Category webLogId
            (Some $"AND data -> '{nameof Category.empty.ParentId}' IS NULL")
    
    /// Retrieve all categories for the given web log in a DotLiquid-friendly format
    let findAllForView webLogId = backgroundTask {
        let! cats =
            Document.findByWebLog conn Table.Category webLogId toCategory
                (Some $"ORDER BY LOWER(data ->> '{nameof Category.empty.Name}')")
        let ordered = Utils.orderByHierarchy cats None None []
        let counts  =
            ordered
            |> Seq.map (fun it ->
                // Parent category post counts include posts in subcategories
                let catIdSql, catIdParams =
                    ordered
                    |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
                    |> Seq.map (fun cat -> cat.Id)
                    |> Seq.append (Seq.singleton it.Id)
                    |> List.ofSeq
                    |> jsonArrayInClause (nameof Post.empty.CategoryIds) id
                let postCount =
                    Sql.existingConnection conn
                    |> Sql.query $"
                        SELECT COUNT(DISTINCT id) AS {countName}
                          FROM {Table.Post}
                         WHERE {webLogWhere}
                           AND data ->> '{nameof Post.empty.Status}' = '{PostStatus.toString Published}'
                           AND ({catIdSql})"
                    |> Sql.parameters (webLogIdParam webLogId :: catIdParams)
                    |> Sql.executeRowAsync Map.toCount
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                it.Id, postCount)
            |> List.ofSeq
        return
            ordered
            |> Seq.map (fun cat ->
                { cat with
                    PostCount = counts
                                |> List.tryFind (fun c -> fst c = cat.Id)
                                |> Option.map snd
                                |> Option.defaultValue 0
                })
            |> Array.ofSeq
    }
    /// Find a category by its ID for the given web log
    let findById catId webLogId =
        Document.findByIdAndWebLog conn Table.Category catId CategoryId.toString webLogId toCategory
    
    /// Find all categories for the given web log
    let findByWebLog webLogId =
        Document.findByWebLog conn Table.Category webLogId toCategory None
    
    /// Create parameters for a category insert / update
    let catParameters (cat : Category) = [
        "@id",   Sql.string (CategoryId.toString cat.Id)
        "@data", Sql.jsonb  (Utils.serialize ser cat)
    ]
    
    /// Delete a category
    let delete catId webLogId = backgroundTask {
        match! findById catId webLogId with
        | Some cat ->
            // Reassign any children to the category's parent category
            let  parentParam = "@parentId", Sql.string (CategoryId.toString catId)
            let! children =
                Sql.existingConnection conn
                |> Sql.query
                    $"SELECT * FROM {Table.Category} WHERE data ->> '{nameof Category.empty.ParentId}' = @parentId"
                |> Sql.parameters [ parentParam ]
                |> Sql.executeAsync toCategory
            let hasChildren = not (List.isEmpty children)
            if hasChildren then
                let! _ =
                    Sql.existingConnection conn
                    |> Sql.executeTransactionAsync [
                        docUpdateSql Table.Category,
                        children |> List.map (fun child -> catParameters { child with ParentId = cat.ParentId })
                    ]
                ()
            // Delete the category off all posts where it is assigned
            let! posts =
                Sql.existingConnection conn
                |> Sql.query $"SELECT * FROM {Table.Post} WHERE data -> '{nameof Post.empty.CategoryIds}' ? @id"
                |> Sql.parameters [ "@id", Sql.jsonb (CategoryId.toString catId) ]
                |> Sql.executeAsync (Map.fromDoc<Post> ser)
            if not (List.isEmpty posts) then
                let! _ =
                    Sql.existingConnection conn
                    |> Sql.executeTransactionAsync [
                        docUpdateSql Table.Post,
                        posts |> List.map (fun post -> [
                            "@id",   Sql.string (PostId.toString post.Id)
                            "@data", Sql.jsonb  (Utils.serialize ser {
                                post with
                                  CategoryIds = post.CategoryIds |> List.filter (fun cat -> cat <> catId)
                            })
                        ])
                    ]
                ()
            // Delete the category itself
            do! Document.delete conn Table.Category (CategoryId.toString catId)
            return if hasChildren then ReassignedChildCategories else CategoryDeleted
        | None -> return CategoryNotFound
    }
    
    /// Save a category
    let save cat = backgroundTask {
        do! Document.upsert conn Table.Category catParameters cat
    }
    
    /// Restore categories from a backup
    let restore cats = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                docInsertSql Table.Category, cats |> List.map catParameters
            ]
        ()
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
