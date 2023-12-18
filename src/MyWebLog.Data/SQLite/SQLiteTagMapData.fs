namespace MyWebLog.Data.SQLite

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json

/// SQLite myWebLog tag mapping data implementation
type SQLiteTagMapData(conn: SqliteConnection, ser: JsonSerializer, log: ILogger) =

    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId =
        log.LogTrace "TagMap.findById"
        Document.findByIdAndWebLog<TagMapId, TagMap> conn ser Table.TagMap tagMapId webLogId
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        log.LogTrace "TagMap.delete"
        match! findById tagMapId webLogId with
        | Some _ ->
            do! Document.delete conn Table.TagMap tagMapId
            return true
        | None -> return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue (urlValue: string) webLogId = backgroundTask {
        log.LogTrace "TagMap.findByUrlValue"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            {Query.selectFromTable Table.TagMap}
             WHERE {Query.whereByWebLog}
               AND data ->> '{nameof TagMap.Empty.UrlValue}' = @urlValue"
        addWebLogId cmd webLogId
        addParam cmd "@urlValue" urlValue
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.fromDoc<TagMap> ser rdr) else None
    }
    
    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        log.LogTrace "TagMap.findByWebLog"
        Document.findByWebLog<TagMap> conn ser Table.TagMap webLogId
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags (tags: string list) webLogId =
        log.LogTrace "TagMap.findMappingForTags"
        use cmd = conn.CreateCommand ()
        let mapSql, mapParams = inClause $"AND data ->> '{nameof TagMap.Empty.Tag}'" "tag" id tags
        cmd.CommandText <- $"{Query.selectFromTable Table.TagMap} WHERE {Query.whereByWebLog} {mapSql}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange mapParams
        cmdToList<TagMap> cmd ser
    
    /// Save a tag mapping
    let save (tagMap: TagMap) = backgroundTask {
        log.LogTrace "TagMap.save"
        match! findById tagMap.Id tagMap.WebLogId with
        | Some _ -> do! Document.update conn ser Table.TagMap tagMap.Id tagMap
        | None -> do! Document.insert conn ser Table.TagMap tagMap
    }
    
    /// Restore tag mappings from a backup
    let restore tagMaps = backgroundTask {
        log.LogTrace "TagMap.restore"
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
