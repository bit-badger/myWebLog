namespace MyWebLog.Data.Postgres

open BitBadger.Npgsql.FSharp.Documents
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql.FSharp

/// PostgreSQL myWebLog user data implementation
type PostgresWebLogUserData(log: ILogger) =
    
    /// Find a user by their ID for the given web log
    let findById userId webLogId =
        log.LogTrace "WebLogUser.findById"
        Document.findByIdAndWebLog<WebLogUserId, WebLogUser> Table.WebLogUser userId webLogId
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
        log.LogTrace "WebLogUser.delete"
        match! findById userId webLogId with
        | Some _ ->
            let  criteria = Query.whereDataContains "@criteria"
            let! isAuthor =
                Custom.scalar
                    $" SELECT (   EXISTS (SELECT 1 FROM {Table.Page} WHERE {criteria})
                               OR EXISTS (SELECT 1 FROM {Table.Post} WHERE {criteria})
                              ) AS {existsName}"
                    [ "@criteria", Query.jsonbDocParam {| AuthorId = userId |} ]
                    Map.toExists
            if isAuthor then
                return Error "User has pages or posts; cannot delete"
            else
                do! Delete.byId Table.WebLogUser (string userId)
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail (email: string) webLogId =
        log.LogTrace "WebLogUser.findByEmail"
        Find.firstByContains<WebLogUser> Table.WebLogUser {| webLogDoc webLogId with Email = email |}
    
    /// Get all users for the given web log
    let findByWebLog webLogId =
        log.LogTrace "WebLogUser.findByWebLog"
        Custom.list
            $"{selectWithCriteria Table.WebLogUser} ORDER BY LOWER(data ->> '{nameof WebLogUser.Empty.PreferredName}')"
            [ webLogContains webLogId ]
            fromData<WebLogUser>
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId (userIds: WebLogUserId list) = backgroundTask {
        log.LogTrace "WebLogUser.findNames"
        let idSql, idParams = inClause "AND id" "id" userIds
        let! users =
            Custom.list
                $"{selectWithCriteria Table.WebLogUser} {idSql}"
                (webLogContains webLogId :: idParams)
                fromData<WebLogUser>
        return users |> List.map (fun u -> { Name = string u.Id; Value = u.DisplayName })
    }
    
    /// Restore users from a backup
    let restore (users: WebLogUser list) = backgroundTask {
        log.LogTrace "WebLogUser.restore"
        let! _ =
            Configuration.dataSource ()
            |> Sql.fromDataSource
            |> Sql.executeTransactionAsync
                [ Query.insert Table.WebLogUser,
                    users |> List.map (fun user -> Query.docParameters (string user.Id) user) ]
        ()
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen (userId: WebLogUserId) webLogId = backgroundTask {
        log.LogTrace "WebLogUser.setLastSeen"
        match! Document.existsByWebLog Table.WebLogUser userId webLogId with
        | true -> do! Update.partialById Table.WebLogUser (string userId) {| LastSeenOn = Some (Noda.now ()) |}
        | false -> ()
    }
    
    /// Save a user
    let save (user: WebLogUser) =
        log.LogTrace "WebLogUser.save"
        save Table.WebLogUser user
    
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
