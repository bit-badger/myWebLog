namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog web log data implementation        
type PostgresWebLogData (conn : NpgsqlConnection, ser : JsonSerializer) =
    
    // SUPPORT FUNCTIONS
    
    /// Map a data row to a web log
    let toWebLog = Map.fromDoc<WebLog> ser
    
    /// The parameters for web log INSERT or UPDATE statements
    let webLogParams (webLog : WebLog) = [
        "@id",   Sql.string (WebLogId.toString webLog.Id)
        "@data", Sql.jsonb  (Utils.serialize ser webLog)
    ]
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a web log
    let add webLog = backgroundTask {
        do! Document.insert conn Table.WebLog webLogParams webLog
    }
    
    /// Retrieve all web logs
    let all () =
        Sql.existingConnection conn
        |> Sql.query $"SELECT * FROM {Table.WebLog}"
        |> Sql.executeAsync toWebLog
    
    /// Delete a web log by its ID
    let delete webLogId = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"
                DELETE FROM {Table.PostComment}
                 WHERE data ->> '{nameof Comment.empty.PostId}' IN (SELECT id FROM {Table.Post} WHERE {webLogWhere});
                DELETE FROM {Table.Post}        WHERE {webLogWhere};
                DELETE FROM {Table.Page}        WHERE {webLogWhere};
                DELETE FROM {Table.Category}    WHERE {webLogWhere};
                DELETE FROM {Table.TagMap}      WHERE {webLogWhere};
                DELETE FROM {Table.Upload}      WHERE web_log_id = @webLogId;
                DELETE FROM {Table.WebLogUser}  WHERE {webLogWhere};
                DELETE FROM {Table.WebLog}      WHERE id = @webLogId"
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Find a web log by its host (URL base)
    let findByHost url =
        Sql.existingConnection conn
        |> Sql.query $"SELECT * FROM {Table.WebLog} WHERE data ->> '{nameof WebLog.empty.UrlBase}' = @urlBase"
        |> Sql.parameters [ "@urlBase", Sql.string url ]
        |> Sql.executeAsync toWebLog
        |> tryHead
    
    /// Find a web log by its ID
    let findById webLogId = 
        Document.findById conn Table.WebLog webLogId WebLogId.toString toWebLog
    
    /// Update settings for a web log
    let updateSettings webLog = backgroundTask {
        do! Document.update conn Table.WebLog webLogParams webLog
    }
    
    /// Update RSS options for a web log
    let updateRssOptions (webLog : WebLog) = backgroundTask {
        use! txn = conn.BeginTransactionAsync ()
        match! findById webLog.Id with
        | Some blog ->
            do! Document.update conn Table.WebLog webLogParams { blog with Rss = webLog.Rss }
            do! txn.CommitAsync ()
        | None -> ()
    }
    
    interface IWebLogData with
        member _.Add webLog = add webLog
        member _.All () = all ()
        member _.Delete webLogId = delete webLogId
        member _.FindByHost url = findByHost url
        member _.FindById webLogId = findById webLogId
        member _.UpdateSettings webLog = updateSettings webLog
        member _.UpdateRssOptions webLog = updateRssOptions webLog
