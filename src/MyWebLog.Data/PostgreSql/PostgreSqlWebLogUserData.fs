namespace MyWebLog.Data.PostgreSql

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog user data implementation        
type PostgreSqlWebLogUserData (conn : NpgsqlConnection) =
    
    /// The INSERT statement for a user
    let userInsert =
        "INSERT INTO web_log_user (
            id, web_log_id, email, first_name, last_name, preferred_name, password_hash, salt, url, access_level,
            created_on, last_seen_on
        ) VALUES (
            @id, @webLogId, @email, @firstName, @lastName, @preferredName, @passwordHash, @salt, @url, @accessLevel,
            @createdOn, @lastSeenOn
        )"
    
    /// Parameters for saving web log users
    let userParams (user : WebLogUser) = [
        "@id",            Sql.string            (WebLogUserId.toString user.Id)
        "@webLogId",      Sql.string            (WebLogId.toString user.WebLogId)
        "@email",         Sql.string            user.Email
        "@firstName",     Sql.string            user.FirstName
        "@lastName",      Sql.string            user.LastName
        "@preferredName", Sql.string            user.PreferredName
        "@passwordHash",  Sql.string            user.PasswordHash
        "@salt",          Sql.uuid              user.Salt
        "@url",           Sql.stringOrNone      user.Url
        "@accessLevel",   Sql.string            (AccessLevel.toString user.AccessLevel)
        "@createdOn",     Sql.timestamptz       user.CreatedOn
        "@lastSeenOn",    Sql.timestamptzOrNone user.LastSeenOn
    ]

    /// Find a user by their ID for the given web log
    let findById userId webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM web_log_user WHERE id = @id AND web_log_id = @webLogId"
        |> Sql.parameters [ "@id", Sql.string (WebLogUserId.toString userId); webLogIdParam webLogId ]
        |> Sql.executeAsync Map.toWebLogUser
        |> tryHead
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
        match! findById userId webLogId with
        | Some _ ->
            let userParam = [ "@userId", Sql.string (WebLogUserId.toString userId) ]
            let! isAuthor =
                Sql.existingConnection conn
                |> Sql.query
                    "SELECT (   EXISTS (SELECT 1 FROM page WHERE author_id = @userId
                             OR EXISTS (SELECT 1 FROM post WHERE author_id = @userId)) AS does_exist"
                |> Sql.parameters userParam
                |> Sql.executeRowAsync Map.toExists
            if isAuthor then
                return Error "User has pages or posts; cannot delete"
            else
                let! _ =
                    Sql.existingConnection conn
                    |> Sql.query "DELETE FROM web_log_user WHERE id = @userId"
                    |> Sql.parameters userParam
                    |> Sql.executeNonQueryAsync
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail email webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM web_log_user WHERE web_log_id = @webLogId AND email = @email"
        |> Sql.parameters [ webLogIdParam webLogId; "@email", Sql.string email ]
        |> Sql.executeAsync Map.toWebLogUser
        |> tryHead
    
    /// Get all users for the given web log
    let findByWebLog webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM web_log_user WHERE web_log_id = @webLogId ORDER BY LOWER(preferred_name)"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync Map.toWebLogUser
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId userIds = backgroundTask {
        let idSql, idParams = inClause "id" WebLogUserId.toString userIds
        let! users =
            Sql.existingConnection conn
            |> Sql.query $"SELECT * FROM web_log_user WHERE web_log_id = @webLogId AND id IN ({idSql})"
            |> Sql.parameters (webLogIdParam webLogId :: idParams)
            |> Sql.executeAsync Map.toWebLogUser
        return
            users
            |> List.map (fun u -> { Name = WebLogUserId.toString u.Id; Value = WebLogUser.displayName u })
    }
    
    /// Restore users from a backup
    let restore users = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                userInsert, users |> List.map userParams
            ]
        ()
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen userId webLogId = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query "UPDATE web_log_user SET last_seen_on = @lastSeenOn WHERE id = @id AND web_log_id = @webLogId"
            |> Sql.parameters
                [   webLogIdParam webLogId
                    "@id",         Sql.string      (WebLogUserId.toString userId)
                    "@lastSeenOn", Sql.timestamptz System.DateTime.UtcNow ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Save a user
    let save user = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"
                {userInsert} ON CONFLICT (id) DO UPDATE
                SET email          = @email,
                    first_name     = @firstName,
                    last_name      = @lastName,
                    preferred_name = @preferredName,
                    password_hash  = @passwordHash,
                    salt           = @salt,
                    url            = @url,
                    access_level   = @accessLevel,
                    created_on     = @createdOn,
                    last_seen_on   = @lastSeenOn"
            |> Sql.parameters (userParams user)
            |> Sql.executeNonQueryAsync
        ()
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

