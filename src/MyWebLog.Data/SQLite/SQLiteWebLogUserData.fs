namespace MyWebLog.Data.SQLite

open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog user data implementation        
type SQLiteWebLogUserData (conn : SqliteConnection) =
    
    // SUPPORT FUNCTIONS

    /// Add parameters for web log user INSERT or UPDATE statements
    let addWebLogUserParameters (cmd : SqliteCommand) (user : WebLogUser) =
        [ cmd.Parameters.AddWithValue ("@id", WebLogUserId.toString user.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString user.webLogId)
          cmd.Parameters.AddWithValue ("@userName", user.userName)
          cmd.Parameters.AddWithValue ("@firstName", user.firstName)
          cmd.Parameters.AddWithValue ("@lastName", user.lastName)
          cmd.Parameters.AddWithValue ("@preferredName", user.preferredName)
          cmd.Parameters.AddWithValue ("@passwordHash", user.passwordHash)
          cmd.Parameters.AddWithValue ("@salt", user.salt)
          cmd.Parameters.AddWithValue ("@url", maybe user.url)
          cmd.Parameters.AddWithValue ("@authorizationLevel", AuthorizationLevel.toString user.authorizationLevel)
        ] |> ignore
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a user
    let add user = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """INSERT INTO web_log_user (
                   id, web_log_id, user_name, first_name, last_name, preferred_name, password_hash, salt,
                   url, authorization_level
               ) VALUES (
                   @id, @webLogId, @userName, @firstName, @lastName, @preferredName, @passwordHash, @salt,
                   @url, @authorizationLevel
               )"""
        addWebLogUserParameters cmd user
        do! write cmd
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail (email : string) webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "SELECT * FROM web_log_user WHERE web_log_id = @webLogId AND user_name = @userName"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@userName", email) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return if rdr.Read () then Some (Map.toWebLogUser rdr) else None
    }
    
    /// Find a user by their ID for the given web log
    let findById userId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log_user WHERE id = @id"
        cmd.Parameters.AddWithValue ("@id", WebLogUserId.toString userId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return Helpers.verifyWebLog<WebLogUser> webLogId (fun u -> u.webLogId) Map.toWebLogUser rdr 
    }
    
    /// Get all users for the given web log
    let findByWebLog webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log_user WHERE web_log_id = @webLogId"
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
            |> List.map (fun u -> { name = WebLogUserId.toString u.id; value = WebLogUser.displayName u })
    }
    
    /// Restore users from a backup
    let restore users = backgroundTask {
        for user in users do
            do! add user
    }
    
    /// Update a user
    let update user = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """UPDATE web_log_user
                  SET user_name           = @userName,
                      first_name          = @firstName,
                      last_name           = @lastName,
                      preferred_name      = @preferredName,
                      password_hash       = @passwordHash,
                      salt                = @salt,
                      url                 = @url,
                      authorization_level = @authorizationLevel
                WHERE id         = @id
                  AND web_log_id = @webLogId"""
        addWebLogUserParameters cmd user
        do! write cmd
    }
    
    interface IWebLogUserData with
        member _.add user = add user
        member _.findByEmail email webLogId = findByEmail email webLogId
        member _.findById userId webLogId = findById userId webLogId
        member _.findByWebLog webLogId = findByWebLog webLogId
        member _.findNames webLogId userIds = findNames webLogId userIds
        member this.restore users = restore users
        member _.update user = update user
