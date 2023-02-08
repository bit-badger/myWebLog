namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog user data implementation        
type PostgresWebLogUserData (source : NpgsqlDataSource) =
    
    /// Query to get users by JSON document containment criteria
    let userByCriteria =
        $"""{Query.selectFromTable Table.WebLogUser} WHERE {Query.whereDataContains "@criteria"}"""
    
    /// Parameters for saving web log users
    let userParams (user : WebLogUser) =
        Query.docParameters (WebLogUserId.toString user.Id) user

    /// Find a user by their ID for the given web log
    let findById userId webLogId =
        Document.findByIdAndWebLog<WebLogUserId, WebLogUser>
            source Table.WebLogUser userId WebLogUserId.toString webLogId
    
    /// Delete a user if they have no posts or pages
    let delete userId webLogId = backgroundTask {
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
                do! Sql.fromDataSource source |> Query.deleteById Table.WebLogUser usrId
                return Ok true
        | None -> return Error "User does not exist"
    }
    
    /// Find a user by their e-mail address for the given web log
    let findByEmail (email : string) webLogId =
        Sql.fromDataSource source
        |> Sql.query userByCriteria
        |> Sql.parameters [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Email = email |} ]
        |> Sql.executeAsync fromData<WebLogUser>
        |> tryHead
    
    /// Get all users for the given web log
    let findByWebLog webLogId =
        Sql.fromDataSource source
        |> Sql.query $"{userByCriteria} ORDER BY LOWER(data->>'{nameof WebLogUser.empty.PreferredName}')"
        |> Sql.parameters [ webLogContains webLogId ]
        |> Sql.executeAsync fromData<WebLogUser>
    
    /// Find the names of users by their IDs for the given web log
    let findNames webLogId userIds = backgroundTask {
        let idSql, idParams = inClause "AND id" "id" WebLogUserId.toString userIds
        let! users =
            Sql.fromDataSource source
            |> Sql.query $"{userByCriteria} {idSql}"
            |> Sql.parameters (webLogContains webLogId :: idParams)
            |> Sql.executeAsync fromData<WebLogUser>
        return
            users
            |> List.map (fun u -> { Name = WebLogUserId.toString u.Id; Value = WebLogUser.displayName u })
    }
    
    /// Restore users from a backup
    let restore users = backgroundTask {
        let! _ =
            Sql.fromDataSource source
            |> Sql.executeTransactionAsync [
                Query.insertQuery Table.WebLogUser, users |> List.map userParams
            ]
        ()
    }
    
    /// Set a user's last seen date/time to now
    let setLastSeen userId webLogId = backgroundTask {
        match! findById userId webLogId with
        | Some user ->
            do! Sql.fromDataSource source
                |> Query.update Table.WebLogUser (WebLogUserId.toString userId)
                       { user with LastSeenOn = Some (Noda.now ()) } 
        | None -> ()
    }
    
    /// Save a user
    let save (user : WebLogUser) =
        Sql.fromDataSource source |> Query.save Table.WebLogUser (WebLogUserId.toString user.Id) user
    
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

