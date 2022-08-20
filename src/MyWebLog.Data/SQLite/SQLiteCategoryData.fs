namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog category data implementation        
type SQLiteCategoryData (conn : SqliteConnection) =
    
    /// Add parameters for category INSERT or UPDATE statements
    let addCategoryParameters (cmd : SqliteCommand) (cat : Category) =
        [   cmd.Parameters.AddWithValue ("@id",          CategoryId.toString cat.Id)
            cmd.Parameters.AddWithValue ("@webLogId",    WebLogId.toString cat.WebLogId)
            cmd.Parameters.AddWithValue ("@name",        cat.Name)
            cmd.Parameters.AddWithValue ("@slug",        cat.Slug)
            cmd.Parameters.AddWithValue ("@description", maybe cat.Description)
            cmd.Parameters.AddWithValue ("@parentId",    maybe (cat.ParentId |> Option.map CategoryId.toString))
        ] |> ignore
    
    /// Add a category
    let add cat = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "INSERT INTO category (
                id, web_log_id, name, slug, description, parent_id
            ) VALUES (
                @id, @webLogId, @name, @slug, @description, @parentId
            )"
        addCategoryParameters cmd cat
        let! _ = cmd.ExecuteNonQueryAsync ()
        ()
    }
    
    /// Count all categories for the given web log
    let countAll webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT COUNT(id) FROM category WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        return! count cmd
    }
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "SELECT COUNT(id) FROM category WHERE web_log_id = @webLogId AND parent_id IS NULL"
        addWebLogId cmd webLogId
        return! count cmd
    }
    
    /// Retrieve all categories for the given web log in a DotLiquid-friendly format
    let findAllForView webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM category WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        let cats =
            seq {
                while rdr.Read () do
                    Map.toCategory rdr
            }
            |> Seq.sortBy (fun cat -> cat.Name.ToLowerInvariant ())
            |> List.ofSeq
        do! rdr.CloseAsync ()
        let  ordered = Utils.orderByHierarchy cats None None []
        let! counts  =
            ordered
            |> Seq.map (fun it -> backgroundTask {
                // Parent category post counts include posts in subcategories
                cmd.Parameters.Clear ()
                addWebLogId cmd webLogId
                cmd.CommandText <-
                    "SELECT COUNT(DISTINCT p.id)
                      FROM post p
                           INNER JOIN post_category pc ON pc.post_id = p.id
                     WHERE p.web_log_id = @webLogId
                       AND p.status     = 'Published'
                       AND pc.category_id IN ("
                ordered
                |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
                |> Seq.map (fun cat -> cat.Id)
                |> Seq.append (Seq.singleton it.Id)
                |> Seq.iteri (fun idx item ->
                    if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
                    cmd.CommandText <- $"{cmd.CommandText}@catId{idx}"
                    cmd.Parameters.AddWithValue ($"@catId{idx}", item) |> ignore)
                cmd.CommandText <- $"{cmd.CommandText})"
                let! postCount = count cmd
                return it.Id, postCount
                })
            |> Task.WhenAll
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
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM category WHERE id = @id"
        cmd.Parameters.AddWithValue ("@id", CategoryId.toString catId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return Helpers.verifyWebLog<Category> webLogId (fun c -> c.WebLogId) Map.toCategory rdr
    }
    
    /// Find all categories for the given web log
    let findByWebLog webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM category WHERE web_log_id = @webLogId"
        cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toCategory rdr
    }
    
    /// Delete a category
    let delete catId webLogId = backgroundTask {
        match! findById catId webLogId with
        | Some cat ->
            use cmd = conn.CreateCommand ()
            // Reassign any children to the category's parent category
            cmd.CommandText <- "SELECT COUNT(id) FROM category WHERE parent_id = @parentId"
            cmd.Parameters.AddWithValue ("@parentId", CategoryId.toString catId) |> ignore
            let! children = count cmd
            if children > 0 then
                cmd.CommandText <- "UPDATE category SET parent_id = @newParentId WHERE parent_id = @parentId"
                cmd.Parameters.AddWithValue ("@newParentId", maybe (cat.ParentId |> Option.map CategoryId.toString))
                |> ignore
                do! write cmd
            // Delete the category off all posts where it is assigned, and the category itself
            cmd.CommandText <-
                "DELETE FROM post_category
                  WHERE category_id = @id
                    AND post_id IN (SELECT id FROM post WHERE web_log_id = @webLogId);
                 DELETE FROM category WHERE id = @id"
            cmd.Parameters.Clear ()
            let _ = cmd.Parameters.AddWithValue ("@id", CategoryId.toString catId)
            addWebLogId cmd webLogId
            do! write cmd
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
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "UPDATE category
                SET name        = @name,
                    slug        = @slug,
                    description = @description,
                    parent_id   = @parentId
              WHERE id         = @id
                AND web_log_id = @webLogId"
        addCategoryParameters cmd cat
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
