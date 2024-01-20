namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open BitBadger.Documents
open BitBadger.Documents.Sqlite
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open NodaTime

/// SQLite myWebLog post data implementation
type SQLitePostData(conn: SqliteConnection, log: ILogger) =
    
    /// The name of the JSON field for the post's permalink
    let linkName = nameof Post.Empty.Permalink
    
    /// The JSON field for when the post was published
    let publishField = $"data ->> '{nameof Post.Empty.PublishedOn}'"
    
    /// The name of the JSON field for the post's status
    let statName = nameof Post.Empty.Status
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions to a post
    let appendPostRevisions (post: Post) = backgroundTask {
        log.LogTrace "Post.appendPostRevisions"
        let! revisions = Revisions.findByEntityId Table.PostRevision Table.Post post.Id conn
        return { post with Revisions = revisions }
    }
    
    /// The SELECT statement to retrieve posts with a web log ID parameter
    let postByWebLog = Document.Query.selectByWebLog Table.Post
    
    /// The SELECT statement to retrieve published posts with a web log ID parameter
    let publishedPostByWebLog = $"""{postByWebLog} AND {Query.whereByField statName EQ $"'{string Published}'"}"""
    
    /// Update a post's revisions
    let updatePostRevisions (postId: PostId) oldRevs newRevs =
        log.LogTrace "Post.updatePostRevisions"
        Revisions.update Table.PostRevision Table.Post postId oldRevs newRevs conn
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Count posts in a status for the given web log
    let countByStatus (status: PostStatus) webLogId =
        log.LogTrace "Post.countByStatus"
        conn.customScalar
            $"""{Document.Query.countByWebLog Table.Post} AND {Query.whereByField statName EQ "@status"}"""
            [ webLogParam webLogId; SqliteParameter("@status", string status) ]
            (toCount >> int)
    
    /// Find a post by its ID for the given web log (excluding revisions)
    let findById postId webLogId =
        log.LogTrace "Post.findById"
        Document.findByIdAndWebLog<PostId, Post> Table.Post postId webLogId conn
    
    /// Find a post by its permalink for the given web log (excluding revisions)
    let findByPermalink (permalink: Permalink) webLogId =
        log.LogTrace "Post.findByPermalink"
        conn.customSingle
            $"""{Document.Query.selectByWebLog Table.Post} AND {Query.whereByField linkName EQ "@link"}"""
            [ webLogParam webLogId; SqliteParameter("@link", string permalink) ]
            fromData<Post>
    
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
            do! conn.customNonQuery
                    $"""DELETE FROM {Table.PostRevision} WHERE post_id = @id;
                        DELETE FROM {Table.PostComment}
                         WHERE {Query.whereByField (nameof Comment.Empty.PostId) EQ "@id"};
                        {Query.Delete.byId Table.Post}"""
                    [ idParam postId ]
            return true
        | None -> return false
    }
    
    /// Find the current permalink from a list of potential prior permalinks for the given web log
    let findCurrentPermalink (permalinks: Permalink list) webLogId =
        log.LogTrace "Post.findCurrentPermalink"
        let linkSql, linkParams = inJsonArray Table.Post (nameof Post.Empty.PriorPermalinks) "link" permalinks
        conn.customSingle
            $"SELECT data ->> '{linkName}' AS permalink
                FROM {Table.Post}
               WHERE {Document.Query.whereByWebLog} AND {linkSql}"
            (webLogParam webLogId :: linkParams)
            Map.toPermalink
    
    /// Get all complete posts for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        log.LogTrace "Post.findFullByWebLog"
        let! posts    = Document.findByWebLog<Post> Table.Post webLogId conn
        let! withRevs = posts |> List.map appendPostRevisions |> Task.WhenAll
        return List.ofArray withRevs
    }
    
    /// Get a page of categorized posts for the given web log (excludes revisions)
    let findPageOfCategorizedPosts webLogId (categoryIds: CategoryId list) pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfCategorizedPosts"
        let catSql, catParams = inJsonArray Table.Post (nameof Post.Empty.CategoryIds) "catId" categoryIds
        conn.customList
            $"{publishedPostByWebLog} AND {catSql}
               ORDER BY {publishField} DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            (webLogParam webLogId :: catParams)
            fromData<Post>
    
    /// Get a page of posts for the given web log (excludes text and revisions)
    let findPageOfPosts webLogId pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfPosts"
        conn.customList
            $"{postByWebLog}
               ORDER BY {publishField} DESC NULLS FIRST, data ->> '{nameof Post.Empty.UpdatedOn}'
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [ webLogParam webLogId ]
            (fun rdr -> { fromData<Post> rdr with Text = "" })
    
    /// Get a page of published posts for the given web log (excludes revisions)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfPublishedPosts"
        conn.customList
            $"{publishedPostByWebLog}
               ORDER BY {publishField} DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            [ webLogParam webLogId ]
            fromData<Post>
    
    /// Get a page of tagged posts for the given web log (excludes revisions)
    let findPageOfTaggedPosts webLogId (tag : string) pageNbr postsPerPage =
        log.LogTrace "Post.findPageOfTaggedPosts"
        let tagSql, tagParams = inJsonArray Table.Post (nameof Post.Empty.Tags) "tag" [ tag ]
        conn.customList
            $"{publishedPostByWebLog} AND {tagSql}
               ORDER BY {publishField} DESC
               LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
            (webLogParam webLogId :: tagParams)
            fromData<Post>
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId (publishedOn : Instant) = backgroundTask {
        log.LogTrace "Post.findSurroundingPosts"
        let! older =
            conn.customSingle
                $"{publishedPostByWebLog} AND {publishField} < @publishedOn ORDER BY {publishField} DESC LIMIT 1"
                [ webLogParam webLogId; SqliteParameter("@publishedOn", instantParam publishedOn) ]
                fromData<Post>
        let! newer =
            conn.customSingle
                $"{publishedPostByWebLog} AND {publishField} > @publishedOn ORDER BY {publishField} LIMIT 1"
                [ webLogParam webLogId; SqliteParameter("@publishedOn", instantParam publishedOn) ]
                fromData<Post>
        return older, newer
    }
    
    /// Save a post
    let save (post: Post) = backgroundTask {
        log.LogTrace "Post.save"
        let! oldPost = findFullById post.Id post.WebLogId
        do! conn.save Table.Post { post with Revisions = [] }
        do! updatePostRevisions post.Id (match oldPost with Some p -> p.Revisions | None -> []) post.Revisions
    }
    
    /// Restore posts from a backup
    let restore posts = backgroundTask {
        log.LogTrace "Post.restore"
        for post in posts do do! save post
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId (permalinks: Permalink list) = backgroundTask {
        match! findById postId webLogId with
        | Some _ ->
            do! conn.patchById Table.Post postId {| PriorPermalinks = permalinks |}
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
