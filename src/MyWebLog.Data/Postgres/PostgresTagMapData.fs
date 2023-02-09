namespace MyWebLog.Data.Postgres

open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog tag mapping data implementation        
type PostgresTagMapData (source : NpgsqlDataSource, log : ILogger) =
    
    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId =
        log.LogTrace "TagMap.findById"
        Document.findByIdAndWebLog<TagMapId, TagMap> source Table.TagMap tagMapId TagMapId.toString webLogId
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        log.LogTrace "TagMap.delete"
        let! exists = Document.existsByWebLog source Table.TagMap tagMapId TagMapId.toString webLogId
        if exists then
            do! Sql.fromDataSource source |> Query.deleteById Table.TagMap (TagMapId.toString tagMapId)
            return true
        else return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue (urlValue : string) webLogId =
        log.LogTrace "TagMap.findByUrlValue"
        Sql.fromDataSource source
        |> Sql.query (selectWithCriteria Table.TagMap)
        |> Sql.parameters [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with UrlValue = urlValue |} ]
        |> Sql.executeAsync fromData<TagMap>
        |> tryHead
    
    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        log.LogTrace "TagMap.findByWebLog"
        Sql.fromDataSource source
        |> Sql.query $"{selectWithCriteria Table.TagMap} ORDER BY data ->> 'tag'"
        |> Sql.parameters [ webLogContains webLogId ]
        |> Sql.executeAsync fromData<TagMap>
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags tags webLogId =
        log.LogTrace "TagMap.findMappingForTags"
        let tagSql, tagParam = arrayContains (nameof TagMap.empty.Tag) id tags
        Sql.fromDataSource source
        |> Sql.query $"{selectWithCriteria Table.TagMap} AND {tagSql}"
        |> Sql.parameters [ webLogContains webLogId; tagParam ]
        |> Sql.executeAsync fromData<TagMap>
    
    /// Save a tag mapping
    let save (tagMap : TagMap) = backgroundTask {
        do! Sql.fromDataSource source |> Query.save Table.TagMap (TagMapId.toString tagMap.Id) tagMap
    }
    
    /// Restore tag mappings from a backup
    let restore (tagMaps : TagMap list) = backgroundTask {
        let! _ =
            Sql.fromDataSource source
            |> Sql.executeTransactionAsync [
                Query.insertQuery Table.TagMap,
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
