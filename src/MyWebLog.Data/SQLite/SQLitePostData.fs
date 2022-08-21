namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open NodaTime

/// SQLite myWebLog post data implementation        
type SQLitePostData (conn : SqliteConnection, ser : JsonSerializer) =

    // SUPPORT FUNCTIONS
    
    /// Add parameters for post INSERT or UPDATE statements
    let addPostParameters (cmd : SqliteCommand) (post : Post) =
        [   cmd.Parameters.AddWithValue ("@id",          PostId.toString post.Id)
            cmd.Parameters.AddWithValue ("@webLogId",    WebLogId.toString post.WebLogId)
            cmd.Parameters.AddWithValue ("@authorId",    WebLogUserId.toString post.AuthorId)
            cmd.Parameters.AddWithValue ("@status",      PostStatus.toString post.Status)
            cmd.Parameters.AddWithValue ("@title",       post.Title)
            cmd.Parameters.AddWithValue ("@permalink",   Permalink.toString post.Permalink)
            cmd.Parameters.AddWithValue ("@publishedOn", maybeInstant post.PublishedOn)
            cmd.Parameters.AddWithValue ("@updatedOn",   instantParam post.UpdatedOn)
            cmd.Parameters.AddWithValue ("@template",    maybe post.Template)
            cmd.Parameters.AddWithValue ("@text",        post.Text)
            cmd.Parameters.AddWithValue ("@episode",     maybe (if Option.isSome post.Episode then
                                                                    Some (Utils.serialize ser post.Episode)
                                                                else None))
            cmd.Parameters.AddWithValue ("@metaItems",   maybe (if List.isEmpty post.Metadata then None
                                                                else Some (Utils.serialize ser post.Metadata)))
        ] |> ignore
    
    /// Append category IDs and tags to a post
    let appendPostCategoryAndTag (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@id", PostId.toString post.Id) |> ignore
        
        cmd.CommandText <- "SELECT category_id AS id FROM post_category WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with CategoryIds = toList Map.toCategoryId rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT tag FROM post_tag WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { post with Tags = toList (Map.getString "tag") rdr }
    }
    
    /// Append revisions and permalinks to a post
    let appendPostRevisionsAndPermalinks (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@postId", PostId.toString post.Id) |> ignore
        
        cmd.CommandText <- "SELECT permalink FROM post_permalink WHERE post_id = @postId"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with PriorPermalinks = toList Map.toPermalink rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT as_of, revision_text FROM post_revision WHERE post_id = @postId ORDER BY as_of DESC"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { post with Revisions = toList Map.toRevision rdr }
    }
    
    /// The SELECT statement for a post that will include episode data, if it exists
    let selectPost = "SELECT p.* FROM post p"
    
    /// Shorthand for mapping a data reader to a post
    let toPost =
        Map.toPost ser
    
    /// Find just-the-post by its ID for the given web log (excludes category, tag, meta, revisions, and permalinks)
    let findPostById postId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"{selectPost} WHERE p.id = @id"
        cmd.Parameters.AddWithValue ("@id", PostId.toString postId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return Helpers.verifyWebLog<Post> webLogId (fun p -> p.WebLogId) toPost rdr
    }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText rdr =
        { toPost rdr with Text = "" }
    
    /// Update a post's assigned categories
    let updatePostCategories postId oldCats newCats = backgroundTask {
        let toDelete, toAdd = Utils.diffLists oldCats newCats CategoryId.toString
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [   cmd.Parameters.AddWithValue ("@postId",     PostId.toString postId)
                cmd.Parameters.Add          ("@categoryId", SqliteType.Text)
            ] |> ignore
            let runCmd catId = backgroundTask {
                cmd.Parameters["@categoryId"].Value <- CategoryId.toString catId
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_category WHERE post_id = @postId AND category_id = @categoryId" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_category VALUES (@postId, @categoryId)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a post's assigned categories
    let updatePostTags postId (oldTags : string list) newTags = backgroundTask {
        let toDelete, toAdd = Utils.diffLists oldTags newTags id
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [   cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                cmd.Parameters.Add          ("@tag",    SqliteType.Text)
            ] |> ignore
            let runCmd (tag : string) = backgroundTask {
                cmd.Parameters["@tag"].Value <- tag
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_tag WHERE post_id = @postId AND tag = @tag" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_tag VALUES (@postId, @tag)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a post's prior permalinks
    let updatePostPermalinks postId oldLinks newLinks = backgroundTask {
        let toDelete, toAdd = Utils.diffPermalinks oldLinks newLinks
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [   cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                cmd.Parameters.Add          ("@link",   SqliteType.Text)
            ] |> ignore
            let runCmd link = backgroundTask {
                cmd.Parameters["@link"].Value <- Permalink.toString link
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_permalink WHERE post_id = @postId AND permalink = @link" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_permalink VALUES (@postId, @link)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a post's revisions
    let updatePostRevisions postId oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = Utils.diffRevisions oldRevs newRevs
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            let runCmd withText rev = backgroundTask {
                cmd.Parameters.Clear ()
                [   cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                    cmd.Parameters.AddWithValue ("@asOf",   instantParam rev.AsOf)
                ] |> ignore
                if withText then cmd.Parameters.AddWithValue ("@text", MarkupText.toString rev.Text) |> ignore
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_revision WHERE post_id = @postId AND as_of = @asOf" 
            toDelete
            |> List.map (runCmd false)
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_revision VALUES (@postId, @asOf, @text)"
            toAdd
            |> List.map (runCmd true)
            |> Task.WhenAll
            |> ignore
    }
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a post
    let add post = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            "INSERT INTO post (
                id, web_log_id, author_id, status, title, permalink, published_on, updated_on, template, post_text,
                episode, meta_items
            ) VALUES (
                @id, @webLogId, @authorId, @status, @title, @permalink, @publishedOn, @updatedOn, @template, @text,
                @episode, @metaItems
            )"
        addPostParameters cmd post
        do! write cmd
        do! updatePostCategories post.Id [] post.CategoryIds
        do! updatePostTags       post.Id [] post.Tags
        do! updatePostPermalinks post.Id [] post.PriorPermalinks
        do! updatePostRevisions  post.Id [] post.Revisions
    }
    
    /// Count posts in a status for the given web log
    let countByStatus status webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT COUNT(id) FROM post WHERE web_log_id = @webLogId AND status = @status"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@status", PostStatus.toString status) |> ignore
        return! count cmd
    }
    
    /// Find a post by its ID for the given web log (excluding revisions and prior permalinks
    let findById postId webLogId = backgroundTask {
        match! findPostById postId webLogId with
        | Some post ->
            let! post = appendPostCategoryAndTag post
            return Some post
        | None -> return None
    }
    
    /// Find a post by its permalink for the given web log (excluding revisions and prior permalinks)
    let findByPermalink permalink webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"{selectPost} WHERE p.web_log_id = @webLogId AND p.permalink  = @link"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@link", Permalink.toString permalink) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        if rdr.Read () then
            let! post = appendPostCategoryAndTag (toPost rdr)
            return Some post
        else
            return None
    }
    
    /// Find a complete post by its ID for the given web log
    let findFullById postId webLogId = backgroundTask {
        match! findById postId webLogId with
        | Some post ->
            let! post = appendPostRevisionsAndPermalinks post
            return Some post
        | None -> return None
    }
    
    /// Delete a post by its ID for the given web log
    let delete postId webLogId = backgroundTask {
        match! findFullById postId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand ()
            cmd.Parameters.AddWithValue ("@id", PostId.toString postId) |> ignore
            cmd.CommandText <-
                "DELETE FROM post_revision  WHERE post_id = @id;
                 DELETE FROM post_permalink WHERE post_id = @id;
                 DELETE FROM post_tag       WHERE post_id = @id;
                 DELETE FROM post_category  WHERE post_id = @id;
                 DELETE FROM post_comment   WHERE post_id = @id;
                 DELETE FROM post           WHERE id      = @id"
            do! write cmd
            return true
        | None -> return false
    }
    
    /// Find the current permalink from a list of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        let linkSql, linkParams = inClause "AND pp.permalink" "link" Permalink.toString permalinks
        cmd.CommandText <- $"
            SELECT p.permalink
               FROM post p
                    INNER JOIN post_permalink pp ON pp.post_id = p.id
              WHERE p.web_log_id = @webLogId
                {linkSql}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange linkParams
        use! rdr = cmd.ExecuteReaderAsync ()
        return if rdr.Read () then Some (Map.toPermalink rdr) else None
    }
    
    /// Get all complete posts for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"{selectPost} WHERE p.web_log_id = @webLogId"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList toPost rdr
            |> List.map (fun post -> backgroundTask {
                let! post = appendPostCategoryAndTag post
                return! appendPostRevisionsAndPermalinks post
            })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of categorized posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        let catSql, catParams = inClause "AND pc.category_id" "catId" CategoryId.toString categoryIds
        cmd.CommandText <- $"
            {selectPost}
                   INNER JOIN post_category pc ON pc.post_id = p.id
             WHERE p.web_log_id = @webLogId
               AND p.status     = @status
               {catSql}
             ORDER BY published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published) |> ignore
        cmd.Parameters.AddRange catParams
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList toPost rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryAndTag post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of posts for the given web log (excludes text, revisions, and prior permalinks)
    let findPageOfPosts webLogId pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"
            {selectPost}
             WHERE p.web_log_id = @webLogId
             ORDER BY p.published_on DESC NULLS FIRST, p.updated_on
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList postWithoutText rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryAndTag post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of published posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"
            {selectPost}
             WHERE p.web_log_id = @webLogId
               AND p.status     = @status
             ORDER BY p.published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList toPost rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryAndTag post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of tagged posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfTaggedPosts webLogId (tag : string) pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"
            {selectPost}
                   INNER JOIN post_tag pt ON pt.post_id = p.id
             WHERE p.web_log_id = @webLogId
               AND p.status     = @status
               AND pt.tag       = @tag
             ORDER BY p.published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"
        addWebLogId cmd webLogId
        [ cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published)
          cmd.Parameters.AddWithValue ("@tag", tag)
        ] |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList toPost rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryAndTag post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId (publishedOn : Instant) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"
            {selectPost}
             WHERE p.web_log_id   = @webLogId
               AND p.status       = @status
               AND p.published_on < @publishedOn
             ORDER BY p.published_on DESC
             LIMIT 1"
        addWebLogId cmd webLogId
        [   cmd.Parameters.AddWithValue ("@status",      PostStatus.toString Published)
            cmd.Parameters.AddWithValue ("@publishedOn", instantParam publishedOn)
        ] |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! older = backgroundTask {
            if rdr.Read () then
                let! post = appendPostCategoryAndTag (postWithoutText rdr)
                return Some post
            else
                return None
        }
        do! rdr.CloseAsync ()
        cmd.CommandText <- $"
            {selectPost}
             WHERE p.web_log_id   = @webLogId
               AND p.status       = @status
               AND p.published_on > @publishedOn
             ORDER BY p.published_on
             LIMIT 1"
        use! rdr = cmd.ExecuteReaderAsync ()
        let! newer = backgroundTask {
            if rdr.Read () then
                let! post = appendPostCategoryAndTag (postWithoutText rdr)
                return Some post
            else
                return None
        }
        return older, newer
    }
    
    /// Restore posts from a backup
    let restore posts = backgroundTask {
        for post in posts do
            do! add post
    }
    
    /// Update a post
    let update (post : Post) = backgroundTask {
        match! findFullById post.Id post.WebLogId with
        | Some oldPost ->
            use cmd = conn.CreateCommand ()
            cmd.CommandText <-
                "UPDATE post
                    SET author_id    = @authorId,
                        status       = @status,
                        title        = @title,
                        permalink    = @permalink,
                        published_on = @publishedOn,
                        updated_on   = @updatedOn,
                        template     = @template,
                        post_text    = @text,
                        episode      = @episode,
                        meta_items   = @metaItems
                  WHERE id         = @id
                    AND web_log_id = @webLogId"
            addPostParameters cmd post
            do! write cmd
            do! updatePostCategories post.Id oldPost.CategoryIds     post.CategoryIds
            do! updatePostTags       post.Id oldPost.Tags            post.Tags
            do! updatePostPermalinks post.Id oldPost.PriorPermalinks post.PriorPermalinks
            do! updatePostRevisions  post.Id oldPost.Revisions       post.Revisions
        | None -> return ()
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId permalinks = backgroundTask {
        match! findFullById postId webLogId with
        | Some post ->
            do! updatePostPermalinks postId post.PriorPermalinks permalinks
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
