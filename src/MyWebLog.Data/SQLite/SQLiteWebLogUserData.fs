namespace MyWebLog.Data.SQLite

open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog user data implementation        
type SQLiteWebLogUserData (conn : SqliteConnection) =
    
    // SUPPORT FUNCTIONS

    /// Add parameters for web log user INSERT or UPDATE statements
    let addWebLogUserParameters (cmd : SqliteCommand) (user : WebLogUser) =
        [   cmd.Parameters.AddWithValue ("@id",            WebLogUserId.toString user.Id)
            cmd.Parameters.AddWithValue ("@webLogId",      WebLogId.toString user.WebLogId)
            cmd.Parameters.AddWithValue ("@email",         user.Email)
            cmd.Parameters.AddWithValue ("@firstName",     user.FirstName)
            cmd.Parameters.AddWithValue ("@lastName",      user.LastName)
            cmd.Parameters.AddWithValue ("@preferredName", user.PreferredName)
            cmd.Parameters.AddWithValue ("@passwordHash",  user.PasswordHash)
            cmd.Parameters.AddWithValue ("@salt",          user.Salt)
            cmd.Parameters.AddWithValue ("@url",           maybe user.Url)
            cmd.Parameters.AddWithValue ("@accessLevel",   AccessLevel.toString user.AccessLevel)
            cmd.Parameters.AddWithValue ("@createdOn",     instantParam user.CreatedOn)
            cmd.Parameters.AddWithValue ("@lastSeenOn",    maybeInstant user.LastSeenOn)
        ] |> ignore
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a user
    let add user = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "INSERT INTO web_log_user (
                id, web_log_id, email, first_name, last_name, preferred_name, password_hash, salt, url, access_level,
                created_on, last_seen_on
            ) VALUES (
                @id, @webLogId, @email, @firstName, @lastName, @preferredName, @passwordHash, @salt, @url, @accessLevel,
                @createdOn, @lastSeenOn
            )"
        addWebLogUserParameters cmd user
        do! write cmd
    }
    
    /// Find a user by their ID for the given web log
    let findById userId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log_user WHERE id = @id"
        cmd.Parameters.AddWithValue ("@id", WebLogUserId.toString userId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return Helpers.verifyWebLog<WebLogUser> webLogId (fun u -> u.WebLogId) Map.toWebLogUser rdr 
    }
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
        match! findById userId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand ()
            cmd.CommandText <- "SELECT COUNT(id) FROM page WHERE author_id = @userId"
            cmd.Parameters.AddWithValue ("@userId", WebLogUserId.toString userId) |> ignore
            let! pageCount = count cmd
            cmd.CommandText <- "SELECT COUNT(id) FROM post WHERE author_id = @userId"
            let! postCount = count cmd
            if pageCount + postCount > 0 then
                return Error "User has pages or posts; cannot delete"
            else
                cmd.CommandText <- "DELETE FROM web_log_user WHERE id = @userId"
                let! _ = cmd.ExecuteNonQueryAsync ()
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail (email : string) webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log_user WHERE web_log_id = @webLogId AND email = @email"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@email", email) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return if rdr.Read () then Some (Map.toWebLogUser rdr) else None
    }
    
    /// Get all users for the given web log
    let findByWebLog webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log_user WHERE web_log_id = @webLogId ORDER BY LOWER(preferred_name)"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toWebLogUser rdr
    }
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId userIds = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log_user WHERE web_log_id = @webLogId AND id IN ("
        userIds
        |> List.iteri (fun idx userId ->
            if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
            cmd.CommandText <- $"{cmd.CommandText}@id{idx}"
            cmd.Parameters.AddWithValue ($"@id{idx}", WebLogUserId.toString userId) |> ignore)
        cmd.CommandText <- $"{cmd.CommandText})"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        return
            toList Map.toWebLogUser rdr
            |> List.map (fun u -> { Name = WebLogUserId.toString u.Id; Value = WebLogUser.displayName u })
    }
    
    /// Restore users from a backup
    let restore users = backgroundTask {
        for user in users do
            do! add user
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen userId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "UPDATE web_log_user
                SET last_seen_on = @lastSeenOn
              WHERE id         = @id
                AND web_log_id = @webLogId"
        addWebLogId cmd webLogId
        [ cmd.Parameters.AddWithValue ("@id",         WebLogUserId.toString userId)
          cmd.Parameters.AddWithValue ("@lastSeenOn", instantParam (Noda.now ()))
        ] |> ignore
        let! _ = cmd.ExecuteNonQueryAsync ()
        ()
    }
    
    /// Update a user
    let update user = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "UPDATE web_log_user
                SET email          = @email,
                    first_name     = @firstName,
                    last_name      = @lastName,
                    preferred_name = @preferredName,
                    password_hash  = @passwordHash,
                    salt           = @salt,
                    url            = @url,
                    access_level   = @accessLevel,
                    created_on     = @createdOn,
                    last_seen_on   = @lastSeenOn
              WHERE id         = @id
                AND web_log_id = @webLogId"
        addWebLogUserParameters cmd user
        do! write cmd
    }
    
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
