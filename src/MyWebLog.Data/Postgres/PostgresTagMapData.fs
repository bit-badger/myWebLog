namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog tag mapping data implementation        
type PostgresTagMapData (conn : NpgsqlConnection, ser : JsonSerializer) =
    
    /// Map a data row to a tag mapping
    let toTagMap = Map.fromDoc<TagMap> ser
    
    /// Find a tag mapping by its ID for the given web log
    let findById tagMapId webLogId =
        Document.findByIdAndWebLog conn Table.TagMap tagMapId TagMapId.toString webLogId toTagMap
    
    /// Delete a tag mapping for the given web log
    let delete tagMapId webLogId = backgroundTask {
        let! exists = Document.existsByWebLog conn Table.TagMap tagMapId TagMapId.toString webLogId
        if exists then
            do! Document.delete conn Table.TagMap (TagMapId.toString tagMapId)
            return true
        else return false
    }
    
    /// Find a tag mapping by its URL value for the given web log
    let findByUrlValue urlValue webLogId =
        Sql.existingConnection conn
        |> Sql.query $"{docSelectForWebLogSql Table.TagMap} AND data ->> '{nameof TagMap.empty.UrlValue}' = @urlValue"
        |> Sql.parameters [ webLogIdParam webLogId; "@urlValue", Sql.string urlValue ]
        |> Sql.executeAsync toTagMap
        |> tryHead
    
    /// Get all tag mappings for the given web log
    let findByWebLog webLogId =
        Document.findByWebLog conn Table.TagMap webLogId toTagMap (Some "ORDER BY tag")
    
    /// Find any tag mappings in a list of tags for the given web log
    let findMappingForTags tags webLogId =
        let tagSql, tagParams = jsonArrayInClause (nameof TagMap.empty.Tag) id tags
        Sql.existingConnection conn
        |> Sql.query $"{docSelectForWebLogSql Table.TagMap} AND ({tagSql})"
        |> Sql.parameters (webLogIdParam webLogId :: tagParams)
        |> Sql.executeAsync toTagMap
    
    /// The parameters for saving a tag mapping
    let tagMapParams (tagMap : TagMap) = [
        "@id",   Sql.string (TagMapId.toString tagMap.Id)
        "@data", Sql.jsonb  (Utils.serialize ser tagMap)
    ]
    
    /// Save a tag mapping
    let save tagMap = backgroundTask {
        do! Document.upsert conn Table.TagMap tagMapParams tagMap
    }
    
    /// Restore tag mappings from a backup
    let restore tagMaps = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                docInsertSql Table.TagMap, tagMaps |> List.map tagMapParams
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
