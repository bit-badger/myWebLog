﻿namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog category data implementation
type PostgresCategoryData (source : NpgsqlDataSource) =
    
    /// Shorthand for turning a web log ID into a string
    let wls = WebLogId.toString

    /// Count all categories for the given web log
    let countAll webLogId =
        Sql.fromDataSource source
        |> Query.countByContains Table.Category {| WebLogId = wls webLogId |}
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId =
        Sql.fromDataSource source
        |> Query.countByContains Table.Category {| WebLogId = wls webLogId; ParentId = None |}
    
    /// Retrieve all categories for the given web log in a DotLiquid-friendly format
    let findAllForView webLogId = backgroundTask {
        let! cats =
            Sql.fromDataSource source
            |> Sql.query $"""
                {Query.selectFromTable Table.Category}
                 WHERE {Query.whereDataContains "@criteria"}
                 ORDER BY LOWER(data->>'{nameof Category.empty.Name}')"""
            |> Sql.parameters [ "@criteria", webLogContains webLogId ]
            |> Sql.executeAsync fromData<Category>
        let ordered = Utils.orderByHierarchy cats None None []
        let counts  =
            ordered
            |> Seq.map (fun it ->
                // Parent category post counts include posts in subcategories
                let catIdSql, catIdParams =
                    ordered
                    |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
                    |> Seq.map (fun cat -> cat.Id)
                    |> Seq.append (Seq.singleton it.Id)
                    |> List.ofSeq
                    |> jsonArrayInClause (nameof Post.empty.CategoryIds) id
                let postCount =
                    Sql.fromDataSource source
                    |> Sql.query $"""
                        SELECT COUNT(DISTINCT id) AS {countName}
                          FROM {Table.Post}
                         WHERE {Query.whereDataContains "@criteria"}
                           AND ({catIdSql})"""
                    |> Sql.parameters (
                        ("@criteria",
                            Query.jsonbDocParam {| WebLogId = wls webLogId; Status = PostStatus.toString Published |})
                        :: catIdParams)
                    |> Sql.executeRowAsync Map.toCount
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                it.Id, postCount)
            |> List.ofSeq
        return
            ordered
            |> Seq.map (fun cat ->
                { cat with
                    PostCount = counts
                                |> List.tryFind (fun c -> fst c = cat.Id)
                                |> Option.map snd
                                |> Option.defaultValue 0
                })
            |> Array.ofSeq
    }
    /// Find a category by its ID for the given web log
    let findById catId webLogId =
        Document.findByIdAndWebLog<CategoryId, Category> source Table.Category catId CategoryId.toString webLogId
    
    /// Find all categories for the given web log
    let findByWebLog webLogId =
        Document.findByWebLog<Category> source Table.Category webLogId
    
    /// Create parameters for a category insert / update
    let catParameters (cat : Category) =
        Query.docParameters (CategoryId.toString cat.Id) cat
    
    /// Delete a category
    let delete catId webLogId = backgroundTask {
        match! findById catId webLogId with
        | Some cat ->
            // Reassign any children to the category's parent category
            let! children =
                Sql.fromDataSource source
                |> Query.findByContains Table.Category {| ParentId = CategoryId.toString catId |}
            let hasChildren = not (List.isEmpty children)
            if hasChildren then
                let! _ =
                    Sql.fromDataSource source
                    |> Sql.executeTransactionAsync [
                        Query.updateQuery Table.Category,
                        children |> List.map (fun child -> catParameters { child with ParentId = cat.ParentId })
                    ]
                ()
            // Delete the category off all posts where it is assigned
            let! posts =
                Sql.fromDataSource source
                |> Sql.query $"SELECT data FROM {Table.Post} WHERE data->'{nameof Post.empty.CategoryIds}' ? @id"
                |> Sql.parameters [ "@id", Sql.jsonb (CategoryId.toString catId) ]
                |> Sql.executeAsync fromData<Post>
            if not (List.isEmpty posts) then
                let! _ =
                    Sql.fromDataSource source
                    |> Sql.executeTransactionAsync [
                        Query.updateQuery Table.Post,
                        posts |> List.map (fun post -> [
                            "@id",   Sql.string (PostId.toString post.Id)
                            "@data", Query.jsonbDocParam
                                        { post with
                                            CategoryIds = post.CategoryIds |> List.filter (fun cat -> cat <> catId)
                                        }
                        ])
                    ]
                ()
            // Delete the category itself
            do! Sql.fromDataSource source |> Query.deleteById Table.Category (CategoryId.toString catId)
            return if hasChildren then ReassignedChildCategories else CategoryDeleted
        | None -> return CategoryNotFound
    }
    
    /// Save a category
    let save (cat : Category) = backgroundTask {
        do! Sql.fromDataSource source |> Query.save Table.Category (CategoryId.toString cat.Id) cat
    }
    
    /// Restore categories from a backup
    let restore cats = backgroundTask {
        let! _ =
            Sql.fromDataSource source
            |> Sql.executeTransactionAsync [
                Query.insertQuery Table.Category, cats |> List.map catParameters
            ]
        ()
    }
    
    interface ICategoryData with
        member _.Add cat = save cat
        member _.CountAll webLogId = countAll webLogId
        member _.CountTopLevel webLogId = countTopLevel webLogId
        member _.FindAllForView webLogId = findAllForView webLogId
        member _.FindById catId webLogId = findById catId webLogId
        member _.FindByWebLog webLogId = findByWebLog webLogId
        member _.Delete catId webLogId = delete catId webLogId
        member _.Restore cats = restore cats
        member _.Update cat = save cat
