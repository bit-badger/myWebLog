namespace MyWebLog.Data.SQLite

open BitBadger.Documents
open BitBadger.Documents.Sqlite
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog user data implementation
type SQLiteWebLogUserData(conn: SqliteConnection, log: ILogger) =
    
    /// Find a user by their ID for the given web log
    let findById userId webLogId =
        log.LogTrace "WebLogUser.findById"
        Document.findByIdAndWebLog<WebLogUserId, WebLogUser> Table.WebLogUser userId webLogId conn
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
        log.LogTrace "WebLogUser.delete"
        match! findById userId webLogId with
        | Some _ ->
            let! pageCount = conn.countByField Table.Page (Field.EQ (nameof Page.Empty.AuthorId) (string userId))
            let! postCount = conn.countByField Table.Post (Field.EQ (nameof Post.Empty.AuthorId) (string userId))
            if pageCount + postCount > 0 then
                return Error "User has pages or posts; cannot delete"
            else
                do! conn.deleteById Table.WebLogUser userId
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail (email: string) webLogId =
        log.LogTrace "WebLogUser.findByEmail"
        let emailParam = Field.EQ (nameof WebLogUser.Empty.Email) email
        conn.customSingle
            $"""{Document.Query.selectByWebLog Table.WebLogUser}
                  AND {Query.whereByField emailParam "@email"}"""
            (addFieldParam "@email" emailParam [ webLogParam webLogId ])
            fromData<WebLogUser>
    
    /// Get all users for the given web log
    let findByWebLog webLogId = backgroundTask {
        log.LogTrace "WebLogUser.findByWebLog"
        let! users = Document.findByWebLog<WebLogUser> Table.WebLogUser webLogId conn
        return users |> List.sortBy _.PreferredName.ToLowerInvariant()
    }
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId (userIds: WebLogUserId list) =
        log.LogTrace "WebLogUser.findNames"
        let nameSql, nameParams = inClause $"AND data ->> '{nameof WebLogUser.Empty.Id}'" "id" string userIds 
        conn.customList
            $"{Document.Query.selectByWebLog Table.WebLogUser} {nameSql}"
            (webLogParam webLogId :: nameParams)
            (fun rdr ->
                let user = fromData<WebLogUser> rdr
                { Name = string user.Id; Value = user.DisplayName })
    
    /// Save a user
    let save user =
        log.LogTrace "WebLogUser.update"
        conn.save<WebLogUser> Table.WebLogUser user
    
    /// Restore users from a backup
    let restore users = backgroundTask {
        log.LogTrace "WebLogUser.restore"
        for user in users do do! save user
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen userId webLogId = backgroundTask {
        log.LogTrace "WebLogUser.setLastSeen"
        match! findById userId webLogId with
        | Some _ -> do! conn.patchById Table.WebLogUser userId {| LastSeenOn = Noda.now () |}
        | None -> ()
    }
    
    interface IWebLogUserData with
        member _.Add user = save user
        member _.Delete userId webLogId = delete userId webLogId
        member _.FindByEmail email webLogId = findByEmail email webLogId
        member _.FindById userId webLogId = findById userId webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindNames webLogId userIds = findNames webLogId userIds
        member _.Restore users = restore users
        member _.SetLastSeen userId webLogId = setLastSeen userId webLogId
        member _.Update user = save user
