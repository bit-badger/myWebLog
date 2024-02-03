namespace MyWebLog.Data.Postgres

open BitBadger.Documents
open BitBadger.Documents.Postgres
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open NodaTime
open Npgsql.FSharp

/// PostgreSQL myWebLog post data implementation
type PostgresPostData(log: ILogger) =

    // SUPPORT FUNCTIONS
    
    /// Append revisions to a post
    let appendPostRevisions (post: Post) = backgroundTask {
        log.LogTrace "Post.appendPostRevisions"
        let! revisions = Revisions.findByEntityId Table.PostRevision Table.Post post.Id
        return { post with Revisions = revisions }
    }
    
    /// Return a post with no revisions or prior permalinks
    let postWithoutLinks row =
        { fromData<Post> row with PriorPermalinks = [] }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText row =
        { postWithoutLinks row with Text = "" }
    
    /// Update a post's revisions
    let updatePostRevisions (postId: PostId) oldRevs newRevs =
        log.LogTrace "Post.updatePostRevisions"
        Revisions.update Table.PostRevision Table.Post postId oldRevs newRevs
    
    /// Does the given post exist?
    let postExists (postId: PostId) webLogId =
        log.LogTrace "Post.postExists"
        Document.existsByWebLog Table.Post postId webLogId
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a post
    let add (post : Post) = backgroundTask {
        log.LogTrace "Post.add"
        do! insert Table.Post { post with Revisions = [] }
        do! updatePostRevisions post.Id [] post.Revisions
    }
    
    /// Count posts in a status for the given web log
    let countByStatus (status: PostStatus) webLogId =
        log.LogTrace "Post.countByStatus"
        Count.byContains Table.Post {| webLogDoc webLogId with Status = status |}
    
    /// Find a post by its ID for the given web log (excluding revisions)
    let findById postId webLogId = backgroundTask {
        log.LogTrace "Post.findById"
        match! Document.findByIdAndWebLog<PostId, Post> Table.Post postId webLogId with
        | Some post -> return Some { post with PriorPermalinks = [] }
        | None -> return None
    }
    
    /// Find a post by its permalink for the given web log (excluding revisions)
    let findByPermalink (permalink: Permalink) webLogId =
        log.LogTrace "Post.findByPermalink"
        Custom.single
            (selectWithCriteria Table.Post)
            [ jsonParam "@criteria" {| webLogDoc webLogId with Permalink = permalink |} ]
            postWithoutLinks
    
    /// Find a complete post by its ID for the given web log
    let findFullById postId webLogId = backgroundTask {
        log.LogTrace "Post.findFullById"
        match! Document.findByIdAndWebLog<PostId, Post> Table.Post postId webLogId with
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
                    $"""DELETE FROM {Table.PostComment}  WHERE {Query.whereDataContains "@criteria"};
                        DELETE FROM {Table.PostRevision} WHERE post_id = @id;
                        DELETE FROM {Table.Post}         WHERE {Query.whereById "@id"}"""
                    [ idParam postId; jsonParam "@criteria" {| PostId = postId |} ]
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
                           AND {linkSql}"""
                    [ webLogContains webLogId; linkParam ]
                    Map.toPermalink
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
            [ jsonParam "@criteria" {| webLogDoc webLogId with Status = Published |}; catParam ]
            postWithoutLinks
    
    /// Get a page of posts for the given web log (excludes text and revisions)
    let findPageOfPosts webLogId pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfPosts"
        Custom.list
            $"{selectWithCriteria Table.Post}
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' DESC NULLS FIRST,
                        data ->> '{nameof Post.Empty.UpdatedOn}'
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [ webLogContains webLogId ]
            postWithoutText
    
    /// Get a page of published posts for the given web log (excludes revisions)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfPublishedPosts"
        Custom.list
            $"{selectWithCriteria Table.Post}
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [ jsonParam "@criteria" {| webLogDoc webLogId with Status = Published |} ]
            postWithoutLinks
    
    /// Get a page of tagged posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfTaggedPosts webLogId (tag: string) pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfTaggedPosts"
        Custom.list
            $"{selectWithCriteria Table.Post}
                 AND data['{nameof Post.Empty.Tags}'] @> @tag
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [ jsonParam "@criteria" {| webLogDoc webLogId with Status = Published |}; jsonParam "@tag" [| tag |] ]
            postWithoutLinks
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId (publishedOn: Instant) = backgroundTask {
        log.LogTrace "Post.findSurroundingPosts"
        let queryParams () =
            [ jsonParam "@criteria" {| webLogDoc webLogId with Status = Published |}
              "@publishedOn", Sql.timestamptz (publishedOn.ToDateTimeOffset()) ]
        let query op direction =
            $"{selectWithCriteria Table.Post}
                 AND (data ->> '{nameof Post.Empty.PublishedOn}')::timestamp with time zone %s{op} @publishedOn
               ORDER BY data ->> '{nameof Post.Empty.PublishedOn}' %s{direction}
               LIMIT 1"
        let! older = Custom.list (query "<" "DESC") (queryParams ()) postWithoutLinks
        let! newer = Custom.list (query ">" "")     (queryParams ()) postWithoutLinks
        return List.tryHead older, List.tryHead newer
    }
    
    /// Update a post
    let update (post : Post) = backgroundTask {
        log.LogTrace "Post.save"
        match! findFullById post.Id post.WebLogId with
        | Some oldPost ->
            do! Update.byId Table.Post post.Id { post with Revisions = [] }
            do! updatePostRevisions post.Id oldPost.Revisions post.Revisions
        | None -> ()
    }
    
    /// Restore posts from a backup
    let restore posts = backgroundTask {
        log.LogTrace "Post.restore"
        let revisions = posts |> List.collect (fun p -> p.Revisions |> List.map (fun r -> p.Id, r))
        let! _ =
            Configuration.dataSource ()
            |> Sql.fromDataSource
            |> Sql.executeTransactionAsync
                [ Query.insert Table.Post,
                    posts |> List.map (fun post -> [ jsonParam "@data" { post with Revisions = [] } ])
                  Revisions.insertSql Table.PostRevision,
                    revisions |> List.map (fun (postId, rev) -> Revisions.revParams postId rev) ]
        ()
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId (permalinks: Permalink list) = backgroundTask {
        log.LogTrace "Post.updatePriorPermalinks"
        match! postExists postId webLogId with
        | true ->
            do! Patch.byId Table.Post postId {| PriorPermalinks = permalinks |}
            return true
        | false -> return false
    }
    
    interface IPostData with
        member _.Add post = add post
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
        member _.Update post = update post
        member _.UpdatePriorPermalinks postId webLogId permalinks = updatePriorPermalinks postId webLogId permalinks
