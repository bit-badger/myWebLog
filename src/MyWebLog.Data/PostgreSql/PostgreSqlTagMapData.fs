namespace MyWebLog.Data.PostgreSql

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog tag mapping data implementation        
type PostgreSqlTagMapData (conn : NpgsqlConnection) =

    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId = backgroundTask {
        let! tagMap =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM tag_map WHERE id = @id AND web_log_id = @webLogId"
            |> Sql.parameters [ "@id", Sql.string (TagMapId.toString tagMapId); webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toTagMap
        return List.tryHead tagMap
    }
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        match! findById tagMapId webLogId with
        | Some _ ->
            let! _ =
                Sql.existingConnection conn
                |> Sql.query "DELETE FROM tag_map WHERE id = @id"
                |> Sql.parameters [ "@id", Sql.string (TagMapId.toString tagMapId) ]
                |> Sql.executeNonQueryAsync
            return true
        | None -> return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue urlValue webLogId = backgroundTask {
        let! tagMap =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM tag_map WHERE web_log_id = @webLogId AND url_value = @urlValue"
            |> Sql.parameters [ webLogIdParam webLogId; "@urlValue", Sql.string urlValue ]
            |> Sql.executeAsync Map.toTagMap
        return List.tryHead tagMap
    }
    
    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM tag_map WHERE web_log_id = @webLogId ORDER BY tag"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync Map.toTagMap
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags tags webLogId =
        let tagSql, tagParams = inClause "tag" id tags
        Sql.existingConnection conn
        |> Sql.query $"SELECT * FROM tag_map WHERE web_log_id = @webLogId AND tag IN ({tagSql}"
        |> Sql.parameters (webLogIdParam webLogId :: tagParams)
        |> Sql.executeAsync Map.toTagMap
    
    /// The INSERT statement for a tag mapping
    let tagMapInsert = """
        INSERT INTO tag_map (
            id, web_log_id, tag, url_value
        ) VALUES (
            @id, @webLogId, @tag, @urlValue
        )"""
    
    /// The parameters for saving a tag mapping
    let tagMapParams (tagMap : TagMap) = [
        webLogIdParam tagMap.WebLogId
        "@id",       Sql.string (TagMapId.toString tagMap.Id)
        "@tag",      Sql.string tagMap.Tag
        "@urlValue", Sql.string tagMap.UrlValue
    ]
    
    /// Save a tag mapping
    let save tagMap = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"""
                {tagMapInsert} ON CONFLICT (id) DO UPDATE
                SET tag       = EXCLUDED.tag,
                    url_value = EXCLUDED.url_value"""
            |> Sql.parameters (tagMapParams tagMap)
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Restore tag mappings from a backup
    let restore tagMaps = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                tagMapInsert, tagMaps |> List.map tagMapParams
            ]
        ()
    }
    
    interface ITagMapData with
        member _.Delete tagMapId webLogId = delete tagMapId webLogId
        member _.FindById tagMapId webLogId = findById tagMapId webLogId
        member _.FindByUrlValue urlValue webLogId = findByUrlValue urlValue webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindMappingForTags tags webLogId = findMappingForTags tags webLogId
        member _.Save tagMap = save tagMap
        member _.Restore tagMaps = restore tagMaps
