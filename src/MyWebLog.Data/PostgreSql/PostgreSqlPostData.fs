namespace MyWebLog.Data.PostgreSql

open System
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog post data implementation        
type PostgreSqlPostData (conn : NpgsqlConnection) =

    // SUPPORT FUNCTIONS
    
    /// Append revisions to a post
    let appendPostRevisions (post : Post) = backgroundTask {
        let! revisions =
            Sql.existingConnection conn
            |> Sql.query "SELECT as_of, revision_text FROM post_revision WHERE post_id = @id ORDER BY as_of DESC"
            |> Sql.parameters [ "@id", Sql.string (PostId.toString post.Id) ]
            |> Sql.executeAsync Map.toRevision
        return { post with Revisions = revisions }
    }
    
    /// The SELECT statement for a post that will include category IDs
    let selectPost =
        """SELECT *, ARRAY(SELECT cat.category_id FROM post_category cat WHERE cat.post_id = p.id) AS category_ids
             FROM post"""
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText row =
        { Map.toPost row with Text = "" }
    
    /// Update a post's assigned categories
    let updatePostCategories postId oldCats newCats = backgroundTask {
        let toDelete, toAdd = Utils.diffLists oldCats newCats CategoryId.toString
        if not (List.isEmpty toDelete) || not (List.isEmpty toAdd) then
            let catParams cats =
                cats
                |> List.map (fun it -> [
                    "@postId",    Sql.string (PostId.toString postId)
                    "categoryId", Sql.string (CategoryId.toString it)
                ])
            let! _ =
                Sql.existingConnection conn
                |> Sql.executeTransactionAsync [
                    if not (List.isEmpty toDelete) then
                        "DELETE FROM post_category WHERE post_id = @postId AND category_id = @categoryId",
                        catParams toDelete
                    if not (List.isEmpty toAdd) then
                        "INSERT INTO post_category VALUES (@postId, @categoryId)", catParams toAdd
                ]
            ()
    }
    
    /// Update a post's revisions
    let updatePostRevisions postId oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = Utils.diffRevisions oldRevs newRevs
        if not (List.isEmpty toDelete) || not (List.isEmpty toAdd) then
            let! _ =
                Sql.existingConnection conn
                |> Sql.executeTransactionAsync [
                    if not (List.isEmpty toDelete) then
                        "DELETE FROM post_revision WHERE post_id = @postId AND as_of = @asOf",
                        toDelete
                        |> List.map (fun it -> [
                            "@postId", Sql.string      (PostId.toString postId)
                            "@asOf",   Sql.timestamptz it.AsOf
                        ])
                    if not (List.isEmpty toAdd) then
                        "INSERT INTO post_revision VALUES (@postId, @asOf, @text)",
                        toAdd
                        |> List.map (fun it -> [
                            "@postId", Sql.string      (PostId.toString postId)
                            "@asOf",   Sql.timestamptz it.AsOf
                            "@text",   Sql.string      (MarkupText.toString it.Text)
                        ])
                ]
            ()
    }
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Count posts in a status for the given web log
    let countByStatus status webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT COUNT(id) AS the_count FROM post WHERE web_log_id = @webLogId AND status = @status"
        |> Sql.parameters [ webLogIdParam webLogId; "@status", Sql.string (PostStatus.toString status) ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Find a post by its ID for the given web log (excluding revisions)
    let findById postId webLogId = backgroundTask {
        let! post =
            Sql.existingConnection conn
            |> Sql.query $"{selectPost} WHERE id = @id AND web_log_id = @webLogId"
            |> Sql.parameters [ "@id", Sql.string (PostId.toString postId); webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toPost
        return List.tryHead post
    }
    
    /// Find a post by its permalink for the given web log (excluding revisions and prior permalinks)
    let findByPermalink permalink webLogId = backgroundTask {
        let! post =
            Sql.existingConnection conn
            |> Sql.query $"{selectPost} WHERE web_log_id = @webLogId AND permalink = @link"
            |> Sql.parameters [ webLogIdParam webLogId; "@link", Sql.string (Permalink.toString permalink) ]
            |> Sql.executeAsync Map.toPost
        return List.tryHead post
    }
    
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
        match! findById postId webLogId with
        | Some _ ->
            let! _ =
                Sql.existingConnection conn
                |> Sql.query """
                    DELETE FROM post_revision WHERE post_id = @id;
                    DELETE FROM post_category WHERE post_id = @id;
                    DELETE FROM post          WHERE id      = @id"""
                |> Sql.parameters [ "@id", Sql.string (PostId.toString postId) ]
                |> Sql.executeNonQueryAsync
            return true
        | None -> return false
    }
    
    /// Find the current permalink from a list of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        if List.isEmpty permalinks then return None
        else
            let linkSql, linkParams = priorPermalinkSql permalinks
            let! links =
                Sql.existingConnection conn
                |> Sql.query $"SELECT permalink FROM post WHERE web_log_id = @webLogId AND ({linkSql}"
                |> Sql.parameters (webLogIdParam webLogId :: linkParams)
                |> Sql.executeAsync Map.toPermalink
            return List.tryHead links
    }
    
    /// Get all complete posts for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        let! posts =
            Sql.existingConnection conn
            |> Sql.query $"{selectPost} WHERE web_log_id = @webLogId"
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toPost
        let! revisions =
            Sql.existingConnection conn
            |> Sql.query """
                SELECT *
                  FROM post_revision pr
                       INNER JOIN post p ON p.id = pr.post_id
                 WHERE p.web_log_id = @webLogId
                 ORDER BY as_of DESC"""
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeAsync (fun row -> PostId (row.string "post_id"), Map.toRevision row)
        return
            posts
            |> List.map (fun it ->
                { it with Revisions = revisions |> List.filter (fun r -> fst r = it.Id) |> List.map snd })
    }
    
    /// Get a page of categorized posts for the given web log (excludes revisions)
    let findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage =
        let catSql, catParams = inClause "catId" CategoryId.toString categoryIds
        Sql.existingConnection conn
        |> Sql.query $"""
            {selectPost} p
                   INNER JOIN post_category pc ON pc.post_id = p.id
             WHERE p.web_log_id = @webLogId
               AND p.status     = @status
               AND pc.category_id IN ({catSql})
             ORDER BY published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        |> Sql.parameters
            [   webLogIdParam webLogId
                "@status", Sql.string (PostStatus.toString Published)
                yield! catParams   ]
        |> Sql.executeAsync Map.toPost
    
    /// Get a page of posts for the given web log (excludes text and revisions)
    let findPageOfPosts webLogId pageNbr postsPerPage =
        Sql.existingConnection conn
        |> Sql.query $"""
            {selectPost}
             WHERE web_log_id = @webLogId
             ORDER BY published_on DESC NULLS FIRST, updated_on
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync postWithoutText
    
    /// Get a page of published posts for the given web log (excludes revisions)
    let findPageOfPublishedPosts webLogId pageNbr postsPerPage =
        Sql.existingConnection conn
        |> Sql.query $"""
            {selectPost}
             WHERE web_log_id = @webLogId
               AND status     = @status
             ORDER BY published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        |> Sql.parameters [ webLogIdParam webLogId; "@status", Sql.string (PostStatus.toString Published) ]
        |> Sql.executeAsync Map.toPost
    
    /// Get a page of tagged posts for the given web log (excludes revisions and prior permalinks)
    let findPageOfTaggedPosts webLogId (tag : string) pageNbr postsPerPage =
        Sql.existingConnection conn
        |> Sql.query $"""
            {selectPost}
             WHERE web_log_id =  @webLogId
               AND status     =  @status
               AND tag        && ARRAY[@tag]
             ORDER BY published_on DESC
             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
        |> Sql.parameters
            [   webLogIdParam webLogId
                "@status", Sql.string (PostStatus.toString Published)
                "@tag",    Sql.string tag
            ]
        |> Sql.executeAsync Map.toPost
    
    /// Find the next newest and oldest post from a publish date for the given web log
    let findSurroundingPosts webLogId (publishedOn : DateTime) = backgroundTask {
        let queryParams = Sql.parameters [
            webLogIdParam webLogId
            "@status",      Sql.string (PostStatus.toString Published)
            "@publishedOn", Sql.timestamptz publishedOn
        ]
        let! older =
            Sql.existingConnection conn
            |> Sql.query $"""
                {selectPost}
                 WHERE web_log_id   = @webLogId
                   AND status       = @status
                   AND published_on < @publishedOn
                 ORDER BY published_on DESC
                 LIMIT 1"""
            |> queryParams
            |> Sql.executeAsync Map.toPost
        let! newer =
            Sql.existingConnection conn
            |> Sql.query $"""
                {selectPost}
                 WHERE web_log_id   = @webLogId
                   AND status       = @status
                   AND published_on > @publishedOn
                 ORDER BY published_on
                 LIMIT 1"""
            |> queryParams
            |> Sql.executeAsync Map.toPost
        return List.tryHead older, List.tryHead newer
    }
    
    /// Save a post
    let save (post : Post) = backgroundTask {
        let! oldPost = findFullById post.Id post.WebLogId
        let! _ =
            Sql.existingConnection conn
            |> Sql.query """
                INSERT INTO post (
                    id, web_log_id, author_id, status, title, permalink, prior_permalinks, published_on, updated_on,
                    template, post_text, tags, meta_items, episode
                ) VALUES (
                    @id, @webLogId, @authorId, @status, @title, @permalink, @priorPermalinks, @publishedOn, @updatedOn,
                    @template, @text, @tags, @metaItems, @episode
                ) ON CONFLICT (id) DO UPDATE
                SET author_id        = EXCLUDED.author_id,
                    status           = EXCLUDED.status,
                    title            = EXCLUDED.title,
                    permalink        = EXCLUDED.permalink,
                    prior_permalinks = EXCLUDED.prior_permalinks,
                    published_on     = EXCLUDED.published_on,
                    updated_on       = EXCLUDED.updated_on,
                    template         = EXCLUDED.template,
                    post_text        = EXCLUDED.text,
                    tags             = EXCLUDED.tags,
                    meta_items       = EXCLUDED.meta_items,
                    episode          = EXCLUDED.episode"""
            |> Sql.parameters
                [   webLogIdParam post.WebLogId
                    "@id",          Sql.string            (PostId.toString post.Id)
                    "@authorId",    Sql.string            (WebLogUserId.toString post.AuthorId)
                    "@status",      Sql.string            (PostStatus.toString post.Status)
                    "@title",       Sql.string            post.Title
                    "@permalink",   Sql.string            (Permalink.toString post.Permalink)
                    "@publishedOn", Sql.timestamptzOrNone post.PublishedOn
                    "@updatedOn",   Sql.timestamptz       post.UpdatedOn
                    "@template",    Sql.stringOrNone      post.Template
                    "@text",        Sql.string            post.Text
                    "@episode",     Sql.jsonbOrNone       (post.Episode |> Option.map JsonConvert.SerializeObject)
                    "@priorPermalinks",
                        Sql.stringArray (post.PriorPermalinks |> List.map Permalink.toString |> Array.ofList)
                    "@tags",
                        Sql.stringArrayOrNone (if List.isEmpty post.Tags then None else Some (Array.ofList post.Tags))
                    "@metaItems",
                        if List.isEmpty post.Metadata then None else Some (JsonConvert.SerializeObject post.Metadata)
                        |> Sql.jsonbOrNone
                ]
            |> Sql.executeNonQueryAsync
        do! updatePostCategories post.Id (match oldPost with Some p -> p.CategoryIds | None -> []) post.CategoryIds
        do! updatePostRevisions  post.Id (match oldPost with Some p -> p.Revisions   | None -> []) post.Revisions
    }
    
    /// Restore posts from a backup
    let restore posts = backgroundTask {
        for post in posts do
            do! save post
    }
    
    /// Update prior permalinks for a post
    let updatePriorPermalinks postId webLogId permalinks = backgroundTask {
        match! findById postId webLogId with
        | Some _ ->
            let! _ =
                Sql.existingConnection conn
                |> Sql.query "UPDATE post SET prior_permalinks = @prior WHERE id = @id"
                |> Sql.parameters
                    [   "@id",    Sql.string      (PostId.toString postId)
                        "@prior", Sql.stringArray (permalinks |> List.map Permalink.toString |> Array.ofList) ]
                |> Sql.executeNonQueryAsync
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
