namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

// The web log podcast insert loop is not statically compilable; this is OK
#nowarn "3511"

/// SQLite myWebLog web log data implementation        
type SQLiteWebLogData (conn : SqliteConnection) =
    
    // SUPPORT FUNCTIONS
    
    /// Add parameters for web log INSERT or web log/RSS options UPDATE statements
    let addWebLogRssParameters (cmd : SqliteCommand) (webLog : WebLog) =
        [ cmd.Parameters.AddWithValue ("@feedEnabled", webLog.rss.feedEnabled)
          cmd.Parameters.AddWithValue ("@feedName", webLog.rss.feedName)
          cmd.Parameters.AddWithValue ("@itemsInFeed", maybe webLog.rss.itemsInFeed)
          cmd.Parameters.AddWithValue ("@categoryEnabled", webLog.rss.categoryEnabled)
          cmd.Parameters.AddWithValue ("@tagEnabled", webLog.rss.tagEnabled)
          cmd.Parameters.AddWithValue ("@copyright", maybe webLog.rss.copyright)
        ] |> ignore
    
    /// Add parameters for web log INSERT or UPDATE statements
    let addWebLogParameters (cmd : SqliteCommand) (webLog : WebLog) =
        [ cmd.Parameters.AddWithValue ("@id", WebLogId.toString webLog.id)
          cmd.Parameters.AddWithValue ("@name", webLog.name)
          cmd.Parameters.AddWithValue ("@subtitle", maybe webLog.subtitle)
          cmd.Parameters.AddWithValue ("@defaultPage", webLog.defaultPage)
          cmd.Parameters.AddWithValue ("@postsPerPage", webLog.postsPerPage)
          cmd.Parameters.AddWithValue ("@themeId", webLog.themePath)
          cmd.Parameters.AddWithValue ("@urlBase", webLog.urlBase)
          cmd.Parameters.AddWithValue ("@timeZone", webLog.timeZone)
          cmd.Parameters.AddWithValue ("@autoHtmx", webLog.autoHtmx)
        ] |> ignore
        addWebLogRssParameters cmd webLog
    
    /// Add parameters for custom feed INSERT or UPDATE statements
    let addCustomFeedParameters (cmd : SqliteCommand) webLogId (feed : CustomFeed) =
        [ cmd.Parameters.AddWithValue ("@id", CustomFeedId.toString feed.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId)
          cmd.Parameters.AddWithValue ("@source", CustomFeedSource.toString feed.source)
          cmd.Parameters.AddWithValue ("@path", Permalink.toString feed.path)
        ] |> ignore
    
    /// Add parameters for podcast INSERT or UPDATE statements
    let addPodcastParameters (cmd : SqliteCommand) feedId (podcast : PodcastOptions) =
        [ cmd.Parameters.AddWithValue ("@feedId", CustomFeedId.toString feedId)
          cmd.Parameters.AddWithValue ("@title", podcast.title)
          cmd.Parameters.AddWithValue ("@subtitle", maybe podcast.subtitle)
          cmd.Parameters.AddWithValue ("@itemsInFeed", podcast.itemsInFeed)
          cmd.Parameters.AddWithValue ("@summary", podcast.summary)
          cmd.Parameters.AddWithValue ("@displayedAuthor", podcast.displayedAuthor)
          cmd.Parameters.AddWithValue ("@email", podcast.email)
          cmd.Parameters.AddWithValue ("@imageUrl", Permalink.toString podcast.imageUrl)
          cmd.Parameters.AddWithValue ("@iTunesCategory", podcast.iTunesCategory)
          cmd.Parameters.AddWithValue ("@iTunesSubcategory", maybe podcast.iTunesSubcategory)
          cmd.Parameters.AddWithValue ("@explicit", ExplicitRating.toString podcast.explicit)
          cmd.Parameters.AddWithValue ("@defaultMediaType", maybe podcast.defaultMediaType)
          cmd.Parameters.AddWithValue ("@mediaBaseUrl", maybe podcast.mediaBaseUrl)
          cmd.Parameters.AddWithValue ("@guid", maybe podcast.guid)
          cmd.Parameters.AddWithValue ("@fundingUrl", maybe podcast.fundingUrl)
          cmd.Parameters.AddWithValue ("@fundingText", maybe podcast.fundingText)
          cmd.Parameters.AddWithValue ("@medium", maybe (podcast.medium |> Option.map PodcastMedium.toString))
        ] |> ignore

    /// Get the current custom feeds for a web log
    let getCustomFeeds (webLog : WebLog) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """SELECT f.*, p.*
                 FROM web_log_feed f
                      LEFT JOIN web_log_feed_podcast p ON p.feed_id = f.id
                WHERE f.web_log_id = @webLogId"""
        addWebLogId cmd webLog.id
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toCustomFeed rdr
    }
    
    /// Append custom feeds to a web log
    let appendCustomFeeds (webLog : WebLog) = backgroundTask {
        let! feeds = getCustomFeeds webLog
        return { webLog with rss = { webLog.rss with customFeeds = feeds } }
    }
    
    /// Add a podcast to a custom feed
    let addPodcast feedId (podcast : PodcastOptions) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """INSERT INTO web_log_feed_podcast (
                   feed_id, title, subtitle, items_in_feed, summary, displayed_author, email,
                   image_url, itunes_category, itunes_subcategory, explicit, default_media_type,
                   media_base_url, guid, funding_url, funding_text, medium
               ) VALUES (
                   @feedId, @title, @subtitle, @itemsInFeed, @summary, @displayedAuthor, @email,
                   @imageUrl, @iTunesCategory, @iTunesSubcategory, @explicit, @defaultMediaType,
                   @mediaBaseUrl, @guid, @fundingUrl, @fundingText, @medium
               )"""
        addPodcastParameters cmd feedId podcast
        do! write cmd
    }
    
    /// Update the custom feeds for a web log
    let updateCustomFeeds (webLog : WebLog) = backgroundTask {
        let! feeds = getCustomFeeds webLog
        let toDelete, toAdd = diffLists feeds webLog.rss.customFeeds (fun it -> $"{CustomFeedId.toString it.id}")
        let toId (feed : CustomFeed) = feed.id
        let toUpdate =
            webLog.rss.customFeeds
            |> List.filter (fun f ->
                not (toDelete |> List.map toId |> List.append (toAdd |> List.map toId) |> List.contains f.id))
        use cmd = conn.CreateCommand ()
        cmd.Parameters.Add ("@id", SqliteType.Text) |> ignore
        toDelete
        |> List.map (fun it -> backgroundTask {
            cmd.CommandText <-
                """DELETE FROM web_log_feed_podcast WHERE feed_id = @id;
                   DELETE FROM web_log_feed         WHERE id      = @id"""
            cmd.Parameters["@id"].Value <- CustomFeedId.toString it.id
            do! write cmd
        })
        |> Task.WhenAll
        |> ignore
        cmd.Parameters.Clear ()
        toAdd
        |> List.map (fun it -> backgroundTask {
            cmd.CommandText <-
                """INSERT INTO web_log_feed (
                       id, web_log_id, source, path
                   ) VALUES (
                       @id, @webLogId, @source, @path
                   )"""
            cmd.Parameters.Clear ()
            addCustomFeedParameters cmd webLog.id it
            do! write cmd
            match it.podcast with
            | Some podcast -> do! addPodcast it.id podcast
            | None -> ()
        })
        |> Task.WhenAll
        |> ignore
        toUpdate
        |> List.map (fun it -> backgroundTask {
            cmd.CommandText <-
                """UPDATE web_log_feed
                      SET source = @source,
                          path   = @path
                    WHERE id         = @id
                      AND web_log_id = @webLogId"""
            cmd.Parameters.Clear ()
            addCustomFeedParameters cmd webLog.id it
            do! write cmd
            let hadPodcast = Option.isSome (feeds |> List.find (fun f -> f.id = it.id)).podcast
            match it.podcast with
            | Some podcast ->
                if hadPodcast then
                    cmd.CommandText <-
                        """UPDATE web_log_feed_podcast
                              SET title              = @title,
                                  subtitle           = @subtitle,
                                  items_in_feed      = @itemsInFeed,
                                  summary            = @summary,
                                  displayed_author   = @displayedAuthor,
                                  email              = @email,
                                  image_url          = @imageUrl,
                                  itunes_category    = @iTunesCategory,
                                  itunes_subcategory = @iTunesSubcategory,
                                  explicit           = @explicit,
                                  default_media_type = @defaultMediaType,
                                  media_base_url     = @mediaBaseUrl,
                                  guid               = @guid,
                                  funding_url        = @fundingUrl,
                                  funding_text       = @fundingText,
                                  medium             = @medium
                            WHERE feed_id = @feedId"""
                    cmd.Parameters.Clear ()
                    addPodcastParameters cmd it.id podcast
                    do! write cmd
                else
                    do! addPodcast it.id podcast
            | None ->
                if hadPodcast then
                    cmd.CommandText <- "DELETE FROM web_log_feed_podcast WHERE feed_id = @id"
                    cmd.Parameters.Clear ()
                    cmd.Parameters.AddWithValue ("@id", CustomFeedId.toString it.id) |> ignore
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
        cmd.CommandText <-
            """INSERT INTO web_log (
                   id, name, subtitle, default_page, posts_per_page, theme_id, url_base, time_zone,
                   auto_htmx, feed_enabled, feed_name, items_in_feed, category_enabled, tag_enabled,
                   copyright
               ) VALUES (
                   @id, @name, @subtitle, @defaultPage, @postsPerPage, @themeId, @urlBase, @timeZone,
                   @autoHtmx, @feedEnabled, @feedName, @itemsInFeed, @categoryEnabled, @tagEnabled,
                   @copyright
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
        use cmd = conn.CreateCommand ()
        addWebLogId cmd webLogId
        let subQuery table = $"(SELECT id FROM {table} WHERE web_log_id = @webLogId)"
        let postSubQuery = subQuery "post"
        let pageSubQuery = subQuery "page"
        cmd.CommandText <-
            $"""DELETE FROM post_comment  WHERE post_id IN {postSubQuery};
                DELETE FROM post_revision WHERE post_id IN {postSubQuery};
                DELETE FROM post_episode  WHERE post_id IN {postSubQuery};
                DELETE FROM post_tag      WHERE post_id IN {postSubQuery};
                DELETE FROM post_category WHERE post_id IN {postSubQuery};
                DELETE FROM post_meta     WHERE post_id IN {postSubQuery};
                DELETE FROM post          WHERE web_log_id = @webLogId;
                DELETE FROM page_revision WHERE page_id IN {pageSubQuery};
                DELETE FROM page_meta     WHERE page_id IN {pageSubQuery};
                DELETE FROM page          WHERE web_log_id = @webLogId;
                DELETE FROM category      WHERE web_log_id = @webLogId;
                DELETE FROM tag_map       WHERE web_log_id = @webLogId;
                DELETE FROM web_log_user  WHERE web_log_id = @webLogId;
                DELETE FROM web_log_feed_podcast WHERE feed_id IN {subQuery "web_log_feed"};
                DELETE FROM web_log_feed  WHERE web_log_id = @webLogId;
                DELETE FROM web_log       WHERE id = @webLogId"""
        do! write cmd
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
        cmd.CommandText <-
            """UPDATE web_log
                  SET name             = @name,
                      subtitle         = @subtitle,
                      default_page     = @defaultPage,
                      posts_per_page   = @postsPerPage,
                      theme_id         = @themeId,
                      url_base         = @urlBase,
                      time_zone        = @timeZone,
                      auto_htmx        = @autoHtmx,
                      feed_enabled     = @feedEnabled,
                      feed_name        = @feedName,
                      items_in_feed    = @itemsInFeed,
                      category_enabled = @categoryEnabled,
                      tag_enabled      = @tagEnabled,
                      copyright        = @copyright
                WHERE id = @id"""
        addWebLogParameters cmd webLog
        do! write cmd
    }
    
    /// Update RSS options for a web log
    let updateRssOptions webLog = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """UPDATE web_log
                  SET feed_enabled     = @feedEnabled,
                      feed_name        = @feedName,
                      items_in_feed    = @itemsInFeed,
                      category_enabled = @categoryEnabled,
                      tag_enabled      = @tagEnabled,
                      copyright        = @copyright
                WHERE id = @id"""
        addWebLogRssParameters cmd webLog
        do! write cmd
        do! updateCustomFeeds webLog
    }
    
    interface IWebLogData with
        member _.add webLog = add webLog
        member _.all () = all ()
        member _.delete webLogId = delete webLogId
        member _.findByHost url = findByHost url
        member _.findById webLogId = findById webLogId
        member _.updateSettings webLog = updateSettings webLog
        member _.updateRssOptions webLog = updateRssOptions webLog
