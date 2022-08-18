namespace MyWebLog.Data.PostgreSql

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

// The web log podcast insert loop is not statically compilable; this is OK
//#nowarn "3511"

/// PostgreSQL myWebLog web log data implementation        
type PostgreSqlWebLogData (conn : NpgsqlConnection) =
    
    // SUPPORT FUNCTIONS
    
    /// Add parameters for web log INSERT or web log/RSS options UPDATE statements
    let addWebLogRssParameters (webLog : WebLog) =
        [   cmd.Parameters.AddWithValue ("@isFeedEnabled", webLog.Rss.IsFeedEnabled)
            cmd.Parameters.AddWithValue ("@feedName", webLog.Rss.FeedName)
            cmd.Parameters.AddWithValue ("@itemsInFeed", maybe webLog.Rss.ItemsInFeed)
            cmd.Parameters.AddWithValue ("@isCategoryEnabled", webLog.Rss.IsCategoryEnabled)
            cmd.Parameters.AddWithValue ("@isTagEnabled", webLog.Rss.IsTagEnabled)
            cmd.Parameters.AddWithValue ("@copyright", maybe webLog.Rss.Copyright)
        ] |> ignore
    
    /// Add parameters for web log INSERT or UPDATE statements
    let addWebLogParameters (webLog : WebLog) =
        [   cmd.Parameters.AddWithValue ("@id", WebLogId.toString webLog.Id)
            cmd.Parameters.AddWithValue ("@name", webLog.Name)
            cmd.Parameters.AddWithValue ("@slug", webLog.Slug)
            cmd.Parameters.AddWithValue ("@subtitle", maybe webLog.Subtitle)
            cmd.Parameters.AddWithValue ("@defaultPage", webLog.DefaultPage)
            cmd.Parameters.AddWithValue ("@postsPerPage", webLog.PostsPerPage)
            cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString webLog.ThemeId)
            cmd.Parameters.AddWithValue ("@urlBase", webLog.UrlBase)
            cmd.Parameters.AddWithValue ("@timeZone", webLog.TimeZone)
            cmd.Parameters.AddWithValue ("@autoHtmx", webLog.AutoHtmx)
            cmd.Parameters.AddWithValue ("@uploads", UploadDestination.toString webLog.Uploads)
        ] |> ignore
        addWebLogRssParameters cmd webLog
    
    /// Add parameters for custom feed INSERT or UPDATE statements
    let addCustomFeedParameters (cmd : SqliteCommand) webLogId (feed : CustomFeed) =
        [   cmd.Parameters.AddWithValue ("@id", CustomFeedId.toString feed.Id)
            cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId)
            cmd.Parameters.AddWithValue ("@source", CustomFeedSource.toString feed.Source)
            cmd.Parameters.AddWithValue ("@path", Permalink.toString feed.Path)
        ] |> ignore
    
    /// Get the current custom feeds for a web log
    let getCustomFeeds (webLog : WebLog) =
        Sql.existingConnection conn
        |> Sql.query """
            SELECT f.*, p.*
              FROM web_log_feed f
                   LEFT JOIN web_log_feed_podcast p ON p.feed_id = f.id
             WHERE f.web_log_id = @webLogId"""
        |> Sql.parameters [ webLogIdParam webLog.Id ]
        |> Sql.executeAsync Map.toCustomFeed
    
    /// Append custom feeds to a web log
    let appendCustomFeeds (webLog : WebLog) = backgroundTask {
        let! feeds = getCustomFeeds webLog
        return { webLog with Rss = { webLog.Rss with CustomFeeds = feeds } }
    }
    
    /// The INSERT statement for a podcast feed
    let feedInsert = """
        INSERT INTO web_log_feed_podcast (
            feed_id, title, subtitle, items_in_feed, summary, displayed_author, email, image_url, apple_category,
            apple_subcategory, explicit, default_media_type, media_base_url, podcast_guid, funding_url, funding_text,
            medium
        ) VALUES (
            @feedId, @title, @subtitle, @itemsInFeed, @summary, @displayedAuthor, @email, @imageUrl, @appleCategory,
            @appleSubcategory, @explicit, @defaultMediaType, @mediaBaseUrl, @podcastGuid, @fundingUrl, @fundingText,
            @medium
        )"""
    
    /// The parameters to save a podcast feed
    let feedParams feedId (podcast : PodcastOptions) = [
        "@feedId",           Sql.string       (CustomFeedId.toString feedId)
        "@title",            Sql.string       podcast.Title
        "@subtitle",         Sql.stringOrNone podcast.Subtitle
        "@itemsInFeed",      Sql.int          podcast.ItemsInFeed
        "@summary",          Sql.string       podcast.Summary
        "@displayedAuthor",  Sql.string       podcast.DisplayedAuthor
        "@email",            Sql.string       podcast.Email
        "@imageUrl",         Sql.string       (Permalink.toString podcast.ImageUrl)
        "@appleCategory",    Sql.string       podcast.AppleCategory
        "@appleSubcategory", Sql.stringOrNone podcast.AppleSubcategory
        "@explicit",         Sql.string       (ExplicitRating.toString podcast.Explicit)
        "@defaultMediaType", Sql.stringOrNone podcast.DefaultMediaType
        "@mediaBaseUrl",     Sql.stringOrNone podcast.MediaBaseUrl
        "@podcastGuid",      Sql.uuidOrNone   podcast.PodcastGuid
        "@fundingUrl",       Sql.stringOrNone podcast.FundingUrl
        "@fundingText",      Sql.stringOrNone podcast.FundingText
        "@medium",           Sql.stringOrNone (podcast.Medium |> Option.map PodcastMedium.toString)
    ]
    
    /// Save a podcast for a custom feed
    let savePodcast feedId (podcast : PodcastOptions) = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"""
                {feedInsert} ON CONFLICT (feed_id) DO UPDATE
                SET title              = EXCLUDED.title,
                    subtitle           = EXCLUDED.subtitle,
                    items_in_feed      = EXCLUDED.items_in_feed,
                    summary            = EXCLUDED.summary,
                    displayed_author   = EXCLUDED.displayed_author,
                    email              = EXCLUDED.email,
                    image_url          = EXCLUDED.image_url,
                    apple_category     = EXCLUDED.apple_category,
                    apple_subcategory  = EXCLUDED.apple_subcategory,
                    explicit           = EXCLUDED.explicit,
                    default_media_type = EXCLUDED.default_media_type,
                    media_base_url     = EXCLUDED.media_base_url,
                    podcast_guid       = EXCLUDED.podcast_guid,
                    funding_url        = EXCLUDED.funding_url,
                    funding_text       = EXCLUDED.funding_text,
                    medium             = EXCLUDED.medium"""
            |> Sql.parameters (feedParams feedId podcast)
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Update the custom feeds for a web log
    let updateCustomFeeds (webLog : WebLog) = backgroundTask {
        let! feeds = getCustomFeeds webLog
        let toDelete, toAdd = Utils.diffLists feeds webLog.Rss.CustomFeeds (fun it -> $"{CustomFeedId.toString it.Id}")
        let toId (feed : CustomFeed) = feed.Id
        let toUpdate =
            webLog.Rss.CustomFeeds
            |> List.filter (fun f ->
                not (toDelete |> List.map toId |> List.append (toAdd |> List.map toId) |> List.contains f.Id))
        use cmd = conn.CreateCommand ()
        cmd.Parameters.Add ("@id", SqliteType.Text) |> ignore
        toDelete
        |> List.map (fun it -> backgroundTask {
            cmd.CommandText <- """
                DELETE FROM web_log_feed_podcast WHERE feed_id = @id;
                DELETE FROM web_log_feed         WHERE id      = @id"""
            cmd.Parameters["@id"].Value <- CustomFeedId.toString it.Id
            do! write cmd
        })
        |> Task.WhenAll
        |> ignore
        cmd.Parameters.Clear ()
        toAdd
        |> List.map (fun it -> backgroundTask {
            cmd.CommandText <- """
                INSERT INTO web_log_feed (
                    id, web_log_id, source, path
                ) VALUES (
                    @id, @webLogId, @source, @path
                )"""
            cmd.Parameters.Clear ()
            addCustomFeedParameters cmd webLog.Id it
            do! write cmd
            match it.Podcast with
            | Some podcast -> do! addPodcast it.Id podcast
            | None -> ()
        })
        |> Task.WhenAll
        |> ignore
        toUpdate
        |> List.map (fun it -> backgroundTask {
            cmd.CommandText <- """
                UPDATE web_log_feed
                   SET source = @source,
                       path   = @path
                 WHERE id         = @id
                   AND web_log_id = @webLogId"""
            cmd.Parameters.Clear ()
            addCustomFeedParameters cmd webLog.Id it
            do! write cmd
            let hadPodcast = Option.isSome (feeds |> List.find (fun f -> f.Id = it.Id)).Podcast
            match it.Podcast with
            | Some podcast -> do! savePodcast it.Id podcast
            | None ->
                if hadPodcast then
                    cmd.CommandText <- "DELETE FROM web_log_feed_podcast WHERE feed_id = @id"
                    cmd.Parameters.Clear ()
                    cmd.Parameters.AddWithValue ("@id", CustomFeedId.toString it.Id) |> ignore
                    do! write cmd
                else
                    ()
        })
        |> Task.WhenAll
        |> ignore
    }
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a web log
    let add webLog = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            INSERT INTO web_log (
                id, name, slug, subtitle, default_page, posts_per_page, theme_id, url_base, time_zone, auto_htmx,
                uploads, is_feed_enabled, feed_name, items_in_feed, is_category_enabled, is_tag_enabled, copyright
            ) VALUES (
                @id, @name, @slug, @subtitle, @defaultPage, @postsPerPage, @themeId, @urlBase, @timeZone, @autoHtmx,
                @uploads, @isFeedEnabled, @feedName, @itemsInFeed, @isCategoryEnabled, @isTagEnabled, @copyright
            )"""
        addWebLogParameters cmd webLog
        do! write cmd
        do! updateCustomFeeds webLog
    }
    
    /// Retrieve all web logs
    let all () = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log"
        use! rdr = cmd.ExecuteReaderAsync ()
        let! webLogs =
            toList Map.toWebLog rdr
            |> List.map (fun webLog -> backgroundTask { return! appendCustomFeeds webLog })
            |> Task.WhenAll
        return List.ofArray webLogs
    }
    
    /// Delete a web log by its ID
    let delete webLogId = backgroundTask {
        let subQuery table = $"(SELECT id FROM {table} WHERE web_log_id = @webLogId)"
        let postSubQuery = subQuery "post"
        let pageSubQuery = subQuery "page"
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"""
                DELETE FROM post_comment         WHERE post_id IN {postSubQuery};
                DELETE FROM post_revision        WHERE post_id IN {postSubQuery};
                DELETE FROM post_category        WHERE post_id IN {postSubQuery};
                DELETE FROM post                 WHERE web_log_id = @webLogId;
                DELETE FROM page_revision        WHERE page_id IN {pageSubQuery};
                DELETE FROM page                 WHERE web_log_id = @webLogId;
                DELETE FROM category             WHERE web_log_id = @webLogId;
                DELETE FROM tag_map              WHERE web_log_id = @webLogId;
                DELETE FROM upload               WHERE web_log_id = @webLogId;
                DELETE FROM web_log_user         WHERE web_log_id = @webLogId;
                DELETE FROM web_log_feed_podcast WHERE feed_id IN {subQuery "web_log_feed"};
                DELETE FROM web_log_feed         WHERE web_log_id = @webLogId;
                DELETE FROM web_log              WHERE id         = @webLogId"""
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Find a web log by its host (URL base)
    let findByHost (url : string) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log WHERE url_base = @urlBase"
        cmd.Parameters.AddWithValue ("@urlBase", url) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        if rdr.Read () then
            let! webLog = appendCustomFeeds (Map.toWebLog rdr)
            return Some webLog
        else
            return None
    }
    
    /// Find a web log by its ID
    let findById webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM web_log WHERE id = @webLogId"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        if rdr.Read () then
            let! webLog = appendCustomFeeds (Map.toWebLog rdr)
            return Some webLog
        else
            return None
    }
    
    /// Update settings for a web log
    let updateSettings webLog = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            UPDATE web_log
               SET name                = @name,
                   slug                = @slug,
                   subtitle            = @subtitle,
                   default_page        = @defaultPage,
                   posts_per_page      = @postsPerPage,
                   theme_id            = @themeId,
                   url_base            = @urlBase,
                   time_zone           = @timeZone,
                   auto_htmx           = @autoHtmx,
                   uploads             = @uploads,
                   is_feed_enabled     = @isFeedEnabled,
                   feed_name           = @feedName,
                   items_in_feed       = @itemsInFeed,
                   is_category_enabled = @isCategoryEnabled,
                   is_tag_enabled      = @isTagEnabled,
                   copyright           = @copyright
             WHERE id = @id"""
        addWebLogParameters cmd webLog
        do! write cmd
    }
    
    /// Update RSS options for a web log
    let updateRssOptions webLog = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            UPDATE web_log
               SET is_feed_enabled     = @isFeedEnabled,
                   feed_name           = @feedName,
                   items_in_feed       = @itemsInFeed,
                   is_category_enabled = @isCategoryEnabled,
                   is_tag_enabled      = @isTagEnabled,
                   copyright           = @copyright
             WHERE id = @id"""
        addWebLogRssParameters cmd webLog
        cmd.Parameters.AddWithValue ("@id", WebLogId.toString webLog.Id) |> ignore
        do! write cmd
        do! updateCustomFeeds webLog
    }
    
    interface IWebLogData with
        member _.Add webLog = add webLog
        member _.All () = all ()
        member _.Delete webLogId = delete webLogId
        member _.FindByHost url = findByHost url
        member _.FindById webLogId = findById webLogId
        member _.UpdateSettings webLog = updateSettings webLog
        member _.UpdateRssOptions webLog = updateRssOptions webLog
