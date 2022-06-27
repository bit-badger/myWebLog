namespace MyWebLog.Data.SQLite

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog post data implementation        
type SQLitePostData (conn : SqliteConnection) =

    // SUPPORT FUNCTIONS
    
    /// Add parameters for post INSERT or UPDATE statements
    let addPostParameters (cmd : SqliteCommand) (post : Post) =
        [ cmd.Parameters.AddWithValue ("@id", PostId.toString post.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString post.webLogId)
          cmd.Parameters.AddWithValue ("@authorId", WebLogUserId.toString post.authorId)
          cmd.Parameters.AddWithValue ("@status", PostStatus.toString post.status)
          cmd.Parameters.AddWithValue ("@title", post.title)
          cmd.Parameters.AddWithValue ("@permalink", Permalink.toString post.permalink)
          cmd.Parameters.AddWithValue ("@publishedOn", maybe post.publishedOn)
          cmd.Parameters.AddWithValue ("@updatedOn", post.updatedOn)
          cmd.Parameters.AddWithValue ("@template", maybe post.template)
          cmd.Parameters.AddWithValue ("@text", post.text)
        ] |> ignore
    
    /// Add parameters for episode INSERT or UPDATE statements
    let addEpisodeParameters (cmd : SqliteCommand) (ep : Episode) =
        [ cmd.Parameters.AddWithValue ("@media", ep.media)
          cmd.Parameters.AddWithValue ("@length", ep.length)
          cmd.Parameters.AddWithValue ("@duration", maybe ep.duration)
          cmd.Parameters.AddWithValue ("@mediaType", maybe ep.mediaType)
          cmd.Parameters.AddWithValue ("@imageUrl", maybe ep.imageUrl)
          cmd.Parameters.AddWithValue ("@subtitle", maybe ep.subtitle)
          cmd.Parameters.AddWithValue ("@explicit", maybe (ep.explicit |> Option.map ExplicitRating.toString))
          cmd.Parameters.AddWithValue ("@chapterFile", maybe ep.chapterFile)
          cmd.Parameters.AddWithValue ("@chapterType", maybe ep.chapterType)
          cmd.Parameters.AddWithValue ("@transcriptUrl", maybe ep.transcriptUrl)
          cmd.Parameters.AddWithValue ("@transcriptType", maybe ep.transcriptType)
          cmd.Parameters.AddWithValue ("@transcriptLang", maybe ep.transcriptLang)
          cmd.Parameters.AddWithValue ("@transcriptCaptions", maybe ep.transcriptCaptions)
          cmd.Parameters.AddWithValue ("@seasonNumber", maybe ep.seasonNumber)
          cmd.Parameters.AddWithValue ("@seasonDescription", maybe ep.seasonDescription)
          cmd.Parameters.AddWithValue ("@episodeNumber", maybe (ep.episodeNumber |> Option.map string))
          cmd.Parameters.AddWithValue ("@episodeDescription", maybe ep.episodeDescription)
        ] |> ignore
        
    /// Append category IDs, tags, and meta items to a post
    let appendPostCategoryTagAndMeta (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@id", PostId.toString post.id) |> ignore
        
        cmd.CommandText <- "SELECT category_id AS id FROM post_category WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with categoryIds = toList Map.toCategoryId rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT tag FROM post_tag WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with tags = toList (Map.getString "tag") rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT name, value FROM post_meta WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { post with metadata = toList Map.toMetaItem rdr }
    }
    
    /// Append revisions and permalinks to a post
    let appendPostRevisionsAndPermalinks (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@postId", PostId.toString post.id) |> ignore
        
        cmd.CommandText <- "SELECT permalink FROM post_permalink WHERE post_id = @postId"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with priorPermalinks = toList Map.toPermalink rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT as_of, revision_text FROM post_revision WHERE post_id = @postId ORDER BY as_of DESC"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { post with revisions = toList Map.toRevision rdr }
    }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText rdr =
        { Map.toPost rdr with text = "" }
    
    /// Update a post's assigned categories
    let updatePostCategories postId oldCats newCats = backgroundTask {
        let toDelete, toAdd = diffLists oldCats newCats CategoryId.toString
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@categoryId", SqliteType.Text)
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
        let toDelete, toAdd = diffLists oldTags newTags id
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@tag", SqliteType.Text)
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
    
    /// Update an episode
    let updatePostEpisode (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT COUNT(post_id) FROM post_episode WHERE post_id = @postId"
        cmd.Parameters.AddWithValue ("@postId", PostId.toString post.id) |> ignore
        let! count = count cmd
        if count = 1 then
            match post.episode with
            | Some ep ->
                cmd.CommandText <-
                    """UPDATE post_episode
                          SET media               = @media,
                              length              = @length,
                              duration            = @duration,
                              media_type          = @mediaType,
                              image_url           = @imageUrl,
                              subtitle            = @subtitle,
                              explicit            = @explicit,
                              chapter_file        = @chapterFile,
                              chapter_type        = @chapterType,
                              transcript_url      = @transcriptUrl,
                              transcript_type     = @transcriptType,
                              transcript_lang     = @transcriptLang,
                              transcript_captions = @transcriptCaptions,
                              season_number       = @seasonNumber,
                              season_description  = @seasonDescription,
                              episode_number      = @episodeNumber,
                              episode_description = @episodeDescription
                        WHERE post_id = @postId"""
                addEpisodeParameters cmd ep
                do! write cmd
            | None ->
                cmd.CommandText <- "DELETE FROM post_episode WHERE post_id = @postId"
                do! write cmd
        else
            match post.episode with
            | Some ep ->
                cmd.CommandText <-
                    """INSERT INTO post_episode (
                           post_id, media, length, duration, media_type, image_url, subtitle, explicit,
                           chapter_file, chapter_type, transcript_url, transcript_type, transcript_lang,
                           transcript_captions, season_number, season_description, episode_number, episode_description
                       ) VALUES (
                           @postId, @media, @length, @duration, @mediaType, @imageUrl, @subtitle, @explicit,
                           @chapterFile, @chapterType, @transcriptUrl, @transcriptType, @transcriptLang,
                           @transcriptCaptions, @seasonNumber, @seasonDescription, @episodeNumber, @episodeDescription
                       )"""
                addEpisodeParameters cmd ep
                do! write cmd
            | None -> ()
    }
    
    /// Update a post's metadata items
    let updatePostMeta postId oldItems newItems = backgroundTask {
        let toDelete, toAdd = diffMetaItems oldItems newItems
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@name", SqliteType.Text)
              cmd.Parameters.Add ("@value", SqliteType.Text)
            ] |> ignore
            let runCmd (item : MetaItem) = backgroundTask {
                cmd.Parameters["@name" ].Value <- item.name
                cmd.Parameters["@value"].Value <- item.value
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_meta WHERE post_id = @postId AND name = @name AND value = @value" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_meta VALUES (@postId, @name, @value)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a post's prior permalinks
    let updatePostPermalinks postId oldLinks newLinks = backgroundTask {
        let toDelete, toAdd = diffPermalinks oldLinks newLinks
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@link", SqliteType.Text)
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
        let toDelete, toAdd = diffRevisions oldRevs newRevs
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            let runCmd withText rev = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                  cmd.Parameters.AddWithValue ("@asOf", rev.asOf)
                ] |> ignore
                if withText then cmd.Parameters.AddWithValue ("@text", MarkupText.toString rev.text) |> ignore
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
    
    /// The SELECT statement for a post that will include episode data, if it exists
    let selectPost = "SELECT p.*, e.* FROM post p LEFT JOIN post_episode e ON e.post_id = p.id"
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a post
    let add post = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """INSERT INTO post (
                   id, web_log_id, author_id, status, title, permalink, published_on, updated_on,
                   template, post_text
               ) VALUES (
                   @id, @webLogId, @authorId, @status, @title, @permalink, @publishedOn, @updatedOn,
                   @template, @text
               )"""
        addPostParameters cmd post
        do! write cmd
        do! updatePostCategories post.id [] post.categoryIds
        do! updatePostTags       post.id [] post.tags
        do! updatePostEpisode    post
        do! updatePostMeta       post.id [] post.metadata
        do! updatePostPermalinks post.id [] post.priorPermalinks
        do! updatePostRevisions  post.id [] post.revisions
    }
    
    /// Count posts in a status for the given web log
    let countByStatus status webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT COUNT(id) FROM post WHERE web_log_id = @webLogId AND status = @status"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@status", PostStatus.toString status) |> ignore
        return! count cmd
    }
    
    /// Find a post by its permalink for the given web log (excluding revisions and prior permalinks)
    let findByPermalink permalink webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"{selectPost} WHERE p.web_log_id = @webLogId AND p.permalink  = @link"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@link", Permalink.toString permalink) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        if rdr.Read () then
            let! post = appendPostCategoryTagAndMeta (Map.toPost rdr)
            return Some post
        else
            return None
    }
    
    /// Find a complete post by its ID for the given web log
    let findFullById postId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"{selectPost} WHERE p.id = @id"
        cmd.Parameters.AddWithValue ("@id", PostId.toString postId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        match Helpers.verifyWebLog<Post> webLogId (fun p -> p.webLogId) Map.toPost rdr with
        | Some post ->
            let! post = appendPostCategoryTagAndMeta     post
            let! post = appendPostRevisionsAndPermalinks post
            return Some post
        | None ->
            return None
    }
    
    /// Delete a post by its ID for the given web log
    let delete postId webLogId = backgroundTask {
        match! findFullById postId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand ()
            cmd.Parameters.AddWithValue ("@id", PostId.toString postId) |> ignore
            cmd.CommandText <-
                """DELETE FROM post_revision  WHERE post_id = @id;
                   DELETE FROM post_permalink WHERE post_id = @id;
                   DELETE FROM post_meta      WHERE post_id = @id;
                   DELETE FROM post_episode   WHERE post_id = @id;
                   DELETE FROM post_tag       WHERE post_id = @id;
                   DELETE FROM post_category  WHERE post_id = @id;
                   DELETE FROM post           WHERE id      = @id"""
            do! write cmd
            return true
        | None -> return false
    }
    
    /// Find the current permalink from a list of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """SELECT p.permalink
                 FROM post p
                      INNER JOIN post_permalink pp ON pp.post_id = p.id
                WHERE p.web_log_id = @webLogId
                  AND pp.permalink IN ("""
        permalinks
        |> List.iteri (fun idx link ->
            if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
            cmd.CommandText <- $"{cmd.CommandText}@link{idx}"
            cmd.Parameters.AddWithValue ($"@link{idx}", Permalink.toString link) |> ignore)
        cmd.CommandText <- $"{cmd.CommandText})"
        addWebLogId cmd webLogId
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
            toList Map.toPost rdr
            |> List.map (fun post -> backgroundTask {
                let! post = appendPostCategoryTagAndMeta post
                return! appendPostRevisionsAndPermalinks post
            })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of categorized posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            $"""{selectPost}
                      INNER JOIN post_category pc ON pc.post_id = p.id
                WHERE p.web_log_id = @webLogId
                  AND p.status     = @status
                  AND pc.category_id IN ("""
        categoryIds
        |> List.iteri (fun idx catId ->
            if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
            cmd.CommandText <- $"{cmd.CommandText}@catId{idx}"
            cmd.Parameters.AddWithValue ($"@catId{idx}", CategoryId.toString catId) |> ignore)
        cmd.CommandText <-
            $"""{cmd.CommandText})
                ORDER BY published_on DESC
                LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList Map.toPost rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of posts for the given web log (excludes text, revisions, and prior permalinks)
    let findPageOfPosts webLogId pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            $"""{selectPost}
                 WHERE p.web_log_id = @webLogId
                 ORDER BY p.published_on DESC NULLS FIRST, p.updated_on
                 LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList postWithoutText rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of published posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            $"""{selectPost}
                 WHERE p.web_log_id = @webLogId
                   AND p.status     = @status
                 ORDER BY p.published_on DESC
                 LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList Map.toPost rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Get a page of tagged posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfTaggedPosts webLogId (tag : string) pageNbr postsPerPage = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            $"""{selectPost}
                       INNER JOIN post_tag pt ON pt.post_id = p.id
                 WHERE p.web_log_id = @webLogId
                   AND p.status     = @status
                   AND pt.tag       = @tag
                 ORDER BY p.published_on DESC
                 LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        addWebLogId cmd webLogId
        [ cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published)
          cmd.Parameters.AddWithValue ("@tag", tag)
        ] |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! posts =
            toList Map.toPost rdr
            |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
            |> Task.WhenAll
        return List.ofArray posts
    }
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId (publishedOn : DateTime) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            $"""{selectPost}
                WHERE p.web_log_id   = @webLogId
                  AND p.status       = @status
                  AND p.published_on < @publishedOn
                ORDER BY p.published_on DESC
                LIMIT 1"""
        addWebLogId cmd webLogId
        [ cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published)
          cmd.Parameters.AddWithValue ("@publishedOn", publishedOn)
        ] |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! older = backgroundTask {
            if rdr.Read () then
                let! post = appendPostCategoryTagAndMeta (postWithoutText rdr)
                return Some post
            else
                return None
        }
        do! rdr.CloseAsync ()
        cmd.CommandText <-
            $"""{selectPost}
                WHERE p.web_log_id   = @webLogId
                  AND p.status       = @status
                  AND p.published_on > @publishedOn
                ORDER BY p.published_on
                LIMIT 1"""
        use! rdr = cmd.ExecuteReaderAsync ()
        let! newer = backgroundTask {
            if rdr.Read () then
                let! post = appendPostCategoryTagAndMeta (postWithoutText rdr)
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
        match! findFullById post.id post.webLogId with
        | Some oldPost ->
            use cmd = conn.CreateCommand ()
            cmd.CommandText <-
                """UPDATE post
                      SET author_id    = @authorId,
                          status       = @status,
                          title        = @title,
                          permalink    = @permalink,
                          published_on = @publishedOn,
                          updated_on   = @updatedOn,
                          template     = @template,
                          post_text    = @text
                    WHERE id         = @id
                      AND web_log_id = @webLogId"""
            addPostParameters cmd post
            do! write cmd
            do! updatePostCategories post.id oldPost.categoryIds     post.categoryIds
            do! updatePostTags       post.id oldPost.tags            post.tags
            do! updatePostEpisode    post
            do! updatePostMeta       post.id oldPost.metadata        post.metadata
            do! updatePostPermalinks post.id oldPost.priorPermalinks post.priorPermalinks
            do! updatePostRevisions  post.id oldPost.revisions       post.revisions
        | None -> return ()
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId permalinks = backgroundTask {
        match! findFullById postId webLogId with
        | Some post ->
            do! updatePostPermalinks postId post.priorPermalinks permalinks
            return true
        | None -> return false
    }
    
    interface IPostData with
        member _.add post = add post
        member _.countByStatus status webLogId = countByStatus status webLogId
        member _.delete postId webLogId = delete postId webLogId
        member _.findByPermalink permalink webLogId = findByPermalink permalink webLogId
        member _.findCurrentPermalink permalinks webLogId = findCurrentPermalink permalinks webLogId
        member _.findFullById postId webLogId = findFullById postId webLogId
        member _.findFullByWebLog webLogId = findFullByWebLog webLogId
        member _.findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage =
            findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage
        member _.findPageOfPosts webLogId pageNbr postsPerPage = findPageOfPosts webLogId pageNbr postsPerPage
        member _.findPageOfPublishedPosts webLogId pageNbr postsPerPage =
            findPageOfPublishedPosts webLogId pageNbr postsPerPage
        member _.findPageOfTaggedPosts webLogId tag pageNbr postsPerPage =
            findPageOfTaggedPosts webLogId tag pageNbr postsPerPage
        member _.findSurroundingPosts webLogId publishedOn = findSurroundingPosts webLogId publishedOn
        member _.restore posts = restore posts
        member _.update post = update post
        member _.updatePriorPermalinks postId webLogId permalinks = updatePriorPermalinks postId webLogId permalinks
