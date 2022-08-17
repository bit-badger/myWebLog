namespace MyWebLog.Data.PostgreSql

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

type PostgreSqlCategoryData (conn : NpgsqlConnection) =
    
    /// Add parameters for category INSERT or UPDATE statements
    let addCategoryParameters (cat : Category) =
        Sql.parameters [
            webLogIdParam cat.WebLogId
            "@id",          Sql.string       (CategoryId.toString cat.Id)
            "@name",        Sql.string       cat.Name
            "@slug",        Sql.string       cat.Slug
            "@description", Sql.stringOrNone cat.Description
            "@parentId",    Sql.stringOrNone (cat.ParentId |> Option.map CategoryId.toString)
        ]
    
    /// Add a category
    let add cat = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query """
                INSERT INTO category (
                    id, web_log_id, name, slug, description, parent_id
                ) VALUES (
                    @id, @webLogId, @name, @slug, @description, @parentId
                )"""
            |> addCategoryParameters cat
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Count all categories for the given web log
    let countAll webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT COUNT(id) AS the_count FROM category WHERE web_log_id = @webLogId"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT COUNT(id) FROM category WHERE web_log_id = @webLogId AND parent_id IS NULL"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Retrieve all categories for the given web log in a DotLiquid-friendly format
    let findAllForView webLogId = backgroundTask {
        let! cats =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM category WHERE web_log_id = @webLogId ORDER BY LOWER(name)"
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toCategory
        let  ordered = Utils.orderByHierarchy cats None None []
        let counts  =
            ordered
            // |> Seq.map (fun it -> backgroundTask {
            //     // Parent category post counts include posts in subcategories
            //     cmd.Parameters.Clear ()
            //     addWebLogId cmd webLogId
            //     cmd.CommandText <- """
            //         SELECT COUNT(DISTINCT p.id)
            //           FROM post p
            //                INNER JOIN post_category pc ON pc.post_id = p.id
            //          WHERE p.web_log_id = @webLogId
            //            AND p.status     = 'Published'
            //            AND pc.category_id IN ("""
            //     ordered
            //     |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
            //     |> Seq.map (fun cat -> cat.Id)
            //     |> Seq.append (Seq.singleton it.Id)
            //     |> Seq.iteri (fun idx item ->
            //         if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
            //         cmd.CommandText <- $"{cmd.CommandText}@catId{idx}"
            //         cmd.Parameters.AddWithValue ($"@catId{idx}", item) |> ignore)
            //     cmd.CommandText <- $"{cmd.CommandText})"
            //     let! postCount = count cmd
            //     return it.Id, postCount
            //     })
            // |> Task.WhenAll
        return
            ordered
            |> Seq.map (fun cat ->
                { cat with
                    PostCount = counts
                                |> Array.tryFind (fun c -> fst c = cat.Id)
                                |> Option.map snd
                                |> Option.defaultValue 0
                })
            |> Array.ofSeq
    }
    /// Find a category by its ID for the given web log
    let findById catId webLogId = backgroundTask {
        let! cat =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM category WHERE id = @id AND web_log_id = @webLogId"
            |> Sql.parameters [ "@id", Sql.string (CategoryId.toString catId); webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toCategory
        return List.tryHead cat
    }
    
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
            let parentParam = "@parentId", Sql.string (CategoryId.toString catId)
            let! children =
                Sql.existingConnection conn
                |> Sql.query "SELECT COUNT(id) AS the_count FROM category WHERE parent_id = @parentId"
                |> Sql.parameters [ parentParam ]
                |> Sql.executeRowAsync Map.toCount
            if children > 0 then
                let! _ =
                    Sql.existingConnection conn
                    |> Sql.query "UPDATE category SET parent_id = @newParentId WHERE parent_id = @parentId"
                    |> Sql.parameters
                        [   parentParam
                            "@newParentId", Sql.stringOrNone (cat.ParentId |> Option.map CategoryId.toString) ]
                    |> Sql.executeNonQueryAsync
                ()
            // Delete the category off all posts where it is assigned
            let catIdParam = "@id", Sql.string (CategoryId.toString catId)
            let! _ =
                Sql.existingConnection conn
                |> Sql.query """
                    DELETE FROM post_category
                     WHERE category_id = @id
                       AND post_id IN (SELECT id FROM post WHERE web_log_id = @webLogId)"""
                |> Sql.parameters [ catIdParam; webLogIdParam webLogId ]
                |> Sql.executeNonQueryAsync
            // Delete the category itself
            let! _ =
                Sql.existingConnection conn
                |> Sql.query "DELETE FROM category WHERE id = @id"
                |> Sql.parameters [ catIdParam ]
                |> Sql.executeNonQueryAsync
            return if children = 0 then CategoryDeleted else ReassignedChildCategories
        | None -> return CategoryNotFound
    }
    
    /// Restore categories from a backup
    let restore cats = backgroundTask {
        for cat in cats do
            do! add cat
    }
    
    /// Update a category
    let update cat = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query """
                UPDATE category
                   SET name        = @name,
                       slug        = @slug,
                       description = @description,
                       parent_id   = @parentId
                 WHERE id         = @id
                   AND web_log_id = @webLogId"""
            |> addCategoryParameters cat
            |> Sql.executeNonQueryAsync
        ()
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
