namespace MyWebLog.Data.SQLite

open BitBadger.Documents
open BitBadger.Documents.Sqlite
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog web log data implementation
type SQLiteWebLogData(conn: SqliteConnection, log: ILogger) =
    
    /// Add a web log
    let add webLog =
        log.LogTrace "WebLog.add"
        conn.insert<WebLog> Table.WebLog webLog
    
    /// Retrieve all web logs
    let all () =
        log.LogTrace "WebLog.all"
        conn.findAll<WebLog> Table.WebLog
    
    /// Delete a web log by its ID
    let delete webLogId =
        log.LogTrace "WebLog.delete"
        let webLogMatches = Query.whereByField (Field.EQ "WebLogId" "") "@webLogId"
        let subQuery table = $"(SELECT data ->> 'Id' FROM {table} WHERE {webLogMatches})"
        Custom.nonQuery
            $"""DELETE FROM {Table.PostComment}  WHERE data ->> 'PostId' IN {subQuery Table.Post};
                DELETE FROM {Table.PostRevision} WHERE post_id           IN {subQuery Table.Post};
                DELETE FROM {Table.PageRevision} WHERE page_id           IN {subQuery Table.Page};
                DELETE FROM {Table.Post}         WHERE {webLogMatches};
                DELETE FROM {Table.Page}         WHERE {webLogMatches};
                DELETE FROM {Table.Category}     WHERE {webLogMatches};
                DELETE FROM {Table.TagMap}       WHERE {webLogMatches};
                DELETE FROM {Table.Upload}       WHERE web_log_id = @webLogId;
                DELETE FROM {Table.WebLogUser}   WHERE {webLogMatches};
                DELETE FROM {Table.WebLog}       WHERE {Query.whereById "@webLogId"}"""
            [ webLogParam webLogId ]
    
    /// Find a web log by its host (URL base)
    let findByHost (url: string) =
        log.LogTrace "WebLog.findByHost"
        conn.findFirstByField<WebLog> Table.WebLog (Field.EQ (nameof WebLog.Empty.UrlBase) url)
    
    /// Find a web log by its ID
    let findById webLogId =
        log.LogTrace "WebLog.findById"
        conn.findById<WebLogId, WebLog> Table.WebLog webLogId
    
    /// Update redirect rules for a web log
    let updateRedirectRules (webLog: WebLog) =
        log.LogTrace "WebLog.updateRedirectRules"
        conn.patchById Table.WebLog webLog.Id {| RedirectRules = webLog.RedirectRules |}

    /// Update RSS options for a web log
    let updateRssOptions (webLog: WebLog) =
        log.LogTrace "WebLog.updateRssOptions"
        conn.patchById Table.WebLog webLog.Id {| Rss = webLog.Rss |}
    
    /// Update settings for a web log
    let updateSettings (webLog: WebLog) =
        log.LogTrace "WebLog.updateSettings"
        conn.updateById Table.WebLog webLog.Id webLog
    
    interface IWebLogData with
        member _.Add webLog = add webLog
        member _.All () = all ()
        member _.Delete webLogId = delete webLogId
        member _.FindByHost url = findByHost url
        member _.FindById webLogId = findById webLogId
        member _.UpdateRedirectRules webLog = updateRedirectRules webLog
        member _.UpdateRssOptions webLog = updateRssOptions webLog
        member _.UpdateSettings webLog = updateSettings webLog
