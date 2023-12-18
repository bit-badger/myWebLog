namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open NodaTime

/// SQLite myWebLog post data implementation
type SQLitePostData(conn: SqliteConnection, ser: JsonSerializer, log: ILogger) =
    
    /// The JSON field for the post's permalink
    let linkField = $"data ->> '{nameof Post.Empty.Permalink}'"
    
    /// The JSON field for when the post was published
    let publishField = $"data ->> '{nameof Post.Empty.PublishedOn}'"
    
    /// The JSON field for post status
    let statField = $"data ->> '{nameof Post.Empty.Status}'"
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions to a post
    let appendPostRevisions (post: Post) = backgroundTask {
        log.LogTrace "Post.appendPostRevisions"
        let! revisions = Revisions.findByEntityId conn Table.PostRevision Table.Post post.Id
        return { post with Revisions = revisions }
    }
    
    /// The SELECT statement to retrieve posts with a web log ID parameter
    let postByWebLog = $"{Query.selectFromTable Table.Post} WHERE {Query.whereByWebLog}"
    
    /// The SELECT statement to retrieve published posts with a web log ID parameter
    let publishedPostByWebLog = $"{postByWebLog} AND {statField} = '{string Published}'"
    
    /// Remove the text from a post
    let withoutText (post: Post) =
        { post with Text = "" }
    
    /// Update a post's revisions
    let updatePostRevisions (postId: PostId) oldRevs newRevs =
        log.LogTrace "Post.updatePostRevisions"
        Revisions.update conn Table.PostRevision Table.Post postId oldRevs newRevs
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a post
    let add (post: Post) = backgroundTask {
        log.LogTrace "Post.add"
        do! Document.insert conn ser Table.Post { post with Revisions = [] }
        do! updatePostRevisions post.Id [] post.Revisions
    }
    
    /// Count posts in a status for the given web log
    let countByStatus (status: PostStatus) webLogId = backgroundTask {
        log.LogTrace "Post.countByStatus"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"{Query.countByWebLog Table.Post} AND {statField} = @status"
        addWebLogId cmd webLogId
        addParam cmd "@status" (string status)
        return! count cmd
    }
    
    /// Find a post by its ID for the given web log (excluding revisions and prior permalinks
    let findById postId webLogId =
        log.LogTrace "Post.findById"
        Document.findByIdAndWebLog<PostId, Post> conn ser Table.Post postId webLogId
    
    /// Find a post by its permalink for the given web log (excluding revisions and prior permalinks)
    let findByPermalink (permalink: Permalink) webLogId = backgroundTask {
        log.LogTrace "Post.findByPermalink"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"{Query.selectFromTable Table.Post} WHERE {Query.whereByWebLog} AND {linkField} = @link"
        addWebLogId cmd webLogId
        addParam cmd "@link" (string permalink)
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.fromDoc<Post> ser rdr) else None
    }
    
    /// Find a complete post by its ID for the given web log
    let findFullById postId webLogId = backgroundTask {
        log.LogTrace "Post.findFullById"
        match! findById postId webLogId with
        | Some post ->
            let! post = appendPostRevisions post
            return Some post
        | None -> return None
    }
    
    /// Delete a post by its ID for the given web log
    let delete postId webLogId = backgroundTask {
        log.LogTrace "Post.delete"
        match! findById postId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand()
            cmd.CommandText <- $"
                DELETE FROM {Table.PostRevision} WHERE post_id = @id;
                DELETE FROM {Table.PostComment}  WHERE data ->> '{nameof Comment.Empty.PostId}' = @id;
                DELETE FROM {Table.Post}         WHERE {Query.whereById}"
            addDocId cmd postId
            do! write cmd
            return true
        | None -> return false
    }
    
    /// Find the current permalink from a list of potential prior permalinks for the given web log
    let findCurrentPermalink (permalinks: Permalink list) webLogId = backgroundTask {
        log.LogTrace "Post.findCurrentPermalink"
        let linkSql, linkParams = inJsonArray Table.Post (nameof Post.Empty.PriorPermalinks) "link" permalinks
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
            $"SELECT {linkField} AS permalink FROM {Table.Post} WHERE {Query.whereByWebLog} AND {linkSql}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange linkParams
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.toPermalink rdr) else None
    }
    
    /// Get all complete posts for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        log.LogTrace "Post.findFullByWebLog"
        let! posts = Document.findByWebLog<Post> conn ser Table.Post webLogId
        let! withRevs =
            posts
            |> List.map (fun post -> backgroundTask { return! appendPostRevisions post })
            |> Task.WhenAll
        return List.ofArray withRevs
    }
    
    /// Get a page of categorized posts for the given web log (excludes revisions)
    let findPageOfCategorizedPosts webLogId (categoryIds: CategoryId list) pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfCategorizedPosts"
        let catSql, catParams = inJsonArray Table.Post (nameof Post.Empty.CategoryIds) "catId" categoryIds
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"
            {publishedPostByWebLog} AND {catSql}
             ORDER BY {publishField} DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange catParams
        cmdToList<Post> cmd ser
    
    /// Get a page of posts for the given web log (excludes revisions)
    let findPageOfPosts webLogId pageNbr postsPerPage = backgroundTask {
        log.LogTrace "Post.findPageOfPosts"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            {postByWebLog}
             ORDER BY {publishField} DESC NULLS FIRST, data ->> '{nameof Post.Empty.UpdatedOn}'
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        let! posts = cmdToList<Post> cmd ser
        return posts |> List.map withoutText
    }
    
    /// Get a page of published posts for the given web log (excludes revisions)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfPublishedPosts"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            {publishedPostByWebLog}
             ORDER BY {publishField} DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        cmdToList<Post> cmd ser
    
    /// Get a page of tagged posts for the given web log (excludes revisions)
    let findPageOfTaggedPosts webLogId (tag : string) pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfTaggedPosts"
        let tagSql, tagParams = inJsonArray Table.Post (nameof Post.Empty.Tags) "tag" [ tag ]
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            {publishedPostByWebLog} AND {tagSql}
             ORDER BY p.published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange tagParams
        cmdToList<Post> cmd ser
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId (publishedOn : Instant) = backgroundTask {
        log.LogTrace "Post.findSurroundingPosts"
        use cmd = conn.CreateCommand ()
        addWebLogId cmd webLogId
        addParam cmd "@publishedOn" (instantParam publishedOn)
        
        cmd.CommandText <-
            $"{publishedPostByWebLog} AND {publishField} < @publishedOn ORDER BY {publishField} DESC LIMIT 1"
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        let older = if isFound then Some (Map.fromDoc<Post> ser rdr) else None
        do! rdr.CloseAsync ()
        
        cmd.CommandText <-
            $"{publishedPostByWebLog} AND {publishField} > @publishedOn ORDER BY {publishField} LIMIT 1"
        use! rdr = cmd.ExecuteReaderAsync ()
        let! isFound = rdr.ReadAsync()
        let newer = if isFound then Some (Map.fromDoc<Post> ser rdr) else None
        
        return older, newer
    }
    
    /// Restore posts from a backup
    let restore posts = backgroundTask {
        log.LogTrace "Post.restore"
        for post in posts do
            do! add post
    }
    
    /// Update a post
    let update (post: Post) = backgroundTask {
        match! findFullById post.Id post.WebLogId with
        | Some oldPost ->
            do! Document.update conn ser Table.Post post.Id { post with Revisions = [] }
            do! updatePostRevisions post.Id oldPost.Revisions post.Revisions
        | None -> return ()
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId (permalinks: Permalink list) = backgroundTask {
        match! findById postId webLogId with
        | Some _ ->
            do! Document.updateField conn ser Table.Post postId (nameof Post.Empty.PriorPermalinks) permalinks
            return true
        | None -> return false
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
