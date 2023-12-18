namespace MyWebLog.Data.SQLite

open System.IO
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog web log data implementation
type SQLiteUploadData(conn: SqliteConnection, log: ILogger) =

    /// Add parameters for uploaded file INSERT and UPDATE statements
    let addUploadParameters (cmd: SqliteCommand) (upload: Upload) =
        addParam cmd "@id"         (string upload.Id)
        addParam cmd "@webLogId"   (string upload.WebLogId)
        addParam cmd "@path"       (string upload.Path)
        addParam cmd "@updatedOn"  (instantParam upload.UpdatedOn)
        addParam cmd "@dataLength" upload.Data.Length
    
    /// Save an uploaded file
    let add upload = backgroundTask {
        log.LogTrace "Upload.add"
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
            $"INSERT INTO {Table.Upload} (
                id, web_log_id, path, updated_on, data
              ) VALUES (
                @id, @webLogId, @path, @updatedOn, ZEROBLOB(@dataLength)
              )"
        addUploadParameters cmd upload
        do! write cmd
        
        cmd.CommandText <- $"SELECT ROWID FROM {Table.Upload} WHERE id = @id"
        let! rowId = cmd.ExecuteScalarAsync()
        
        use dataStream = new MemoryStream(upload.Data)
        use blobStream = new SqliteBlob(conn, Table.Upload, "data", rowId :?> int64)
        do! dataStream.CopyToAsync blobStream
    }
    
    /// Delete an uploaded file by its ID
    let delete (uploadId: UploadId) webLogId = backgroundTask {
        log.LogTrace "Upload.delete"
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
            $"SELECT id, web_log_id, path, updated_on
                FROM {Table.Upload}
               WHERE id         = @id
                 AND web_log_id = @webLogId"
        addWebLogId cmd webLogId
        addDocId cmd uploadId
        let! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        if isFound then
            let upload = Map.toUpload false rdr
            do! rdr.CloseAsync()
            cmd.CommandText <- $"DELETE FROM {Table.Upload} WHERE id = @id AND web_log_id = @webLogId"
            do! write cmd
            return Ok (string upload.Path)
        else
            return Error $"""Upload ID {cmd.Parameters["@id"].Value} not found"""
    }
    
    /// Find an uploaded file by its path for the given web log
    let findByPath (path: string) webLogId = backgroundTask {
        log.LogTrace "Upload.findByPath"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"SELECT *, ROWID FROM {Table.Upload} WHERE web_log_id = @webLogId AND path = @path"
        addWebLogId cmd webLogId
        addParam cmd "@path" path
        let! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.toUpload true rdr) else None
    }
    
    /// Find all uploaded files for the given web log (excludes data)
    let findByWebLog webLogId = backgroundTask {
        log.LogTrace "Upload.findByWebLog"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"SELECT id, web_log_id, path, updated_on FROM {Table.Upload} WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        let! rdr = cmd.ExecuteReaderAsync()
        return toList (Map.toUpload false) rdr
    }
    
    /// Find all uploaded files for the given web log
    let findByWebLogWithData webLogId = backgroundTask {
        log.LogTrace "Upload.findByWebLogWithData"
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"SELECT *, ROWID FROM {Table.Upload} WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        let! rdr = cmd.ExecuteReaderAsync()
        return toList (Map.toUpload true) rdr
    }
    
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
        