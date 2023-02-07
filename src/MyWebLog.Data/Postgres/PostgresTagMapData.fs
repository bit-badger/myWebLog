namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog tag mapping data implementation        
type PostgresTagMapData (source : NpgsqlDataSource) =
    
    /// Shorthand for turning a web log ID into a string
    let wls = WebLogId.toString

    /// A query to select tag map(s) by JSON document containment criteria
    let tagMapByCriteria =
        $"""{Query.selectFromTable Table.TagMap} WHERE {Query.whereDataContains "@criteria"}"""
    
    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId =
        Document.findByIdAndWebLog<TagMapId, TagMap> source Table.TagMap tagMapId TagMapId.toString webLogId
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        let! exists = Document.existsByWebLog source Table.TagMap tagMapId TagMapId.toString webLogId
        if exists then
            do! Sql.fromDataSource source |> Query.deleteById Table.TagMap (TagMapId.toString tagMapId)
            return true
        else return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue (urlValue : string) webLogId =
        Sql.fromDataSource source
        |> Sql.query tagMapByCriteria
        |> Sql.parameters [ "@criteria", Query.jsonbDocParam {| WebLogId = wls webLogId; UrlValue = urlValue |} ]
        |> Sql.executeAsync fromData<TagMap>
        |> tryHead
    
    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        Sql.fromDataSource source
        |> Sql.query $"{tagMapByCriteria} ORDER BY data->>'tag'"
        |> Sql.parameters [ "@criteria", webLogContains webLogId ]
        |> Sql.executeAsync fromData<TagMap>
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags tags webLogId =
        let tagSql, tagParams = jsonArrayInClause (nameof TagMap.empty.Tag) id tags
        Sql.fromDataSource source
        |> Sql.query $"{tagMapByCriteria} AND ({tagSql})"
        |> Sql.parameters (("@criteria", webLogContains webLogId) :: tagParams)
        |> Sql.executeAsync fromData<TagMap>
    
    /// The parameters for saving a tag mapping
    let tagMapParams (tagMap : TagMap) =
        Query.docParameters (TagMapId.toString tagMap.Id) tagMap
    
    /// Save a tag mapping
    let save (tagMap : TagMap) = backgroundTask {
        do! Sql.fromDataSource source |> Query.save Table.TagMap (TagMapId.toString tagMap.Id) tagMap
    }
    
    /// Restore tag mappings from a backup
    let restore tagMaps = backgroundTask {
        let! _ =
            Sql.fromDataSource source
            |> Sql.executeTransactionAsync [
                Query.insertQuery Table.TagMap, tagMaps |> List.map tagMapParams
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
