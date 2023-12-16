namespace MyWebLog.Data.Postgres

open BitBadger.Npgsql.FSharp.Documents
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql.FSharp

/// PostgreSQL myWebLog uploaded file data implementation        
type PostgresUploadData (log : ILogger) =

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
        "@path", Sql.string upload.Path.Value
        "@data", Sql.bytea  upload.Data
    ]
    
    /// Save an uploaded file
    let add upload =
        log.LogTrace "Upload.add"
        Custom.nonQuery upInsert (upParams upload)
    
    /// Delete an uploaded file by its ID
    let delete uploadId webLogId = backgroundTask {
        log.LogTrace "Upload.delete"
        let idParam = [ "@id", Sql.string (UploadId.toString uploadId) ]
        let! path =
            Custom.single $"SELECT path FROM {Table.Upload} WHERE id = @id AND web_log_id = @webLogId"
                          (webLogIdParam webLogId :: idParam) (fun row -> row.string "path")
        if Option.isSome path then
            do! Custom.nonQuery (Query.Delete.byId Table.Upload) idParam
            return Ok path.Value
        else return Error $"""Upload ID {UploadId.toString uploadId} not found"""
    }
    
    /// Find an uploaded file by its path for the given web log
    let findByPath path webLogId =
        log.LogTrace "Upload.findByPath"
        Custom.single $"SELECT * FROM {Table.Upload} WHERE web_log_id = @webLogId AND path = @path"
                      [ webLogIdParam webLogId; "@path", Sql.string path ] (Map.toUpload true)
    
    /// Find all uploaded files for the given web log (excludes data)
    let findByWebLog webLogId =
        log.LogTrace "Upload.findByWebLog"
        Custom.list $"SELECT id, web_log_id, path, updated_on FROM {Table.Upload} WHERE web_log_id = @webLogId"
                    [ webLogIdParam webLogId ] (Map.toUpload false)
    
    /// Find all uploaded files for the given web log
    let findByWebLogWithData webLogId =
        log.LogTrace "Upload.findByWebLogWithData"
        Custom.list $"SELECT * FROM {Table.Upload} WHERE web_log_id = @webLogId" [ webLogIdParam webLogId ]
                    (Map.toUpload true)
    
    /// Restore uploads from a backup
    let restore uploads = backgroundTask {
        log.LogTrace "Upload.restore"
        for batch in uploads |> List.chunkBySize 5 do
            let! _ =
                Configuration.dataSource ()
                |> Sql.fromDataSource
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
        