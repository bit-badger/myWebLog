namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog web log data implementation        
type PostgresWebLogData (conn : NpgsqlConnection) =
    
    // SUPPORT FUNCTIONS
    
    /// The parameters for web log INSERT or web log/RSS options UPDATE statements
    let rssParams (webLog : WebLog) = [
        "@isFeedEnabled",     Sql.bool         webLog.Rss.IsFeedEnabled
        "@feedName",          Sql.string       webLog.Rss.FeedName
        "@itemsInFeed",       Sql.intOrNone    webLog.Rss.ItemsInFeed
        "@isCategoryEnabled", Sql.bool         webLog.Rss.IsCategoryEnabled
        "@isTagEnabled",      Sql.bool         webLog.Rss.IsTagEnabled
        "@copyright",         Sql.stringOrNone webLog.Rss.Copyright
    ]
    
    /// The parameters for web log INSERT or UPDATE statements
    let webLogParams (webLog : WebLog) = [
        "@id",           Sql.string       (WebLogId.toString webLog.Id)
        "@name",         Sql.string       webLog.Name
        "@slug",         Sql.string       webLog.Slug
        "@subtitle",     Sql.stringOrNone webLog.Subtitle
        "@defaultPage",  Sql.string       webLog.DefaultPage
        "@postsPerPage", Sql.int          webLog.PostsPerPage
        "@themeId",      Sql.string       (ThemeId.toString webLog.ThemeId)
        "@urlBase",      Sql.string       webLog.UrlBase
        "@timeZone",     Sql.string       webLog.TimeZone
        "@autoHtmx",     Sql.bool         webLog.AutoHtmx
        "@uploads",      Sql.string       (UploadDestination.toString webLog.Uploads)
        yield! rssParams webLog
    ]
    
    /// The SELECT statement for custom feeds, which includes podcast feed settings if present    
    let feedSelect = "SELECT f.*, p.* FROM web_log_feed f LEFT JOIN web_log_feed_podcast p ON p.feed_id = f.id"
    
    /// Get the current custom feeds for a web log
    let getCustomFeeds (webLog : WebLog) =
        Sql.existingConnection conn
        |> Sql.query $"{feedSelect} WHERE f.web_log_id = @webLogId"
        |> Sql.parameters [ webLogIdParam webLog.Id ]
        |> Sql.executeAsync Map.toCustomFeed
    
    /// Append custom feeds to a web log
    let appendCustomFeeds (webLog : WebLog) = backgroundTask {
        let! feeds = getCustomFeeds webLog
        return { webLog with Rss = { webLog.Rss with CustomFeeds = feeds } }
    }
    
    /// The parameters to save a podcast feed
    let podcastParams feedId (podcast : PodcastOptions) = [
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
    
    /// The parameters to save a custom feed
    let feedParams webLogId (feed : CustomFeed) = [
        webLogIdParam webLogId
        "@id",     Sql.string (CustomFeedId.toString feed.Id)
        "@source", Sql.string (CustomFeedSource.toString feed.Source)
        "@path",   Sql.string (Permalink.toString feed.Path)
    ]

    /// Update the custom feeds for a web log
    let updateCustomFeeds (webLog : WebLog) = backgroundTask {
        let! feeds = getCustomFeeds webLog
        let toDelete, _ = Utils.diffLists feeds webLog.Rss.CustomFeeds (fun it -> $"{CustomFeedId.toString it.Id}")
        let toId (feed : CustomFeed) = feed.Id
        let toAddOrUpdate =
            webLog.Rss.CustomFeeds |> List.filter (fun f -> not (toDelete |> List.map toId |> List.contains f.Id))
        if not (List.isEmpty toDelete) || not (List.isEmpty toAddOrUpdate) then
            let! _ =
                Sql.existingConnection conn
                |> Sql.executeTransactionAsync [
                    if not (List.isEmpty toDelete) then
                        "DELETE FROM web_log_feed_podcast WHERE feed_id = @id;
                         DELETE FROM web_log_feed         WHERE id      = @id",
                        toDelete |> List.map (fun it -> [ "@id", Sql.string (CustomFeedId.toString it.Id) ])
                    if not (List.isEmpty toAddOrUpdate) then
                        "INSERT INTO web_log_feed (
                            id, web_log_id, source, path
                        ) VALUES (
                            @id, @webLogId, @source, @path
                        ) ON CONFLICT (id) DO UPDATE
                        SET source = EXCLUDED.source,
                            path   = EXCLUDED.path",
                        toAddOrUpdate |> List.map (feedParams webLog.Id)
                        let podcasts = toAddOrUpdate |> List.filter (fun it -> Option.isSome it.Podcast)
                        if not (List.isEmpty podcasts) then
                            "INSERT INTO web_log_feed_podcast (
                                feed_id, title, subtitle, items_in_feed, summary, displayed_author, email, image_url,
                                apple_category, apple_subcategory, explicit, default_media_type, media_base_url,
                                podcast_guid, funding_url, funding_text, medium
                            ) VALUES (
                                @feedId, @title, @subtitle, @itemsInFeed, @summary, @displayedAuthor, @email, @imageUrl,
                                @appleCategory, @appleSubcategory, @explicit, @defaultMediaType, @mediaBaseUrl,
                                @podcastGuid, @fundingUrl, @fundingText, @medium
                            ) ON CONFLICT (feed_id) DO UPDATE
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
                                medium             = EXCLUDED.medium",
                            podcasts |> List.map (fun it -> podcastParams it.Id it.Podcast.Value)
                        let hadPodcasts =
                            toAddOrUpdate
                            |> List.filter (fun it ->
                                match feeds |> List.tryFind (fun feed -> feed.Id = it.Id) with
                                | Some feed -> Option.isSome feed.Podcast && Option.isNone it.Podcast
                                | None -> false)
                        if not (List.isEmpty hadPodcasts) then
                            "DELETE FROM web_log_feed_podcast WHERE feed_id = @id",
                            hadPodcasts |> List.map (fun it -> [ "@id", Sql.string (CustomFeedId.toString it.Id) ])
                ]
            ()
    }
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a web log
    let add webLog = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query
                "INSERT INTO web_log (
                    id, name, slug, subtitle, default_page, posts_per_page, theme_id, url_base, time_zone, auto_htmx,
                    uploads, is_feed_enabled, feed_name, items_in_feed, is_category_enabled, is_tag_enabled, copyright
                ) VALUES (
                    @id, @name, @slug, @subtitle, @defaultPage, @postsPerPage, @themeId, @urlBase, @timeZone, @autoHtmx,
                    @uploads, @isFeedEnabled, @feedName, @itemsInFeed, @isCategoryEnabled, @isTagEnabled, @copyright
                )"
            |> Sql.parameters (webLogParams webLog)
            |> Sql.executeNonQueryAsync
        do! updateCustomFeeds webLog
    }
    
    /// Retrieve all web logs
    let all () = backgroundTask {
        let! webLogs =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM web_log"
            |> Sql.executeAsync Map.toWebLog
        let! feeds =
            Sql.existingConnection conn
            |> Sql.query feedSelect
            |> Sql.executeAsync (fun row -> WebLogId (row.string "web_log_id"), Map.toCustomFeed row)
        return
            webLogs
            |> List.map (fun it ->
                { it with
                    Rss =
                        { it.Rss with
                            CustomFeeds = feeds |> List.filter (fun (wlId, _) -> wlId = it.Id) |> List.map snd } })
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
    let findByHost url = backgroundTask {
        let! webLog =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM web_log WHERE url_base = @urlBase"
            |> Sql.parameters [ "@urlBase", Sql.string url ]
            |> Sql.executeAsync Map.toWebLog
            |> tryHead
        if Option.isSome webLog then
            let! withFeeds = appendCustomFeeds webLog.Value
            return Some withFeeds
        else return None
    }
    
    /// Find a web log by its ID
    let findById webLogId = backgroundTask {
        let! webLog =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM web_log WHERE id = @webLogId"
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toWebLog
            |> tryHead
        if Option.isSome webLog then
            let! withFeeds = appendCustomFeeds webLog.Value
            return Some withFeeds
        else return None
    }
    
    /// Update settings for a web log
    let updateSettings webLog = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query
                "UPDATE web_log
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
                  WHERE id = @id"
            |> Sql.parameters (webLogParams webLog)
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Update RSS options for a web log
    let updateRssOptions (webLog : WebLog) = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query
                "UPDATE web_log
                    SET is_feed_enabled     = @isFeedEnabled,
                        feed_name           = @feedName,
                        items_in_feed       = @itemsInFeed,
                        is_category_enabled = @isCategoryEnabled,
                        is_tag_enabled      = @isTagEnabled,
                        copyright           = @copyright
                  WHERE id = @webLogId"
            |> Sql.parameters (webLogIdParam webLog.Id :: rssParams webLog)
            |> Sql.executeNonQueryAsync
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
