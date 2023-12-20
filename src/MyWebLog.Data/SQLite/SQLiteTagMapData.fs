namespace MyWebLog.Data.SQLite

open BitBadger.Sqlite.FSharp.Documents
open BitBadger.Sqlite.FSharp.Documents.WithConn
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog tag mapping data implementation
type SQLiteTagMapData(conn: SqliteConnection, log: ILogger) =

    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId =
        log.LogTrace "TagMap.findById"
        Document.findByIdAndWebLog<TagMapId, TagMap> Table.TagMap tagMapId webLogId conn
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        log.LogTrace "TagMap.delete"
        match! findById tagMapId webLogId with
        | Some _ ->
            do! Delete.byId Table.TagMap tagMapId conn
            return true
        | None -> return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue (urlValue: string) webLogId =
        log.LogTrace "TagMap.findByUrlValue"
        Custom.single
            $"""{Document.Query.selectByWebLog Table.TagMap}
                  AND {Query.whereFieldEquals (nameof TagMap.Empty.UrlValue) "@urlValue"}"""
            [ webLogParam webLogId; SqliteParameter("@urlValue", urlValue) ]
            fromData<TagMap>
            conn
    
    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        log.LogTrace "TagMap.findByWebLog"
        Document.findByWebLog<TagMap> Table.TagMap webLogId conn
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags (tags: string list) webLogId =
        log.LogTrace "TagMap.findMappingForTags"
        let mapSql, mapParams = inClause $"AND data ->> '{nameof TagMap.Empty.Tag}'" "tag" id tags
        Custom.list
            $"{Document.Query.selectByWebLog Table.TagMap} {mapSql}"
            (webLogParam webLogId :: mapParams)
            fromData<TagMap>
            conn
    
    /// Save a tag mapping
    let save (tagMap: TagMap) =
        log.LogTrace "TagMap.save"
        save Table.TagMap tagMap conn
    
    /// Restore tag mappings from a backup
    let restore tagMaps = backgroundTask {
        log.LogTrace "TagMap.restore"
        for tagMap in tagMaps do do! save tagMap
    }
    
    interface ITagMapData with
        member _.Delete tagMapId webLogId = delete tagMapId webLogId
        member _.FindById tagMapId webLogId = findById tagMapId webLogId
        member _.FindByUrlValue urlValue webLogId = findByUrlValue urlValue webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindMappingForTags tags webLogId = findMappingForTags tags webLogId
        member _.Save tagMap = save tagMap
        member _.Restore tagMaps = restore tagMaps
