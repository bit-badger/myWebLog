namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog web log data implementation        
type PostgresWebLogData (source : NpgsqlDataSource) =
    
    /// Add a web log
    let add (webLog : WebLog) =
        Sql.fromDataSource source |> Query.insert Table.WebLog (WebLogId.toString webLog.Id) webLog
    
    /// Retrieve all web logs
    let all () =
        Sql.fromDataSource source
        |> Query.all<WebLog> Table.WebLog
    
    /// Delete a web log by its ID
    let delete webLogId = backgroundTask {
        let criteria = Query.whereDataContains "@criteria"
        let! _ =
            Sql.fromDataSource source
            |> Sql.query $"
                DELETE FROM {Table.PostComment}
                 WHERE data->>'{nameof Comment.empty.PostId}' IN (SELECT id FROM {Table.Post} WHERE {criteria});
                DELETE FROM {Table.Post}        WHERE {criteria};
                DELETE FROM {Table.Page}        WHERE {criteria};
                DELETE FROM {Table.Category}    WHERE {criteria};
                DELETE FROM {Table.TagMap}      WHERE {criteria};
                DELETE FROM {Table.Upload}      WHERE web_log_id = @webLogId;
                DELETE FROM {Table.WebLogUser}  WHERE {criteria};
                DELETE FROM {Table.WebLog}      WHERE id = @webLogId"
            |> Sql.parameters [ webLogIdParam webLogId; "@criteria", webLogContains webLogId ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Find a web log by its host (URL base)
    let findByHost (url : string) =
        Sql.fromDataSource source
        |> Sql.query $"""{Query.selectFromTable Table.WebLog} WHERE {Query.whereDataContains "@criteria"}"""
        |> Sql.parameters [ "@criteria", Query.jsonbDocParam {| UrlBase = url |} ]
        |> Sql.executeAsync fromData<WebLog>
        |> tryHead
    
    /// Find a web log by its ID
    let findById webLogId = 
        Sql.fromDataSource source
        |> Query.tryById<WebLog> Table.WebLog (WebLogId.toString webLogId)
    
    /// Update settings for a web log
    let updateSettings (webLog : WebLog) =
        Sql.fromDataSource source |> Query.update Table.WebLog (WebLogId.toString webLog.Id) webLog
    
    /// Update RSS options for a web log
    let updateRssOptions (webLog : WebLog) = backgroundTask {
        match! findById webLog.Id with
        | Some blog ->
            do! Sql.fromDataSource source
                |> Query.update Table.WebLog (WebLogId.toString webLog.Id) { blog with Rss = webLog.Rss }
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
