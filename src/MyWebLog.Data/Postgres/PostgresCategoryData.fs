namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog category data implementation
type PostgresCategoryData (conn : NpgsqlConnection) =
    
    /// Count all categories for the given web log
    let countAll webLogId =
        Sql.existingConnection conn
        |> Sql.query $"SELECT COUNT(id) AS {countName} FROM category WHERE web_log_id = @webLogId"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId =
        Sql.existingConnection conn
        |> Sql.query $"SELECT COUNT(id) AS {countName} FROM category WHERE web_log_id = @webLogId AND parent_id IS NULL"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Retrieve all categories for the given web log in a DotLiquid-friendly format
    let findAllForView webLogId = backgroundTask {
        let! cats =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM category WHERE web_log_id = @webLogId ORDER BY LOWER(name)"
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toCategory
        let ordered = Utils.orderByHierarchy cats None None []
        let counts  =
            ordered
            |> Seq.map (fun it ->
                // Parent category post counts include posts in subcategories
                let catIdSql, catIdParams =
                    ordered
                    |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
                    |> Seq.map (fun cat -> cat.Id)
                    |> List.ofSeq
                    |> inClause "id" id
                let postCount =
                    Sql.existingConnection conn
                    |> Sql.query $"
                        SELECT COUNT(DISTINCT p.id) AS {countName}
                          FROM post p
                               INNER JOIN post_category pc ON pc.post_id = p.id
                         WHERE p.web_log_id = @webLogId
                           AND p.status     = 'Published'
                           AND pc.category_id IN ({catIdSql})"
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
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM category WHERE id = @id AND web_log_id = @webLogId"
        |> Sql.parameters [ "@id", Sql.string (CategoryId.toString catId); webLogIdParam webLogId ]
        |> Sql.executeAsync Map.toCategory
        |> tryHead
    
    /// Find all categories for the given web log
    let findByWebLog webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM category WHERE web_log_id = @webLogId"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync Map.toCategory
    
    
    /// Delete a category
    let delete catId webLogId = backgroundTask {
        match! findById catId webLogId with
        | Some cat ->
            // Reassign any children to the category's parent category
            let  parentParam = "@parentId", Sql.string (CategoryId.toString catId)
            let! hasChildren =
                Sql.existingConnection conn
                |> Sql.query $"SELECT EXISTS (SELECT 1 FROM category WHERE parent_id = @parentId) AS {existsName}"
                |> Sql.parameters [ parentParam ]
                |> Sql.executeRowAsync Map.toExists
            if hasChildren then
                let! _ =
                    Sql.existingConnection conn
                    |> Sql.query "UPDATE category SET parent_id = @newParentId WHERE parent_id = @parentId"
                    |> Sql.parameters
                        [   parentParam
                            "@newParentId", Sql.stringOrNone (cat.ParentId |> Option.map CategoryId.toString) ]
                    |> Sql.executeNonQueryAsync
                ()
            // Delete the category off all posts where it is assigned, and the category itself
            let! _ =
                Sql.existingConnection conn
                |> Sql.query
                    "DELETE FROM post_category
                      WHERE category_id = @id
                        AND post_id IN (SELECT id FROM post WHERE web_log_id = @webLogId);
                     DELETE FROM category WHERE id = @id"
                |> Sql.parameters [ "@id", Sql.string (CategoryId.toString catId); webLogIdParam webLogId ]
                |> Sql.executeNonQueryAsync
            return if hasChildren then ReassignedChildCategories else CategoryDeleted
        | None -> return CategoryNotFound
    }
    
    /// The INSERT statement for a category
    let catInsert =
        "INSERT INTO category (
            id, web_log_id, name, slug, description, parent_id
        ) VALUES (
            @id, @webLogId, @name, @slug, @description, @parentId
        )"
    
    /// Create parameters for a category insert / update
    let catParameters (cat : Category) = [
        webLogIdParam cat.WebLogId
        "@id",          Sql.string       (CategoryId.toString cat.Id)
        "@name",        Sql.string       cat.Name
        "@slug",        Sql.string       cat.Slug
        "@description", Sql.stringOrNone cat.Description
        "@parentId",    Sql.stringOrNone (cat.ParentId |> Option.map CategoryId.toString)
    ]

    /// Save a category
    let save cat = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"
                {catInsert} ON CONFLICT (id) DO UPDATE
                SET name        = EXCLUDED.name,
                    slug        = EXCLUDED.slug,
                    description = EXCLUDED.description,
                    parent_id   = EXCLUDED.parent_id"
            |> Sql.parameters (catParameters cat)
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Restore categories from a backup
    let restore cats = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                catInsert, cats |> List.map catParameters
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
