namespace MyWebLog.Data.SQLite

open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog tag mapping data implementation        
type SQLiteTagMapData (conn : SqliteConnection) =

    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM tag_map WHERE id = @id"
        cmd.Parameters.AddWithValue ("@id", TagMapId.toString tagMapId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return Helpers.verifyWebLog<TagMap> webLogId (fun tm -> tm.webLogId) Map.toTagMap rdr
    }
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        match! findById tagMapId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand ()
            cmd.CommandText <- "DELETE FROM tag_map WHERE id = @id"
            cmd.Parameters.AddWithValue ("@id", TagMapId.toString tagMapId) |> ignore
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
        cmd.CommandText <-
            """SELECT *
                 FROM tag_map
                WHERE web_log_id = @webLogId
                  AND tag IN ("""
        tags
        |> List.iteri (fun idx tag ->
            if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
            cmd.CommandText <- $"{cmd.CommandText}@tag{idx}"
            cmd.Parameters.AddWithValue ($"@tag{idx}", tag) |> ignore)
        cmd.CommandText <- $"{cmd.CommandText})"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toTagMap rdr
    }
    
    /// Save a tag mapping
    let save (tagMap : TagMap) = backgroundTask {
        use cmd = conn.CreateCommand ()
        match! findById tagMap.id tagMap.webLogId with
        | Some _ ->
            cmd.CommandText <-
                """UPDATE tag_map
                      SET tag       = @tag,
                          url_value = @urlValue
                    WHERE id         = @id
                      AND web_log_id = @webLogId"""
        | None ->
            cmd.CommandText <-
                """INSERT INTO tag_map (
                       id, web_log_id, tag, url_value
                   ) VALUES (
                       @id, @webLogId, @tag, @urlValue
                   )"""
        addWebLogId cmd tagMap.webLogId
        [ cmd.Parameters.AddWithValue ("@id", TagMapId.toString tagMap.id)
          cmd.Parameters.AddWithValue ("@tag", tagMap.tag)
          cmd.Parameters.AddWithValue ("@urlValue", tagMap.urlValue)
        ] |> ignore
        do! write cmd
    }
    
    /// Restore tag mappings from a backup
    let restore tagMaps = backgroundTask {
        for tagMap in tagMaps do
            do! save tagMap
    }
    
    interface ITagMapData with
        member _.delete tagMapId webLogId = delete tagMapId webLogId
        member _.findById tagMapId webLogId = findById tagMapId webLogId
        member _.findByUrlValue urlValue webLogId = findByUrlValue urlValue webLogId
        member _.findByWebLog webLogId = findByWebLog webLogId
        member _.findMappingForTags tags webLogId = findMappingForTags tags webLogId
        member _.save tagMap = save tagMap
        member this.restore tagMaps = restore tagMaps
