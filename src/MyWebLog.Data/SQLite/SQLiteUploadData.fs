namespace MyWebLog.Data.SQLite

open System.IO
open BitBadger.Sqlite.FSharp.Documents.WithConn
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog web log data implementation
type SQLiteUploadData(conn: SqliteConnection, log: ILogger) =

    /// Save an uploaded file
    let add (upload: Upload) = backgroundTask {
        log.LogTrace "Upload.add"
        do! Custom.nonQuery
                $"INSERT INTO {Table.Upload} (
                    id, web_log_id, path, updated_on, data
                  ) VALUES (
                    @id, @webLogId, @path, @updatedOn, ZEROBLOB(@dataLength)
                  )"
                [ idParam     upload.Id
                  webLogParam upload.WebLogId
                  sqlParam "@path"       (string upload.Path)
                  sqlParam "@updatedOn"  (instantParam upload.UpdatedOn)
                  sqlParam "@dataLength" upload.Data.Length ]
                conn
        let! rowId =
            Custom.scalar $"SELECT ROWID FROM {Table.Upload} WHERE id = @id" [ idParam upload.Id ] (_.GetInt64(0)) conn
        use dataStream = new MemoryStream(upload.Data)
        use blobStream = new SqliteBlob(conn, Table.Upload, "data", rowId)
        do! dataStream.CopyToAsync blobStream
    }
    
    /// Delete an uploaded file by its ID
    let delete (uploadId: UploadId) webLogId = backgroundTask {
        log.LogTrace "Upload.delete"
        let! upload =
            Custom.single
                $"SELECT id, web_log_id, path, updated_on FROM {Table.Upload} WHERE id = @id AND web_log_id = @webLogId"
                [ idParam uploadId; webLogParam webLogId ]
                (Map.toUpload false)
                conn
        match upload with
        | Some up ->
            do! Custom.nonQuery $"DELETE FROM {Table.Upload} WHERE id = @id" [ idParam up.Id ] conn
            return Ok (string up.Path)
        | None -> return Error $"Upload ID {string uploadId} not found"
    }
    
    /// Find an uploaded file by its path for the given web log
    let findByPath (path: string) webLogId =
        log.LogTrace "Upload.findByPath"
        Custom.single
            $"SELECT *, ROWID FROM {Table.Upload} WHERE web_log_id = @webLogId AND path = @path"
            [ webLogParam webLogId; sqlParam "@path" path ]
            (Map.toUpload true)
            conn
    
    /// Find all uploaded files for the given web log (excludes data)
    let findByWebLog webLogId =
        log.LogTrace "Upload.findByWebLog"
        Custom.list
            $"SELECT id, web_log_id, path, updated_on FROM {Table.Upload} WHERE web_log_id = @webLogId"
            [ webLogParam webLogId ]
            (Map.toUpload false)
            conn
    
    /// Find all uploaded files for the given web log
    let findByWebLogWithData webLogId =
        log.LogTrace "Upload.findByWebLogWithData"
        Custom.list
            $"SELECT *, ROWID FROM {Table.Upload} WHERE web_log_id = @webLogId"
            [ webLogParam webLogId ]
            (Map.toUpload true)
            conn
    
    /// Restore uploads from a backup
    let restore uploads = backgroundTask {
        log.LogTrace "Upload.restore"
        for upload in uploads do do! add upload
    }
    
    interface IUploadData with
        member _.Add upload = add upload
        member _.Delete uploadId webLogId = delete uploadId webLogId
        member _.FindByPath path webLogId = findByPath path webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindByWebLogWithData webLogId = findByWebLogWithData webLogId
        member _.Restore uploads = restore uploads
        