namespace MyWebLog.Data.SQLite

open System.IO
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog web log data implementation        
type SQLiteUploadData (conn : SqliteConnection) =

    /// Add parameters for uploaded file INSERT and UPDATE statements
    let addUploadParameters (cmd : SqliteCommand) (upload : Upload) =
        [   cmd.Parameters.AddWithValue ("@id", UploadId.toString upload.Id)
            cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString upload.WebLogId)
            cmd.Parameters.AddWithValue ("@path", Permalink.toString upload.Path)
            cmd.Parameters.AddWithValue ("@updatedOn", upload.UpdatedOn)
            cmd.Parameters.AddWithValue ("@dataLength", upload.Data.Length)
        ] |> ignore
    
    /// Save an uploaded file
    let add upload = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            INSERT INTO upload (
                id, web_log_id, path, updated_on, data
            ) VALUES (
                @id, @webLogId, @path, @updatedOn, ZEROBLOB(@dataLength)
            )"""
        addUploadParameters cmd upload
        do! write cmd
        
        cmd.CommandText <- "SELECT ROWID FROM upload WHERE id = @id"
        let! rowId = cmd.ExecuteScalarAsync ()
        
        use dataStream = new MemoryStream (upload.Data)
        use blobStream = new SqliteBlob (conn, "upload", "data", rowId :?> int64)
        do! dataStream.CopyToAsync blobStream
    }
    
    /// Delete an uploaded file by its ID
    let delete uploadId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            SELECT id, web_log_id, path, updated_on
              FROM upload
             WHERE id         = @id
               AND web_log_id = @webLogId"""
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@id", UploadId.toString uploadId) |> ignore
        let! rdr = cmd.ExecuteReaderAsync ()
        if (rdr.Read ()) then
            let upload = Map.toUpload false rdr
            do! rdr.CloseAsync ()
            cmd.CommandText <- "DELETE FROM upload WHERE id = @id AND web_log_id = @webLogId"
            do! write cmd
            return Ok (Permalink.toString upload.Path)
        else
            return Error $"""Upload ID {cmd.Parameters["@id"]} not found"""
    }
    
    /// Find an uploaded file by its path for the given web log
    let findByPath (path : string) webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT *, ROWID FROM upload WHERE web_log_id = @webLogId AND path = @path"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@path", path) |> ignore
        let! rdr = cmd.ExecuteReaderAsync ()
        return if rdr.Read () then Some (Map.toUpload true rdr) else None
    }
    
    /// Find all uploaded files for the given web log (excludes data)
    let findByWebLog webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT id, web_log_id, path, updated_on FROM upload WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        let! rdr = cmd.ExecuteReaderAsync ()
        return toList (Map.toUpload false) rdr
    }
    
    /// Find all uploaded files for the given web log
    let findByWebLogWithData webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT *, ROWID FROM upload WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        let! rdr = cmd.ExecuteReaderAsync ()
        return toList (Map.toUpload true) rdr
    }
    
    /// Restore uploads from a backup
    let restore uploads = backgroundTask {
        for upload in uploads do do! add upload
    }
    
    interface IUploadData with
        member _.Add upload = add upload
        member _.Delete uploadId webLogId = delete uploadId webLogId
        member _.FindByPath path webLogId = findByPath path webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindByWebLogWithData webLogId = findByWebLogWithData webLogId
        member _.Restore uploads = restore uploads
        