namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog user data implementation        
type PostgresWebLogUserData (conn : NpgsqlConnection, ser : JsonSerializer) =
    
    /// Map a data row to a user
    let toWebLogUser = Map.fromDoc<WebLogUser> ser
    
    /// Parameters for saving web log users
    let userParams (user : WebLogUser) = [
        "@id",   Sql.string (WebLogUserId.toString user.Id)
        "@data", Sql.jsonb  (Utils.serialize ser user)
    ]

    /// Find a user by their ID for the given web log
    let findById userId webLogId =
        Document.findByIdAndWebLog conn Table.WebLogUser userId WebLogUserId.toString webLogId toWebLogUser
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
        match! findById userId webLogId with
        | Some _ ->
            let! isAuthor =
                Sql.existingConnection conn
                |> Sql.query $"
                    SELECT (   EXISTS (SELECT 1 FROM {Table.Page} WHERE data ->> '{nameof Page.empty.AuthorId}' = @id
                            OR EXISTS (SELECT 1 FROM {Table.Post} WHERE data ->> '{nameof Post.empty.AuthorId}' = @id))
                        AS {existsName}"
                |> Sql.parameters [ "@id", Sql.string (WebLogUserId.toString userId) ]
                |> Sql.executeRowAsync Map.toExists
            if isAuthor then
                return Error "User has pages or posts; cannot delete"
            else
                do! Document.delete conn Table.WebLogUser (WebLogUserId.toString userId)
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail email webLogId =
        Sql.existingConnection conn
        |> Sql.query $"{docSelectForWebLogSql Table.WebLogUser} AND data ->> '{nameof WebLogUser.empty.Email}' = @email"
        |> Sql.parameters [ webLogIdParam webLogId; "@email", Sql.string email ]
        |> Sql.executeAsync toWebLogUser
        |> tryHead
    
    /// Get all users for the given web log
    let findByWebLog webLogId =
        Document.findByWebLog conn Table.WebLogUser webLogId toWebLogUser
            (Some $"ORDER BY LOWER(data ->> '{nameof WebLogUser.empty.PreferredName}')")
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId userIds = backgroundTask {
        let idSql, idParams = inClause "AND id" "id" WebLogUserId.toString userIds
        let! users =
            Sql.existingConnection conn
            |> Sql.query $"{docSelectForWebLogSql Table.WebLogUser} {idSql}"
            |> Sql.parameters (webLogIdParam webLogId :: idParams)
            |> Sql.executeAsync toWebLogUser
        return
            users
            |> List.map (fun u -> { Name = WebLogUserId.toString u.Id; Value = WebLogUser.displayName u })
    }
    
    /// Restore users from a backup
    let restore users = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                docInsertSql Table.WebLogUser, users |> List.map userParams
            ]
        ()
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen userId webLogId = backgroundTask {
        use! txn = conn.BeginTransactionAsync ()
        match! findById userId webLogId with
        | Some user ->
            do! Document.update conn Table.WebLogUser userParams { user with LastSeenOn = Some (Noda.now ()) } 
            do! txn.CommitAsync ()
        | None -> ()
    }
    
    /// Save a user
    let save user = backgroundTask {
        do! Document.upsert conn Table.WebLogUser userParams user
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

