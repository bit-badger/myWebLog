namespace MyWebLog.Data.Postgres

open BitBadger.Documents
open BitBadger.Documents.Postgres
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// PostgreSQL myWebLog web log data implementation
type PostgresWebLogData(log: ILogger) =
    
    /// Add a web log
    let add (webLog: WebLog) =
        log.LogTrace "WebLog.add"
        insert Table.WebLog webLog
    
    /// Retrieve all web logs
    let all () =
        log.LogTrace "WebLog.all"
        Find.all<WebLog> Table.WebLog
    
    /// Delete a web log by its ID
    let delete webLogId =
        log.LogTrace "WebLog.delete"
        Custom.nonQuery
            $"""DELETE FROM {Table.PostComment}
                 WHERE data ->> '{nameof Comment.Empty.PostId}'
                         IN (SELECT data ->> '{nameof Post.Empty.Id}'
                               FROM {Table.Post}
                              WHERE {Query.whereDataContains "@criteria"});
                DELETE FROM {Table.PostRevision}
                 WHERE post_id IN (SELECT data ->> 'Id' FROM {Table.Post} WHERE {Query.whereDataContains "@criteria"});
                DELETE FROM {Table.PageRevision}
                 WHERE page_id IN (SELECT data ->> 'Id' FROM {Table.Page} WHERE {Query.whereDataContains "@criteria"});
                {Query.Delete.byContains Table.Post};
                {Query.Delete.byContains Table.Page};
                {Query.Delete.byContains Table.Category};
                {Query.Delete.byContains Table.TagMap};
                {Query.Delete.byContains Table.WebLogUser};
                DELETE FROM {Table.Upload} WHERE web_log_id = @webLogId;
                DELETE FROM {Table.WebLog} WHERE {Query.whereById "@webLogId"}"""
            [ webLogIdParam webLogId; webLogContains webLogId ]
    
    /// Find a web log by its host (URL base)
    let findByHost (url: string) =
        log.LogTrace "WebLog.findByHost"
        Find.firstByContains<WebLog> Table.WebLog {| UrlBase = url |}
    
    /// Find a web log by its ID
    let findById (webLogId: WebLogId) = 
        log.LogTrace "WebLog.findById"
        Find.byId<WebLogId, WebLog> Table.WebLog webLogId
    
    /// Update redirect rules for a web log
    let updateRedirectRules (webLog: WebLog) = backgroundTask {
        log.LogTrace "WebLog.updateRedirectRules"
        match! findById webLog.Id with
        | Some _ -> do! Patch.byId Table.WebLog webLog.Id {| RedirectRules = webLog.RedirectRules |}
        | None -> ()
    }
    
    /// Update RSS options for a web log
    let updateRssOptions (webLog: WebLog) = backgroundTask {
        log.LogTrace "WebLog.updateRssOptions"
        match! findById webLog.Id with
        | Some _ -> do! Patch.byId Table.WebLog webLog.Id {| Rss = webLog.Rss |}
        | None -> ()
    }
    
    /// Update settings for a web log
    let updateSettings (webLog: WebLog) =
        log.LogTrace "WebLog.updateSettings"
        Update.byId Table.WebLog webLog.Id webLog
    
    interface IWebLogData with
        member _.Add webLog = add webLog
        member _.All() = all ()
        member _.Delete webLogId = delete webLogId
        member _.FindByHost url = findByHost url
        member _.FindById webLogId = findById webLogId
        member _.UpdateRedirectRules webLog = updateRedirectRules webLog
        member _.UpdateRssOptions webLog = updateRssOptions webLog
        member _.UpdateSettings webLog = updateSettings webLog
