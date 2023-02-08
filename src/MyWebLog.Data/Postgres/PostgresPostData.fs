namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open NodaTime
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog post data implementation        
type PostgresPostData (source : NpgsqlDataSource) =

    // SUPPORT FUNCTIONS
    
    /// Append revisions to a post
    let appendPostRevisions (post : Post) = backgroundTask {
        let! revisions = Revisions.findByEntityId source Table.PostRevision Table.Post post.Id PostId.toString
        return { post with Revisions = revisions }
    }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText row =
        { fromData<Post> row with Text = "" }
    
    /// Update a post's revisions
    let updatePostRevisions postId oldRevs newRevs =
        Revisions.update source Table.PostRevision Table.Post postId PostId.toString oldRevs newRevs
    
    /// Does the given post exist?
    let postExists postId webLogId =
        Document.existsByWebLog source Table.Post postId PostId.toString webLogId
    
    /// Query to select posts by JSON document containment criteria
    let postsByCriteria =
        $"""{Query.selectFromTable Table.Post} WHERE {Query.whereDataContains "@criteria"}"""
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Count posts in a status for the given web log
    let countByStatus status webLogId =
        Sql.fromDataSource source
        |> Sql.query
            $"""SELECT COUNT(id) AS {countName} FROM {Table.Post} WHERE {Query.whereDataContains "@criteria"}"""
        |> Sql.parameters
            [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = PostStatus.toString status |} ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Find a post by its ID for the given web log (excluding revisions)
    let findById postId webLogId =
        Document.findByIdAndWebLog<PostId, Post> source Table.Post postId PostId.toString webLogId
    
    /// Find a post by its permalink for the given web log (excluding revisions and prior permalinks)
    let findByPermalink permalink webLogId =
        Sql.fromDataSource source
        |> Sql.query postsByCriteria
        |> Sql.parameters
            [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Permalink = Permalink.toString permalink |} ]
        |> Sql.executeAsync fromData<Post>
        |> tryHead
    
    /// Find a complete post by its ID for the given web log
    let findFullById postId webLogId = backgroundTask {
        match! findById postId webLogId with
        | Some post ->
            let! withRevisions = appendPostRevisions post
            return Some withRevisions
        | None -> return None
    }
    
    /// Delete a post by its ID for the given web log
    let delete postId webLogId = backgroundTask {
        match! postExists postId webLogId with
        | true ->
            let theId = PostId.toString postId
            let! _ =
                Sql.fromDataSource source
                |> Sql.query $"""
                    DELETE FROM {Table.PostComment} WHERE {Query.whereDataContains "@criteria"};
                    DELETE FROM {Table.Post}        WHERE id = @id"""
                |> Sql.parameters [ "@id", Sql.string theId; "@criteria", Query.jsonbDocParam {| PostId = theId |} ]
                |> Sql.executeNonQueryAsync
            return true
        | false -> return false
    }
    
    /// Find the current permalink from a list of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        if List.isEmpty permalinks then return None
        else
            let linkSql, linkParams =
                jsonArrayInClause (nameof Post.empty.PriorPermalinks) Permalink.toString permalinks
            return!
                Sql.fromDataSource source
                |> Sql.query $"""
                    SELECT data->>'{nameof Post.empty.Permalink}' AS permalink
                      FROM {Table.Post}
                     WHERE {Query.whereDataContains "@criteria"}
                       AND ({linkSql})"""
                |> Sql.parameters (webLogContains webLogId :: linkParams)
                |> Sql.executeAsync Map.toPermalink
                |> tryHead
    }
    
    /// Get all complete posts for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        let! posts     = Document.findByWebLog<Post> source Table.Post webLogId
        let! revisions = Revisions.findByWebLog source Table.PostRevision Table.Post PostId webLogId
        return
            posts
            |> List.map (fun it ->
                { it with Revisions = revisions |> List.filter (fun r -> fst r = it.Id) |> List.map snd })
    }
    
    /// Get a page of categorized posts for the given web log (excludes revisions)
    let findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage =
        let catSql, catParams = jsonArrayInClause (nameof Post.empty.CategoryIds) CategoryId.toString categoryIds
        Sql.fromDataSource source
        |> Sql.query $"
            {postsByCriteria}
               AND ({catSql})
             ORDER BY published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        |> Sql.parameters (
            ("@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = PostStatus.toString Published |})
            :: catParams)
        |> Sql.executeAsync fromData<Post>
    
    /// Get a page of posts for the given web log (excludes text and revisions)
    let findPageOfPosts webLogId pageNbr postsPerPage =
        Sql.fromDataSource source
        |> Sql.query $"
            {postsByCriteria}
             ORDER BY data->>'{nameof Post.empty.PublishedOn}' DESC NULLS FIRST,
                      data->>'{nameof Post.empty.UpdatedOn}'
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        |> Sql.parameters [ webLogContains webLogId ]
        |> Sql.executeAsync postWithoutText
    
    /// Get a page of published posts for the given web log (excludes revisions)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage =
        Sql.fromDataSource source
        |> Sql.query $"
            {postsByCriteria}
             ORDER BY data->>'{nameof Post.empty.PublishedOn}' DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        |> Sql.parameters
            [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = PostStatus.toString Published |} ]
        |> Sql.executeAsync fromData<Post>
    
    /// Get a page of tagged posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfTaggedPosts webLogId (tag : string) pageNbr postsPerPage =
        Sql.fromDataSource source
        |> Sql.query $"
            {postsByCriteria}
               AND data->'{nameof Post.empty.Tags}' ? @tag
             ORDER BY data->>'{nameof Post.empty.PublishedOn}' DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        |> Sql.parameters
            [   "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = PostStatus.toString Published |}
                "@tag",      Sql.jsonb tag
            ]
        |> Sql.executeAsync fromData<Post>
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId (publishedOn : Instant) = backgroundTask {
        let queryParams () = Sql.parameters [
            "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with Status = PostStatus.toString Published |}
            typedParam "publishedOn" publishedOn
        ]
        let! older =
            Sql.fromDataSource source
            |> Sql.query $"
                {postsByCriteria}
                   AND data->>'{nameof Post.empty.PublishedOn}' < @publishedOn
                 ORDER BY data->>'{nameof Post.empty.PublishedOn}' DESC
                 LIMIT 1"
            |> queryParams ()
            |> Sql.executeAsync fromData<Post>
        let! newer =
            Sql.fromDataSource source
            |> Sql.query $"
                {postsByCriteria}
                   AND data->>'{nameof Post.empty.PublishedOn}' > @publishedOn
                 ORDER BY data->>'{nameof Post.empty.PublishedOn}'
                 LIMIT 1"
            |> queryParams ()
            |> Sql.executeAsync fromData<Post>
        return List.tryHead older, List.tryHead newer
    }
    
    /// The parameters for saving a post
    let postParams (post : Post) =
        Query.docParameters (PostId.toString post.Id) post
    
    /// Save a post
    let save (post : Post) = backgroundTask {
        let! oldPost = findFullById post.Id post.WebLogId
        do! Sql.fromDataSource source |> Query.save Table.Post (PostId.toString post.Id) post
        do! updatePostRevisions post.Id (match oldPost with Some p -> p.Revisions | None -> []) post.Revisions
    }
    
    /// Restore posts from a backup
    let restore posts = backgroundTask {
        let revisions = posts |> List.collect (fun p -> p.Revisions |> List.map (fun r -> p.Id, r))
        let! _ =
            Sql.fromDataSource source
            |> Sql.executeTransactionAsync [
                Query.insertQuery Table.Post, posts |> List.map postParams
                Revisions.insertSql Table.PostRevision,
                    revisions |> List.map (fun (postId, rev) -> Revisions.revParams postId PostId.toString rev)
            ]
        ()
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId permalinks = backgroundTask {
        match! findById postId webLogId with
        | Some post ->
            do! Sql.fromDataSource source
                |> Query.update Table.Post (PostId.toString post.Id) { post with PriorPermalinks = permalinks }
            return true
        | None -> return false
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
