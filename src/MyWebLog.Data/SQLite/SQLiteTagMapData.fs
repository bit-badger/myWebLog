namespace MyWebLog.Data.SQLite

open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog tag mapping data implementation        
type SQLiteTagMapData (conn : SqliteConnection) =

    /// Find a tag mapping by its ID for the given web log
    let findById (tagMapId: TagMapId) webLogId = backgroundTask {
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT * FROM tag_map WHERE id = @id"
        cmd.Parameters.AddWithValue ("@id", string tagMapId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync()
        return verifyWebLog<TagMap> webLogId (_.WebLogId) Map.toTagMap rdr
    }
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        match! findById tagMapId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand ()
            cmd.CommandText <- "DELETE FROM tag_map WHERE id = @id"
            cmd.Parameters.AddWithValue ("@id", string tagMapId) |> ignore
            do! write cmd
            return true
        | None -> return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue (urlValue : string) webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM tag_map WHERE web_log_id = @webLogId AND url_value = @urlValue"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@urlValue", urlValue) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return if rdr.Read () then Some (Map.toTagMap rdr) else None
    }
    
    /// Get all tag mappings for the given web log
    let findByWebLog webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM tag_map WHERE web_log_id = @webLogId ORDER BY tag"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toTagMap rdr
    }
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags (tags : string list) webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        let mapSql, mapParams = inClause "AND tag" "tag" id tags
        cmd.CommandText <- $"
            SELECT *
               FROM tag_map
              WHERE web_log_id = @webLogId
                {mapSql}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange mapParams
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toTagMap rdr
    }
    
    /// Save a tag mapping
    let save (tagMap : TagMap) = backgroundTask {
        use cmd = conn.CreateCommand ()
        match! findById tagMap.Id tagMap.WebLogId with
        | Some _ ->
            cmd.CommandText <-
                "UPDATE tag_map
                    SET tag       = @tag,
                        url_value = @urlValue
                  WHERE id         = @id
                    AND web_log_id = @webLogId"
        | None ->
            cmd.CommandText <-
                "INSERT INTO tag_map (
                    id, web_log_id, tag, url_value
                ) VALUES (
                    @id, @webLogId, @tag, @urlValue
                )"
        addWebLogId cmd tagMap.WebLogId
        [   cmd.Parameters.AddWithValue ("@id",       string tagMap.Id)
            cmd.Parameters.AddWithValue ("@tag",      tagMap.Tag)
            cmd.Parameters.AddWithValue ("@urlValue", tagMap.UrlValue)
        ] |> ignore
        do! write cmd
    }
    
    /// Restore tag mappings from a backup
    let restore tagMaps = backgroundTask {
        for tagMap in tagMaps do
            do! save tagMap
    }
    
    interface ITagMapData with
        member _.Delete tagMapId webLogId = delete tagMapId webLogId
        member _.FindById tagMapId webLogId = findById tagMapId webLogId
        member _.FindByUrlValue urlValue webLogId = findByUrlValue urlValue webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindMappingForTags tags webLogId = findMappingForTags tags webLogId
        member _.Save tagMap = save tagMap
        member _.Restore tagMaps = restore tagMaps
