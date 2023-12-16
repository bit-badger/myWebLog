namespace MyWebLog.Data.Postgres

open BitBadger.Npgsql.FSharp.Documents
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql.FSharp

/// PostgreSQL myWebLog category data implementation
type PostgresCategoryData(log: ILogger) =
    
    /// Count all categories for the given web log
    let countAll webLogId =
        log.LogTrace "Category.countAll"
        Count.byContains Table.Category (webLogDoc webLogId)
    
    /// Count all top-level categories for the given web log
    let countTopLevel webLogId =
        log.LogTrace "Category.countTopLevel"
        Count.byContains Table.Category {| webLogDoc webLogId with ParentId = None |}
    
    /// Retrieve all categories for the given web log in a DotLiquid-friendly format
    let findAllForView webLogId = backgroundTask {
        log.LogTrace "Category.findAllForView"
        let! cats =
            Custom.list $"{selectWithCriteria Table.Category} ORDER BY LOWER(data ->> '{nameof Category.Empty.Name}')"
                        [ webLogContains webLogId ] fromData<Category>
        let ordered = Utils.orderByHierarchy cats None None []
        let counts  =
            ordered
            |> Seq.map (fun it ->
                // Parent category post counts include posts in subcategories
                let catIdSql, catIdParams =
                    ordered
                    |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
                    |> Seq.map _.Id
                    |> Seq.append (Seq.singleton it.Id)
                    |> List.ofSeq
                    |> arrayContains (nameof Post.Empty.CategoryIds) id
                let postCount =
                    Custom.scalar
                        $"""SELECT COUNT(DISTINCT id) AS {countName}
                              FROM {Table.Post}
                             WHERE {Query.whereDataContains "@criteria"}
                               AND {catIdSql}"""
                        [   "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = Published |}
                            catIdParams ]
                        Map.toCount
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
        log.LogTrace "Category.findById"
        Document.findByIdAndWebLog<CategoryId, Category> Table.Category catId string webLogId
    
    /// Find all categories for the given web log
    let findByWebLog webLogId =
        log.LogTrace "Category.findByWebLog"
        Document.findByWebLog<Category> Table.Category webLogId
    
    /// Create parameters for a category insert / update
    let catParameters (cat : Category) =
        Query.docParameters (string cat.Id) cat
    
    /// Delete a category
    let delete catId webLogId = backgroundTask {
        log.LogTrace "Category.delete"
        match! findById catId webLogId with
        | Some cat ->
            // Reassign any children to the category's parent category
            let! children = Find.byContains<Category> Table.Category {| ParentId = string catId |}
            let hasChildren = not (List.isEmpty children)
            if hasChildren then
                let! _ =
                    Configuration.dataSource ()
                    |> Sql.fromDataSource
                    |> Sql.executeTransactionAsync [
                        Query.Update.partialById Table.Category,
                        children |> List.map (fun child -> [
                            "@id",   Sql.string (string child.Id)
                            "@data", Query.jsonbDocParam {| ParentId = cat.ParentId |}
                        ])
                    ]
                ()
            // Delete the category off all posts where it is assigned
            let! posts =
                Custom.list $"SELECT data FROM {Table.Post} WHERE data -> '{nameof Post.Empty.CategoryIds}' @> @id"
                            [ "@id", Query.jsonbDocParam [| string catId |] ] fromData<Post>
            if not (List.isEmpty posts) then
                let! _ =
                    Configuration.dataSource ()
                    |> Sql.fromDataSource
                    |> Sql.executeTransactionAsync [
                        Query.Update.partialById Table.Post,
                        posts |> List.map (fun post -> [
                            "@id",   Sql.string (string post.Id)
                            "@data", Query.jsonbDocParam
                                        {| CategoryIds = post.CategoryIds |> List.filter (fun cat -> cat <> catId) |}
                        ])
                    ]
                ()
            // Delete the category itself
            do! Delete.byId Table.Category (string catId)
            return if hasChildren then ReassignedChildCategories else CategoryDeleted
        | None -> return CategoryNotFound
    }
    
    /// Save a category
    let save (cat : Category) = backgroundTask {
        log.LogTrace "Category.save"
        do! save Table.Category cat
    }
    
    /// Restore categories from a backup
    let restore cats = backgroundTask {
        log.LogTrace "Category.restore"
        let! _ =
            Configuration.dataSource ()
            |> Sql.fromDataSource
            |> Sql.executeTransactionAsync [
                Query.insert Table.Category, cats |> List.map catParameters
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
