namespace MyWebLog.Data.SQLite

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json

/// SQLite myWebLog user data implementation
type SQLiteWebLogUserData(conn: SqliteConnection, ser: JsonSerializer, log: ILogger) =
    
    // SUPPORT FUNCTIONS

    /// Add parameters for web log user INSERT or UPDATE statements
    let addWebLogUserParameters (cmd: SqliteCommand) (user: WebLogUser) =
        [   cmd.Parameters.AddWithValue ("@id",            string user.Id)
            cmd.Parameters.AddWithValue ("@webLogId",      string user.WebLogId)
            cmd.Parameters.AddWithValue ("@email",         user.Email)
            cmd.Parameters.AddWithValue ("@firstName",     user.FirstName)
            cmd.Parameters.AddWithValue ("@lastName",      user.LastName)
            cmd.Parameters.AddWithValue ("@preferredName", user.PreferredName)
            cmd.Parameters.AddWithValue ("@passwordHash",  user.PasswordHash)
            cmd.Parameters.AddWithValue ("@url",           maybe user.Url)
            cmd.Parameters.AddWithValue ("@accessLevel",   string user.AccessLevel)
            cmd.Parameters.AddWithValue ("@createdOn",     instantParam user.CreatedOn)
            cmd.Parameters.AddWithValue ("@lastSeenOn",    maybeInstant user.LastSeenOn)
        ] |> ignore
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a user
    let add user =
        log.LogTrace "WebLogUser.add"
        Document.insert<WebLogUser> conn ser Table.WebLogUser user
    
    /// Find a user by their ID for the given web log
    let findById userId webLogId =
        log.LogTrace "WebLogUser.findById"
        Document.findByIdAndWebLog<WebLogUserId, WebLogUser> conn ser Table.WebLogUser userId webLogId
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
        log.LogTrace "WebLogUser.delete"
        match! findById userId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand()
            cmd.CommandText <- $"SELECT COUNT(*) FROM {Table.Page} WHERE data ->> 'AuthorId' = @id"
            addDocId cmd userId
            let! pageCount = count cmd
            cmd.CommandText <- cmd.CommandText.Replace($"FROM {Table.Page}", $"FROM {Table.Post}")
            let! postCount = count cmd
            if pageCount + postCount > 0 then
                return Error "User has pages or posts; cannot delete"
            else
                do! Document.delete conn Table.WebLogUser userId
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail (email: string) webLogId = backgroundTask {
        log.LogTrace "WebLogUser.findByEmail"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            {Query.selectFromTable Table.WebLogUser}
             WHERE {Query.whereByWebLog}
               AND data ->> '{nameof WebLogUser.Empty.Email}' = @email"
        addWebLogId cmd webLogId
        addParam cmd "@email" email
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.fromDoc<WebLogUser> ser rdr) else None
    }
    
    /// Get all users for the given web log
    let findByWebLog webLogId = backgroundTask {
        log.LogTrace "WebLogUser.findByWebLog"
        let! users = Document.findByWebLog<WebLogUser> conn ser Table.WebLogUser webLogId
        return users |> List.sortBy _.PreferredName.ToLowerInvariant()
    }
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId (userIds: WebLogUserId list) = backgroundTask {
        log.LogTrace "WebLogUser.findNames"
        use cmd = conn.CreateCommand()
        let nameSql, nameParams = inClause "AND data ->> 'Id'" "id" string userIds 
        cmd.CommandText <- $"{Query.selectFromTable Table.WebLogUser} WHERE {Query.whereByWebLog} {nameSql}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange nameParams
        let! users = cmdToList<WebLogUser> cmd ser
        return users |> List.map (fun u -> { Name = string u.Id; Value = u.DisplayName })
    }
    
    /// Restore users from a backup
    let restore users = backgroundTask {
        log.LogTrace "WebLogUser.restore"
        for user in users do
            do! add user
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen (userId: WebLogUserId) webLogId = backgroundTask {
        log.LogTrace "WebLogUser.setLastSeen"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            UPDATE {Table.WebLogUser}
               SET data = json_set(data, '$.{nameof WebLogUser.Empty.LastSeenOn}', @lastSeenOn)
             WHERE {Query.whereById}
               AND {Query.whereByWebLog}"
        addDocId cmd userId
        addWebLogId cmd webLogId
        addParam cmd "@lastSeenOn" (instantParam (Noda.now ()))
        do! write cmd
    }
    
    /// Update a user
    let update (user: WebLogUser) =
        log.LogTrace "WebLogUser.update"
        Document.update conn ser Table.WebLogUser user.Id user
    
    interface IWebLogUserData with
        member _.Add user = add user
        member _.Delete userId webLogId = delete userId webLogId
        member _.FindByEmail email webLogId = findByEmail email webLogId
        member _.FindById userId webLogId = findById userId webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.FindNames webLogId userIds = findNames webLogId userIds
        member _.Restore users = restore users
        member _.SetLastSeen userId webLogId = setLastSeen userId webLogId
        member _.Update user = update user
