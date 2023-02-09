namespace MyWebLog.Data.Postgres

open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog uploaded file data implementation        
type PostgresUploadData (source : NpgsqlDataSource, log : ILogger) =

    /// The INSERT statement for an uploaded file
    let upInsert = $"
        INSERT INTO {Table.Upload} (
            id, web_log_id, path, updated_on, data
        ) VALUES (
            @id, @webLogId, @path, @updatedOn, @data
        )"
    
    /// Parameters for adding an uploaded file
    let upParams (upload : Upload) = [
        webLogIdParam upload.WebLogId
        typedParam "updatedOn" upload.UpdatedOn
        "@id",   Sql.string (UploadId.toString upload.Id)
        "@path", Sql.string (Permalink.toString upload.Path)
        "@data", Sql.bytea  upload.Data
    ]
    
    /// Save an uploaded file
    let add upload = backgroundTask {
        log.LogTrace "Upload.add"
        let! _ =
            Sql.fromDataSource source
            |> Sql.query upInsert
            |> Sql.parameters (upParams upload)
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Delete an uploaded file by its ID
    let delete uploadId webLogId = backgroundTask {
        log.LogTrace "Upload.delete"
        let idParam = [ "@id", Sql.string (UploadId.toString uploadId) ]
        let! path =
            Sql.fromDataSource source
            |> Sql.query $"SELECT path FROM {Table.Upload} WHERE id = @id AND web_log_id = @webLogId"
            |> Sql.parameters (webLogIdParam webLogId :: idParam)
            |> Sql.executeAsync (fun row -> row.string "path")
            |> tryHead
        if Option.isSome path then
            let! _ =
                Sql.fromDataSource source
                |> Sql.query $"DELETE FROM {Table.Upload} WHERE id = @id"
                |> Sql.parameters idParam
                |> Sql.executeNonQueryAsync
            return Ok path.Value
        else return Error $"""Upload ID {UploadId.toString uploadId} not found"""
    }
    
    /// Find an uploaded file by its path for the given web log
    let findByPath path webLogId =
        log.LogTrace "Upload.findByPath"
        Sql.fromDataSource source
        |> Sql.query $"SELECT * FROM {Table.Upload} WHERE web_log_id = @webLogId AND path = @path"
        |> Sql.parameters [ webLogIdParam webLogId; "@path", Sql.string path ]
        |> Sql.executeAsync (Map.toUpload true)
        |> tryHead
    
    /// Find all uploaded files for the given web log (excludes data)
    let findByWebLog webLogId =
        log.LogTrace "Upload.findByWebLog"
        Sql.fromDataSource source
        |> Sql.query $"SELECT id, web_log_id, path, updated_on FROM {Table.Upload} WHERE web_log_id = @webLogId"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync (Map.toUpload false)
    
    /// Find all uploaded files for the given web log
    let findByWebLogWithData webLogId =
        log.LogTrace "Upload.findByWebLogWithData"
        Sql.fromDataSource source
        |> Sql.query $"SELECT * FROM {Table.Upload} WHERE web_log_id = @webLogId"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync (Map.toUpload true)
    
    /// Restore uploads from a backup
    let restore uploads = backgroundTask {
        log.LogTrace "Upload.restore"
        for batch in uploads |> List.chunkBySize 5 do
            let! _ =
                Sql.fromDataSource source
                |> Sql.executeTransactionAsync [ upInsert, batch |> List.map upParams ]
            ()
    }
    
    interface IUploadData with
        member _.Add upload = add upload
        member _.Delete uploadId webLogId = delete uploadId webLogId
        member _.FindByPath path webLogId = findByPath path webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindByWebLogWithData webLogId = findByWebLogWithData webLogId
        member _.Restore uploads = restore uploads
        