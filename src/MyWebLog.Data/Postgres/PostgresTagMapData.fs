namespace MyWebLog.Data.Postgres

open BitBadger.Npgsql.FSharp.Documents
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql.FSharp

/// PostgreSQL myWebLog tag mapping data implementation        
type PostgresTagMapData (log : ILogger) =
    
    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId =
        log.LogTrace "TagMap.findById"
        Document.findByIdAndWebLog<TagMapId, TagMap> Table.TagMap tagMapId TagMapId.toString webLogId
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        log.LogTrace "TagMap.delete"
        let! exists = Document.existsByWebLog Table.TagMap tagMapId TagMapId.toString webLogId
        if exists then
            do! Delete.byId Table.TagMap (TagMapId.toString tagMapId)
            return true
        else return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue (urlValue : string) webLogId =
        log.LogTrace "TagMap.findByUrlValue"
        Custom.single (selectWithCriteria Table.TagMap)
                      [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with UrlValue = urlValue |} ]
                      fromData<TagMap>

    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        log.LogTrace "TagMap.findByWebLog"
        Custom.list $"{selectWithCriteria Table.TagMap} ORDER BY data ->> 'tag'" [ webLogContains webLogId ]
                    fromData<TagMap>
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags tags webLogId =
        log.LogTrace "TagMap.findMappingForTags"
        let tagSql, tagParam = arrayContains (nameof TagMap.empty.Tag) id tags
        Custom.list $"{selectWithCriteria Table.TagMap} AND {tagSql}" [ webLogContains webLogId; tagParam ]
                    fromData<TagMap>
    
    /// Save a tag mapping
    let save (tagMap : TagMap) =
        save Table.TagMap (TagMapId.toString tagMap.Id) tagMap
    
    /// Restore tag mappings from a backup
    let restore (tagMaps : TagMap list) = backgroundTask {
        let! _ =
            Configuration.dataSource ()
            |> Sql.fromDataSource
            |> Sql.executeTransactionAsync [
                Query.insert Table.TagMap,
                tagMaps |> List.map (fun tagMap -> Query.docParameters (TagMapId.toString tagMap.Id) tagMap)
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
