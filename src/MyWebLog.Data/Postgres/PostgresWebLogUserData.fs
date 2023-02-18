namespace MyWebLog.Data.Postgres

open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog user data implementation        
type PostgresWebLogUserData (source : NpgsqlDataSource, log : ILogger) =
    
    /// Find a user by their ID for the given web log
    let findById userId webLogId =
        log.LogTrace "WebLogUser.findById"
        Document.findByIdAndWebLog<WebLogUserId, WebLogUser>
            source Table.WebLogUser userId WebLogUserId.toString webLogId
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
        log.LogTrace "WebLogUser.delete"
        match! findById userId webLogId with
        | Some _ ->
            let  criteria = Query.whereDataContains "@criteria"
            let  usrId    = WebLogUserId.toString userId
            let! isAuthor =
                Sql.fromDataSource source
                |> Sql.query $"
                    SELECT (   EXISTS (SELECT 1 FROM {Table.Page} WHERE {criteria}
                            OR EXISTS (SELECT 1 FROM {Table.Post} WHERE {criteria}))
                        AS {existsName}"
                |> Sql.parameters [ "@criteria", Query.jsonbDocParam {| AuthorId = usrId |} ]
                |> Sql.executeRowAsync Map.toExists
            if isAuthor then
                return Error "User has pages or posts; cannot delete"
            else
                do! Delete.byId Table.WebLogUser usrId
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail (email : string) webLogId =
        log.LogTrace "WebLogUser.findByEmail"
        Sql.fromDataSource source
        |> Sql.query (selectWithCriteria Table.WebLogUser)
        |> Sql.parameters [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Email = email |} ]
        |> Sql.executeAsync fromData<WebLogUser>
        |> tryHead
    
    /// Get all users for the given web log
    let findByWebLog webLogId =
        log.LogTrace "WebLogUser.findByWebLog"
        Sql.fromDataSource source
        |> Sql.query
            $"{selectWithCriteria Table.WebLogUser} ORDER BY LOWER(data->>'{nameof WebLogUser.empty.PreferredName}')"
        |> Sql.parameters [ webLogContains webLogId ]
        |> Sql.executeAsync fromData<WebLogUser>
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId userIds = backgroundTask {
        log.LogTrace "WebLogUser.findNames"
        let idSql, idParams = inClause "AND id" "id" WebLogUserId.toString userIds
        let! users =
            Sql.fromDataSource source
            |> Sql.query $"{selectWithCriteria Table.WebLogUser} {idSql}"
            |> Sql.parameters (webLogContains webLogId :: idParams)
            |> Sql.executeAsync fromData<WebLogUser>
        return
            users
            |> List.map (fun u -> { Name = WebLogUserId.toString u.Id; Value = WebLogUser.displayName u })
    }
    
    /// Restore users from a backup
    let restore (users : WebLogUser list) = backgroundTask {
        log.LogTrace "WebLogUser.restore"
        let! _ =
            Sql.fromDataSource source
            |> Sql.executeTransactionAsync [
                Query.insert Table.WebLogUser,
                users |> List.map (fun user -> Query.docParameters (WebLogUserId.toString user.Id) user)
            ]
        ()
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen userId webLogId = backgroundTask {
        log.LogTrace "WebLogUser.setLastSeen"
        match! findById userId webLogId with
        | Some user ->
            do! update Table.WebLogUser (WebLogUserId.toString userId) { user with LastSeenOn = Some (Noda.now ()) } 
        | None -> ()
    }
    
    /// Save a user
    let save (user : WebLogUser) =
        log.LogTrace "WebLogUser.save"
        save Table.WebLogUser (WebLogUserId.toString user.Id) user
    
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

