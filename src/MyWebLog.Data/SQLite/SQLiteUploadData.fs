namespace MyWebLog.Data.SQLite

open System.IO
open BitBadger.Documents.Sqlite
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog web log data implementation
type SQLiteUploadData(conn: SqliteConnection, log: ILogger) =

    /// Save an uploaded file
    let add (upload: Upload) = backgroundTask {
        log.LogTrace "Upload.add"
        do! conn.customNonQuery
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
        let! rowId =
            conn.customScalar $"SELECT ROWID FROM {Table.Upload} WHERE id = @id" [ idParam upload.Id ] _.GetInt64(0)
        use dataStream = new MemoryStream(upload.Data)
        use blobStream = new SqliteBlob(conn, Table.Upload, "data", rowId)
        do! dataStream.CopyToAsync blobStream
    }
    
    /// Delete an uploaded file by its ID
    let delete (uploadId: UploadId) webLogId = backgroundTask {
        log.LogTrace "Upload.delete"
        let! upload =
            conn.customSingle
                $"SELECT id, web_log_id, path, updated_on FROM {Table.Upload} WHERE id = @id AND web_log_id = @webLogId"
                [ idParam uploadId; webLogParam webLogId ]
                (Map.toUpload false)
        match upload with
        | Some up ->
            do! conn.customNonQuery $"DELETE FROM {Table.Upload} WHERE id = @id" [ idParam up.Id ]
            return Ok (string up.Path)
        | None -> return Error $"Upload ID {string uploadId} not found"
    }
    
    /// Find an uploaded file by its path for the given web log
    let findByPath (path: string) webLogId =
        log.LogTrace "Upload.findByPath"
        conn.customSingle
            $"SELECT *, ROWID FROM {Table.Upload} WHERE web_log_id = @webLogId AND path = @path"
            [ webLogParam webLogId; sqlParam "@path" path ]
            (Map.toUpload true)
    
    /// Find all uploaded files for the given web log (excludes data)
    let findByWebLog webLogId =
        log.LogTrace "Upload.findByWebLog"
        conn.customList
            $"SELECT id, web_log_id, path, updated_on FROM {Table.Upload} WHERE web_log_id = @webLogId"
            [ webLogParam webLogId ]
            (Map.toUpload false)
    
    /// Find all uploaded files for the given web log
    let findByWebLogWithData webLogId =
        log.LogTrace "Upload.findByWebLogWithData"
        conn.customList
            $"SELECT *, ROWID FROM {Table.Upload} WHERE web_log_id = @webLogId"
            [ webLogParam webLogId ]
            (Map.toUpload true)
    
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
        