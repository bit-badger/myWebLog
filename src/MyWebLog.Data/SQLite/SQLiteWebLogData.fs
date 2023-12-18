namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json

/// SQLite myWebLog web log data implementation
type SQLiteWebLogData(conn: SqliteConnection, ser: JsonSerializer, log: ILogger) =
    
    /// Add a web log
    let add webLog =
        log.LogTrace "WebLog.add"
        Document.insert<WebLog> conn ser Table.WebLog webLog
    
    /// Retrieve all web logs
    let all () =
        log.LogTrace "WebLog.all"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- Query.selectFromTable Table.WebLog
        cmdToList<WebLog> cmd ser
    
    /// Delete a web log by its ID
    let delete webLogId = backgroundTask {
        log.LogTrace "WebLog.delete"
        let idField = "data ->> 'WebLogId'"
        let subQuery table = $"(SELECT data ->> 'Id' FROM {table} WHERE {idField} = @webLogId)"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            DELETE FROM {Table.PostComment}  WHERE data ->> 'PostId' IN {subQuery Table.Post};
            DELETE FROM {Table.PostRevision} WHERE post_id           IN {subQuery Table.Post};
            DELETE FROM {Table.PageRevision} WHERE page_id           IN {subQuery Table.Page};
            DELETE FROM {Table.Post}         WHERE {idField}  = @webLogId;
            DELETE FROM {Table.Page}         WHERE {idField}  = @webLogId;
            DELETE FROM {Table.Category}     WHERE {idField}  = @webLogId;
            DELETE FROM {Table.TagMap}       WHERE {idField}  = @webLogId;
            DELETE FROM {Table.Upload}       WHERE web_log_id = @webLogId;
            DELETE FROM {Table.WebLogUser}   WHERE {idField}  = @webLogId;
            DELETE FROM {Table.WebLog}       WHERE id         = @webLogId"
        addWebLogId cmd webLogId
        do! write cmd
    }
    
    /// Find a web log by its host (URL base)
    let findByHost (url: string) = backgroundTask {
        log.LogTrace "WebLog.findByHost"
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
            $"{Query.selectFromTable Table.WebLog} WHERE data ->> '{nameof WebLog.Empty.UrlBase}' = @urlBase"
        addParam cmd "@urlBase" url
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.fromDoc<WebLog> ser rdr) else None
    }
    
    /// Find a web log by its ID
    let findById webLogId =
        log.LogTrace "WebLog.findById"
        Document.findById<WebLogId, WebLog> conn ser Table.WebLog webLogId
    
    /// Update redirect rules for a web log
    let updateRedirectRules (webLog: WebLog) =
        log.LogTrace "WebLog.updateRedirectRules"
        Document.updateField conn ser Table.WebLog webLog.Id (nameof WebLog.Empty.RedirectRules) webLog.RedirectRules

    /// Update RSS options for a web log
    let updateRssOptions (webLog: WebLog) =
        log.LogTrace "WebLog.updateRssOptions"
        Document.updateField conn ser Table.WebLog webLog.Id (nameof WebLog.Empty.Rss) webLog.Rss
    
    /// Update settings for a web log
    let updateSettings (webLog: WebLog) =
        log.LogTrace "WebLog.updateSettings"
        Document.update conn ser Table.WebLog webLog.Id webLog
    
    interface IWebLogData with
        member _.Add webLog = add webLog
        member _.All () = all ()
        member _.Delete webLogId = delete webLogId
        member _.FindByHost url = findByHost url
        member _.FindById webLogId = findById webLogId
        member _.UpdateRedirectRules webLog = updateRedirectRules webLog
        member _.UpdateRssOptions webLog = updateRssOptions webLog
        member _.UpdateSettings webLog = updateSettings webLog
