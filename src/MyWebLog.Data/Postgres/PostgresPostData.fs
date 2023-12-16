namespace MyWebLog.Data.Postgres

open BitBadger.Npgsql.FSharp.Documents
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open NodaTime.Text
open Npgsql.FSharp

/// PostgreSQL myWebLog post data implementation
type PostgresPostData(log: ILogger) =

    // SUPPORT FUNCTIONS
    
    /// Append revisions to a post
    let appendPostRevisions (post: Post) = backgroundTask {
        log.LogTrace "Post.appendPostRevisions"
        let! revisions = Revisions.findByEntityId Table.PostRevision Table.Post post.Id string
        return { post with Revisions = revisions }
    }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText row =
        { fromData<Post> row with Text = "" }
    
    /// Update a post's revisions
    let updatePostRevisions (postId: PostId) oldRevs newRevs =
        log.LogTrace "Post.updatePostRevisions"
        Revisions.update Table.PostRevision Table.Post postId string oldRevs newRevs
    
    /// Does the given post exist?
    let postExists (postId: PostId) webLogId =
        log.LogTrace "Post.postExists"
        Document.existsByWebLog Table.Post postId string webLogId
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Count posts in a status for the given web log
    let countByStatus (status: PostStatus) webLogId =
        log.LogTrace "Post.countByStatus"
        Count.byContains Table.Post {| webLogDoc webLogId with Status = status |}
    
    /// Find a post by its ID for the given web log (excluding revisions)
    let findById postId webLogId =
        log.LogTrace "Post.findById"
        Document.findByIdAndWebLog<PostId, Post> Table.Post postId string webLogId
    
    /// Find a post by its permalink for the given web log (excluding revisions and prior permalinks)
    let findByPermalink (permalink: Permalink) webLogId =
        log.LogTrace "Post.findByPermalink"
        Custom.single (selectWithCriteria Table.Post)
                      [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Permalink = string permalink |} ]
                      fromData<Post>
    
    /// Find a complete post by its ID for the given web log
    let findFullById postId webLogId = backgroundTask {
        log.LogTrace "Post.findFullById"
        match! findById postId webLogId with
        | Some post ->
            let! withRevisions = appendPostRevisions post
            return Some withRevisions
        | None -> return None
    }
    
    /// Delete a post by its ID for the given web log
    let delete postId webLogId = backgroundTask {
        log.LogTrace "Post.delete"
        match! postExists postId webLogId with
        | true ->
            do! Custom.nonQuery
                    $"""DELETE FROM {Table.PostComment} WHERE {Query.whereDataContains "@criteria"};
                        DELETE FROM {Table.Post}        WHERE id = @id"""
                    [ "@id", Sql.string (string postId); "@criteria", Query.jsonbDocParam {| PostId = postId |} ]
            return true
        | false -> return false
    }
    
    /// Find the current permalink from a list of potential prior permalinks for the given web log
    let findCurrentPermalink (permalinks: Permalink list) webLogId = backgroundTask {
        log.LogTrace "Post.findCurrentPermalink"
        if List.isEmpty permalinks then return None
        else
            let linkSql, linkParam = arrayContains (nameof Post.Empty.PriorPermalinks) string permalinks
            return!
                Custom.single
                    $"""SELECT data ->> '{nameof Post.Empty.Permalink}' AS permalink
                          FROM {Table.Post}
                         WHERE {Query.whereDataContains "@criteria"}
                           AND {linkSql}""" [ webLogContains webLogId; linkParam ] Map.toPermalink
    }
    
    /// Get all complete posts for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        log.LogTrace "Post.findFullByWebLog"
        let! posts     = Document.findByWebLog<Post> Table.Post webLogId
        let! revisions = Revisions.findByWebLog Table.PostRevision Table.Post PostId webLogId
        return
            posts
            |> List.map (fun it ->
                { it with Revisions = revisions |> List.filter (fun r -> fst r = it.Id) |> List.map snd })
    }
    
    /// Get a page of categorized posts for the given web log (excludes revisions)
    let findPageOfCategorizedPosts webLogId (categoryIds: CategoryId list) pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfCategorizedPosts"
        let catSql, catParam = arrayContains (nameof Post.Empty.CategoryIds) string categoryIds
        Custom.list
            $"{selectWithCriteria Table.Post}
                 AND {catSql}
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [   "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = Published |}
                catParam
            ] fromData<Post>
    
    /// Get a page of posts for the given web log (excludes text and revisions)
    let findPageOfPosts webLogId pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfPosts"
        Custom.list
            $"{selectWithCriteria Table.Post}
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' DESC NULLS FIRST,
                        data ->> '{nameof Post.Empty.UpdatedOn}'
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [ webLogContains webLogId ] postWithoutText
    
    /// Get a page of published posts for the given web log (excludes revisions)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfPublishedPosts"
        Custom.list
            $"{selectWithCriteria Table.Post}
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = Published |} ]
            fromData<Post>
    
    /// Get a page of tagged posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfTaggedPosts webLogId (tag : string) pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfTaggedPosts"
        Custom.list
            $"{selectWithCriteria Table.Post}
                 AND data['{nameof Post.Empty.Tags}'] @> @tag
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [   "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = Published |}
                "@tag",      Query.jsonbDocParam [| tag |]
            ] fromData<Post>
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId publishedOn = backgroundTask {
        log.LogTrace "Post.findSurroundingPosts"
        let queryParams () = [
            "@criteria",    Query.jsonbDocParam {| webLogDoc webLogId with Status = Published |}
            "@publishedOn", Sql.string ((InstantPattern.General.Format publishedOn)[..19])
        ]
        let pubField  = nameof Post.Empty.PublishedOn
        let! older =
            Custom.list
                $"{selectWithCriteria Table.Post}
                     AND SUBSTR(data ->> '{pubField}', 1, 19) < @publishedOn
                   ORDER BY data ->> '{pubField}' DESC
                   LIMIT 1" (queryParams ()) fromData<Post>
        let! newer =
            Custom.list
                $"{selectWithCriteria Table.Post}
                     AND SUBSTR(data ->> '{pubField}', 1, 19) > @publishedOn
                   ORDER BY data ->> '{pubField}'
                   LIMIT 1" (queryParams ()) fromData<Post>
        return List.tryHead older, List.tryHead newer
    }
    
    /// Save a post
    let save (post : Post) = backgroundTask {
        log.LogTrace "Post.save"
        let! oldPost = findFullById post.Id post.WebLogId
        do! save Table.Post { post with Revisions = [] }
        do! updatePostRevisions post.Id (match oldPost with Some p -> p.Revisions | None -> []) post.Revisions
    }
    
    /// Restore posts from a backup
    let restore posts = backgroundTask {
        log.LogTrace "Post.restore"
        let revisions = posts |> List.collect (fun p -> p.Revisions |> List.map (fun r -> p.Id, r))
        let! _ =
            Configuration.dataSource ()
            |> Sql.fromDataSource
            |> Sql.executeTransactionAsync [
                Query.insert Table.Post,
                    posts |> List.map (fun post -> Query.docParameters (string post.Id) { post with Revisions = [] })
                Revisions.insertSql Table.PostRevision,
                    revisions |> List.map (fun (postId, rev) -> Revisions.revParams postId string rev)
            ]
        ()
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId permalinks = backgroundTask {
        log.LogTrace "Post.updatePriorPermalinks"
        match! postExists postId webLogId with
        | true ->
            do! Update.partialById Table.Post (string postId) {| PriorPermalinks = permalinks |}
            return true
        | false -> return false
    }
    
    interface IPostData with
        member _.Add post = save post
        member _.CountByStatus status webLogId = countByStatus status webLogId
        member _.Delete postId webLogId = delete postId webLogId
        member _.FindById postId webLogId = findById postId webLogId
        member _.FindByPermalink permalink webLogId = findByPermalink permalink webLogId
        member _.FindCurrentPermalink permalinks webLogId = findCurrentPermalink permalinks webLogId
        member _.FindFullById postId webLogId = findFullById postId webLogId
        member _.FindFullByWebLog webLogId = findFullByWebLog webLogId
        member _.FindPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage =
            findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage
        member _.FindPageOfPosts webLogId pageNbr postsPerPage = findPageOfPosts webLogId pageNbr postsPerPage
        member _.FindPageOfPublishedPosts webLogId pageNbr postsPerPage =
            findPageOfPublishedPosts webLogId pageNbr postsPerPage
        member _.FindPageOfTaggedPosts webLogId tag pageNbr postsPerPage =
            findPageOfTaggedPosts webLogId tag pageNbr postsPerPage
        member _.FindSurroundingPosts webLogId publishedOn = findSurroundingPosts webLogId publishedOn
        member _.Restore posts = restore posts
        member _.Update post = save post
        member _.UpdatePriorPermalinks postId webLogId permalinks = updatePriorPermalinks postId webLogId permalinks
