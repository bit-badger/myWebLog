namespace MyWebLog.Data.SQLite

open BitBadger.Sqlite.FSharp.Documents
open BitBadger.Sqlite.FSharp.Documents.WithConn
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog web log data implementation
type SQLiteWebLogData(conn: SqliteConnection, log: ILogger) =
    
    /// Add a web log
    let add webLog =
        log.LogTrace "WebLog.add"
        insert<WebLog> Table.WebLog webLog conn
    
    /// Retrieve all web logs
    let all () =
        log.LogTrace "WebLog.all"
        Find.all<WebLog> Table.WebLog conn
    
    /// Delete a web log by its ID
    let delete webLogId =
        log.LogTrace "WebLog.delete"
        let subQuery table =
            $"""(SELECT data ->> 'Id' FROM {table} WHERE {Query.whereFieldEquals "WebLogId" "@webLogId"}"""
        Custom.nonQuery
            $"""DELETE FROM {Table.PostComment}  WHERE data ->> 'PostId' IN {subQuery Table.Post};
                DELETE FROM {Table.PostRevision} WHERE post_id           IN {subQuery Table.Post};
                DELETE FROM {Table.PageRevision} WHERE page_id           IN {subQuery Table.Page};
                DELETE FROM {Table.Post}         WHERE {Query.whereFieldEquals "WebLogId" "@webLogId"};
                DELETE FROM {Table.Page}         WHERE {Query.whereFieldEquals "WebLogId" "@webLogId"};
                DELETE FROM {Table.Category}     WHERE {Query.whereFieldEquals "WebLogId" "@webLogId"};
                DELETE FROM {Table.TagMap}       WHERE {Query.whereFieldEquals "WebLogId" "@webLogId"};
                DELETE FROM {Table.Upload}       WHERE web_log_id = @id;
                DELETE FROM {Table.WebLogUser}   WHERE {Query.whereFieldEquals "WebLogId" "@webLogId"};
                DELETE FROM {Table.WebLog}       WHERE {Query.whereById "@webLogId"}"""
            [ webLogParam webLogId ]
            conn
    
    /// Find a web log by its host (URL base)
    let findByHost (url: string) =
        log.LogTrace "WebLog.findByHost"
        Find.firstByFieldEquals<WebLog> Table.WebLog (nameof WebLog.Empty.UrlBase) url conn
    
    /// Find a web log by its ID
    let findById webLogId =
        log.LogTrace "WebLog.findById"
        Find.byId<WebLogId, WebLog> Table.WebLog webLogId conn
    
    /// Update redirect rules for a web log
    let updateRedirectRules (webLog: WebLog) =
        log.LogTrace "WebLog.updateRedirectRules"
        Update.partialById Table.WebLog webLog.Id {| RedirectRules = webLog.RedirectRules |} conn

    /// Update RSS options for a web log
    let updateRssOptions (webLog: WebLog) =
        log.LogTrace "WebLog.updateRssOptions"
        Update.partialById Table.WebLog webLog.Id {| Rss = webLog.Rss |} conn
    
    /// Update settings for a web log
    let updateSettings (webLog: WebLog) =
        log.LogTrace "WebLog.updateSettings"
        Update.full Table.WebLog webLog.Id webLog conn
    
    interface IWebLogData with
        member _.Add webLog = add webLog
        member _.All () = all ()
        member _.Delete webLogId = delete webLogId
        member _.FindByHost url = findByHost url
        member _.FindById webLogId = findById webLogId
        member _.UpdateRedirectRules webLog = updateRedirectRules webLog
        member _.UpdateRssOptions webLog = updateRssOptions webLog
        member _.UpdateSettings webLog = updateSettings webLog
