namespace MyWebLog.Data.Postgres

open BitBadger.Documents
open BitBadger.Documents.Postgres
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql.FSharp

/// PostgreSQL myWebLog tag mapping data implementation
type PostgresTagMapData(log: ILogger) =
    
    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId =
        log.LogTrace "TagMap.findById"
        Document.findByIdAndWebLog<TagMapId, TagMap> Table.TagMap tagMapId webLogId
    
    /// Delete a tag mapping for the given web log
    let delete (tagMapId: TagMapId) webLogId = backgroundTask {
        log.LogTrace "TagMap.delete"
        let! exists = Document.existsByWebLog Table.TagMap tagMapId webLogId
        if exists then
            do! Delete.byId Table.TagMap tagMapId
            return true
        else return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue (urlValue: string) webLogId =
        log.LogTrace "TagMap.findByUrlValue"
        Find.firstByContains<TagMap> Table.TagMap {| webLogDoc webLogId with UrlValue = urlValue |}

    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        log.LogTrace "TagMap.findByWebLog"
        Custom.list
            $"{selectWithCriteria Table.TagMap} ORDER BY data ->> 'tag'"
            [ webLogContains webLogId ]
            fromData<TagMap>
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags tags webLogId =
        log.LogTrace "TagMap.findMappingForTags"
        let tagSql, tagParam = arrayContains (nameof TagMap.Empty.Tag) id tags
        Custom.list
            $"{selectWithCriteria Table.TagMap} AND {tagSql}"
            [ webLogContains webLogId; tagParam ]
            fromData<TagMap>
    
    /// Save a tag mapping
    let save (tagMap: TagMap) =
        log.LogTrace "TagMap.save"
        save Table.TagMap tagMap
    
    /// Restore tag mappings from a backup
    let restore (tagMaps: TagMap list) = backgroundTask {
        let! _ =
            Configuration.dataSource ()
            |> Sql.fromDataSource
            |> Sql.executeTransactionAsync
                [ Query.insert Table.TagMap,
                    tagMaps |> List.map (fun tagMap -> [ jsonParam "@data" tagMap ]) ]
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
