namespace MyWebLog.Data

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data.SQLite
open Newtonsoft.Json
open NodaTime

/// SQLite myWebLog data implementation        
type SQLiteData (conn : SqliteConnection, log : ILogger<SQLiteData>, ser : JsonSerializer) =
    
    let ensureTables () = backgroundTask {

        use cmd = conn.CreateCommand ()
        
        let! tables = backgroundTask {
            cmd.CommandText <- "SELECT name FROM sqlite_master WHERE type = 'table'"
            let! rdr = cmd.ExecuteReaderAsync ()
            let mutable tableList = []
            while rdr.Read() do
                tableList <- Map.getString "name" rdr :: tableList
            do! rdr.CloseAsync ()
            return tableList
        }
        let needsTable table =
            not (List.contains table tables)
        seq {
            // Theme tables
            if needsTable "theme" then
                "CREATE TABLE theme (
                    id       TEXT PRIMARY KEY,
                    name     TEXT NOT NULL,
                    version  TEXT NOT NULL)"
            if needsTable "theme_template" then
                "CREATE TABLE theme_template (
                    theme_id  TEXT NOT NULL REFERENCES theme (id),
                    name      TEXT NOT NULL,
                    template  TEXT NOT NULL,
                    PRIMARY KEY (theme_id, name))"
            if needsTable "theme_asset" then
                "CREATE TABLE theme_asset (
                    theme_id    TEXT NOT NULL REFERENCES theme (id),
                    path        TEXT NOT NULL,
                    updated_on  TEXT NOT NULL,
                    data        BLOB NOT NULL,
                    PRIMARY KEY (theme_id, path))"
            
            // Web log tables
            if needsTable "web_log" then
                "CREATE TABLE web_log (
                    id                   TEXT PRIMARY KEY,
                    name                 TEXT NOT NULL,
                    slug                 TEXT NOT NULL,
                    subtitle             TEXT,
                    default_page         TEXT NOT NULL,
                    posts_per_page       INTEGER NOT NULL,
                    theme_id             TEXT NOT NULL REFERENCES theme (id),
                    url_base             TEXT NOT NULL,
                    time_zone            TEXT NOT NULL,
                    auto_htmx            INTEGER NOT NULL DEFAULT 0,
                    uploads              TEXT NOT NULL,
                    is_feed_enabled      INTEGER NOT NULL DEFAULT 0,
                    feed_name            TEXT NOT NULL,
                    items_in_feed        INTEGER,
                    is_category_enabled  INTEGER NOT NULL DEFAULT 0,
                    is_tag_enabled       INTEGER NOT NULL DEFAULT 0,
                    copyright            TEXT);
                CREATE INDEX web_log_theme_idx ON web_log (theme_id)"
            if needsTable "web_log_feed" then
                "CREATE TABLE web_log_feed (
                    id          TEXT PRIMARY KEY,
                    web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                    source      TEXT NOT NULL,
                    path        TEXT NOT NULL,
                    podcast     TEXT);
                CREATE INDEX web_log_feed_web_log_idx ON web_log_feed (web_log_id)"
            
            // Category table
            if needsTable "category" then
                "CREATE TABLE category (
                    id           TEXT PRIMARY KEY,
                    web_log_id   TEXT NOT NULL REFERENCES web_log (id),
                    name         TEXT NOT NULL,
                    slug         TEXT NOT NULL,
                    description  TEXT,
                    parent_id    TEXT);
                CREATE INDEX category_web_log_idx ON category (web_log_id)"
            
            // Web log user table
            if needsTable "web_log_user" then
                "CREATE TABLE web_log_user (
                    id              TEXT PRIMARY KEY,
                    web_log_id      TEXT NOT NULL REFERENCES web_log (id),
                    email           TEXT NOT NULL,
                    first_name      TEXT NOT NULL,
                    last_name       TEXT NOT NULL,
                    preferred_name  TEXT NOT NULL,
                    password_hash   TEXT NOT NULL,
                    url             TEXT,
                    access_level    TEXT NOT NULL,
                    created_on      TEXT NOT NULL,
                    last_seen_on    TEXT);
                CREATE INDEX web_log_user_web_log_idx ON web_log_user (web_log_id);
                CREATE INDEX web_log_user_email_idx   ON web_log_user (web_log_id, email)"
            
            // Page tables
            if needsTable "page" then
                "CREATE TABLE page (
                    id               TEXT PRIMARY KEY,
                    web_log_id       TEXT NOT NULL REFERENCES web_log (id),
                    author_id        TEXT NOT NULL REFERENCES web_log_user (id),
                    title            TEXT NOT NULL,
                    permalink        TEXT NOT NULL,
                    published_on     TEXT NOT NULL,
                    updated_on       TEXT NOT NULL,
                    is_in_page_list  INTEGER NOT NULL DEFAULT 0,
                    template         TEXT,
                    page_text        TEXT NOT NULL,
                    meta_items       TEXT);
                CREATE INDEX page_web_log_idx   ON page (web_log_id);
                CREATE INDEX page_author_idx    ON page (author_id);
                CREATE INDEX page_permalink_idx ON page (web_log_id, permalink)"
            if needsTable "page_permalink" then
                "CREATE TABLE page_permalink (
                    page_id    TEXT NOT NULL REFERENCES page (id),
                    permalink  TEXT NOT NULL,
                    PRIMARY KEY (page_id, permalink))"
            if needsTable "page_revision" then
                "CREATE TABLE page_revision (
                    page_id        TEXT NOT NULL REFERENCES page (id),
                    as_of          TEXT NOT NULL,
                    revision_text  TEXT NOT NULL,
                    PRIMARY KEY (page_id, as_of))"
            
            // Post tables
            if needsTable "post" then
                "CREATE TABLE post (
                    id            TEXT PRIMARY KEY,
                    web_log_id    TEXT NOT NULL REFERENCES web_log (id),
                    author_id     TEXT NOT NULL REFERENCES web_log_user (id),
                    status        TEXT NOT NULL,
                    title         TEXT NOT NULL,
                    permalink     TEXT NOT NULL,
                    published_on  TEXT,
                    updated_on    TEXT NOT NULL,
                    template      TEXT,
                    post_text     TEXT NOT NULL,
                    meta_items    TEXT,
                    episode       TEXT);
                CREATE INDEX post_web_log_idx   ON post (web_log_id);
                CREATE INDEX post_author_idx    ON post (author_id);
                CREATE INDEX post_status_idx    ON post (web_log_id, status, updated_on);
                CREATE INDEX post_permalink_idx ON post (web_log_id, permalink)"
            if needsTable "post_category" then
                "CREATE TABLE post_category (
                    post_id      TEXT NOT NULL REFERENCES post (id),
                    category_id  TEXT NOT NULL REFERENCES category (id),
                    PRIMARY KEY (post_id, category_id));
                CREATE INDEX post_category_category_idx ON post_category (category_id)"
            if needsTable "post_tag" then
                "CREATE TABLE post_tag (
                    post_id  TEXT NOT NULL REFERENCES post (id),
                    tag      TEXT NOT NULL,
                    PRIMARY KEY (post_id, tag))"
            if needsTable "post_permalink" then
                "CREATE TABLE post_permalink (
                    post_id    TEXT NOT NULL REFERENCES post (id),
                    permalink  TEXT NOT NULL,
                    PRIMARY KEY (post_id, permalink))"
            if needsTable "post_revision" then
                "CREATE TABLE post_revision (
                    post_id        TEXT NOT NULL REFERENCES post (id),
                    as_of          TEXT NOT NULL,
                    revision_text  TEXT NOT NULL,
                    PRIMARY KEY (post_id, as_of))"
            if needsTable "post_comment" then
                "CREATE TABLE post_comment (
                    id              TEXT PRIMARY KEY,
                    post_id         TEXT NOT NULL REFERENCES post(id),
                    in_reply_to_id  TEXT,
                    name            TEXT NOT NULL,
                    email           TEXT NOT NULL,
                    url             TEXT,
                    status          TEXT NOT NULL,
                    posted_on       TEXT NOT NULL,
                    comment_text    TEXT NOT NULL);
                CREATE INDEX post_comment_post_idx ON post_comment (post_id)"
            
            // Tag map table
            if needsTable "tag_map" then
                "CREATE TABLE tag_map (
                    id          TEXT PRIMARY KEY,
                    web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                    tag         TEXT NOT NULL,
                    url_value   TEXT NOT NULL);
                CREATE INDEX tag_map_web_log_idx ON tag_map (web_log_id)"
            
            // Uploaded file table
            if needsTable "upload" then
                "CREATE TABLE upload (
                    id          TEXT PRIMARY KEY,
                    web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                    path        TEXT NOT NULL,
                    updated_on  TEXT NOT NULL,
                    data        BLOB NOT NULL);
                CREATE INDEX upload_web_log_idx ON upload (web_log_id);
                CREATE INDEX upload_path_idx    ON upload (web_log_id, path)"
            
            // Database version table
            if needsTable "db_version" then
                "CREATE TABLE db_version (id TEXT PRIMARY KEY);
                 INSERT INTO db_version VALUES ('v2-rc1')"
        }
        |> Seq.map (fun sql ->
            log.LogInformation $"Creating {(sql.Split ' ')[2]} table..."
            cmd.CommandText <- sql
            write cmd |> Async.AwaitTask |> Async.RunSynchronously)
        |> List.ofSeq
        |> ignore
    }
    
    /// Set the database version to the specified version
    let setDbVersion version = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"DELETE FROM db_version; INSERT INTO db_version VALUES ('%s{version}')"
        do! write cmd
    }
    
    /// Implement the changes between v2-rc1 and v2-rc2
    let migrateV2Rc1ToV2Rc2 () = backgroundTask {
        let logStep = Utils.logMigrationStep log "v2-rc1 to v2-rc2"
        // Move meta items, podcast settings, and episode details to JSON-encoded text fields
        use cmd = conn.CreateCommand ()
        logStep "Adding new columns"
        cmd.CommandText <-
            "ALTER TABLE web_log_feed ADD COLUMN podcast    TEXT;
             ALTER TABLE page         ADD COLUMN meta_items TEXT;
             ALTER TABLE post         ADD COLUMN meta_items TEXT;
             ALTER TABLE post         ADD COLUMN episode    TEXT"
        do! write cmd
        logStep "Migrating meta items"
        let migrateMeta entity = backgroundTask {
            cmd.CommandText <- $"SELECT * FROM %s{entity}_meta"
            use! metaRdr = cmd.ExecuteReaderAsync ()
            let allMetas =
                seq {
                    while metaRdr.Read () do
                        Map.getString $"{entity}_id" metaRdr,
                        { Name = Map.getString "name" metaRdr; Value = Map.getString "value" metaRdr }
                } |> List.ofSeq
            metaRdr.Close ()
            let metas =
                allMetas
                |> List.map fst
                |> List.distinct
                |> List.map (fun it -> it, allMetas |> List.filter (fun meta -> fst meta = it))
            metas
            |> List.iter (fun (entityId, items) ->
                cmd.CommandText <-
                    "UPDATE post
                        SET meta_items = @metaItems
                      WHERE id = @postId"
                [   cmd.Parameters.AddWithValue ("@metaItems", Utils.serialize ser items)
                    cmd.Parameters.AddWithValue ("@id",        entityId) ] |> ignore
                let _ = cmd.ExecuteNonQuery ()
                cmd.Parameters.Clear ())
        }
        do! migrateMeta "page"
        do! migrateMeta "post"
        logStep "Migrating podcasts and episodes"
        cmd.CommandText <- "SELECT * FROM web_log_feed_podcast"
        use! podcastRdr = cmd.ExecuteReaderAsync ()
        let podcasts =
            seq {
                while podcastRdr.Read () do
                    CustomFeedId (Map.getString "feed_id" podcastRdr),
                    {   Title             = Map.getString "title"              podcastRdr
                        Subtitle          = Map.tryString "subtitle"           podcastRdr
                        ItemsInFeed       = Map.getInt    "items_in_feed"      podcastRdr
                        Summary           = Map.getString "summary"            podcastRdr
                        DisplayedAuthor   = Map.getString "displayed_author"   podcastRdr
                        Email             = Map.getString "email"              podcastRdr
                        ImageUrl          = Map.getString "image_url"          podcastRdr |> Permalink
                        AppleCategory     = Map.getString "apple_category"     podcastRdr
                        AppleSubcategory  = Map.tryString "apple_subcategory"  podcastRdr
                        Explicit          = Map.getString "explicit"           podcastRdr |> ExplicitRating.parse
                        DefaultMediaType  = Map.tryString "default_media_type" podcastRdr
                        MediaBaseUrl      = Map.tryString "media_base_url"     podcastRdr
                        PodcastGuid       = Map.tryGuid   "podcast_guid"       podcastRdr
                        FundingUrl        = Map.tryString "funding_url"        podcastRdr
                        FundingText       = Map.tryString "funding_text"       podcastRdr
                        Medium            = Map.tryString "medium"             podcastRdr
                                            |> Option.map PodcastMedium.parse
                    }
            } |> List.ofSeq
        podcastRdr.Close ()
        podcasts
        |> List.iter (fun (feedId, podcast) ->
            cmd.CommandText <- "UPDATE web_log_feed SET podcast = @podcast WHERE id = @id"
            [   cmd.Parameters.AddWithValue ("@podcast", Utils.serialize ser podcast)
                cmd.Parameters.AddWithValue ("@id", CustomFeedId.toString feedId) ] |> ignore
            let _ = cmd.ExecuteNonQuery ()
            cmd.Parameters.Clear ())
        cmd.CommandText <- "SELECT * FROM post_episode"
        use! epRdr = cmd.ExecuteReaderAsync ()
        let episodes =
            seq {
                while epRdr.Read () do
                    PostId (Map.getString "post_id" epRdr),
                    {   Media              = Map.getString   "media"               epRdr
                        Length             = Map.getLong     "length"              epRdr
                        Duration           = Map.tryTimeSpan "duration"            epRdr
                                             |> Option.map Duration.FromTimeSpan
                        MediaType          = Map.tryString   "media_type"          epRdr
                        ImageUrl           = Map.tryString   "image_url"           epRdr
                        Subtitle           = Map.tryString   "subtitle"            epRdr
                        Explicit           = Map.tryString   "explicit"            epRdr
                                             |> Option.map ExplicitRating.parse
                        ChapterFile        = Map.tryString   "chapter_file"        epRdr
                        ChapterType        = Map.tryString   "chapter_type"        epRdr
                        TranscriptUrl      = Map.tryString   "transcript_url"      epRdr
                        TranscriptType     = Map.tryString   "transcript_type"     epRdr
                        TranscriptLang     = Map.tryString   "transcript_lang"     epRdr
                        TranscriptCaptions = Map.tryBoolean  "transcript_captions" epRdr
                        SeasonNumber       = Map.tryInt      "season_number"       epRdr
                        SeasonDescription  = Map.tryString   "season_description"  epRdr
                        EpisodeNumber      = Map.tryString   "episode_number"      epRdr
                                             |> Option.map System.Double.Parse
                        EpisodeDescription = Map.tryString   "episode_description" epRdr
                    }
            } |> List.ofSeq
        epRdr.Close ()
        episodes
        |> List.iter (fun (postId, episode) ->
            cmd.CommandText <- "UPDATE post SET episode = @episode WHERE id = @id"
            [   cmd.Parameters.AddWithValue ("@episode", Utils.serialize ser episode)
                cmd.Parameters.AddWithValue ("@id",      PostId.toString postId) ] |> ignore
            let _ = cmd.ExecuteNonQuery ()
            cmd.Parameters.Clear ())
        
        logStep "Migrating dates/times"
        let inst (dt : System.DateTime) =
            System.DateTime (dt.Ticks, System.DateTimeKind.Utc)
            |> (Instant.FromDateTimeUtc >> Noda.toSecondsPrecision)
        // page.updated_on, page.published_on
        cmd.CommandText <- "SELECT id, updated_on, published_on FROM page"
        use! pageRdr = cmd.ExecuteReaderAsync ()
        let toUpdate =
            seq {
                while pageRdr.Read () do
                    Map.getString "id" pageRdr,
                    inst (Map.getDateTime "updated_on"   pageRdr),
                    inst (Map.getDateTime "published_on" pageRdr)
            } |> List.ofSeq
        pageRdr.Close ()
        cmd.CommandText <- "UPDATE page SET updated_on = @updatedOn, published_on = @publishedOn WHERE id = @id"
        [   cmd.Parameters.Add ("@id",          SqliteType.Text)
            cmd.Parameters.Add ("@updatedOn",   SqliteType.Text)
            cmd.Parameters.Add ("@publishedOn", SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.iter (fun (pageId, updatedOn, publishedOn) ->
            cmd.Parameters["@id"         ].Value <- pageId
            cmd.Parameters["@updatedOn"  ].Value <- instantParam updatedOn
            cmd.Parameters["@publishedOn"].Value <- instantParam publishedOn
            let _ = cmd.ExecuteNonQuery ()
            ())
        cmd.Parameters.Clear ()
        // page_revision.as_of
        cmd.CommandText <- "SELECT * FROM page_revision"
        use! pageRevRdr = cmd.ExecuteReaderAsync ()
        let toUpdate =
            seq {
                while pageRevRdr.Read () do
                    let asOf = Map.getDateTime "as_of" pageRevRdr
                    Map.getString "page_id" pageRevRdr, asOf, inst asOf, Map.getString "revision_text" pageRevRdr
            } |> List.ofSeq
        pageRevRdr.Close ()
        cmd.CommandText <-
            "DELETE FROM page_revision WHERE page_id = @pageId AND as_of = @oldAsOf;
             INSERT INTO page_revision (page_id, as_of, revision_text) VALUES (@pageId, @asOf, @text)"
        [   cmd.Parameters.Add ("@pageId",  SqliteType.Text)
            cmd.Parameters.Add ("@oldAsOf", SqliteType.Text)
            cmd.Parameters.Add ("@asOf",    SqliteType.Text)
            cmd.Parameters.Add ("@text",    SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.iter (fun (pageId, oldAsOf, asOf, text) ->
            cmd.Parameters["@pageId" ].Value <- pageId
            cmd.Parameters["@oldAsOf"].Value <- oldAsOf
            cmd.Parameters["@asOf"   ].Value <- instantParam asOf
            cmd.Parameters["@text"   ].Value <- text
            let _ = cmd.ExecuteNonQuery ()
            ())
        cmd.Parameters.Clear ()
        // post.updated_on, post.published_on (opt)
        cmd.CommandText <- "SELECT id, updated_on, published_on FROM post"
        use! postRdr = cmd.ExecuteReaderAsync ()
        let toUpdate =
            seq {
                while postRdr.Read () do
                    Map.getString "id" postRdr,
                    inst (Map.getDateTime "updated_on"   postRdr),
                    (Map.tryDateTime "published_on" postRdr |> Option.map inst)
            } |> List.ofSeq
        postRdr.Close ()
        cmd.CommandText <- "UPDATE post SET updated_on = @updatedOn, published_on = @publishedOn WHERE id = @id"
        [   cmd.Parameters.Add ("@id",          SqliteType.Text)
            cmd.Parameters.Add ("@updatedOn",   SqliteType.Text)
            cmd.Parameters.Add ("@publishedOn", SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.iter (fun (postId, updatedOn, publishedOn) ->
            cmd.Parameters["@id"         ].Value <- postId
            cmd.Parameters["@updatedOn"  ].Value <- instantParam updatedOn
            cmd.Parameters["@publishedOn"].Value <- maybeInstant publishedOn
            let _ = cmd.ExecuteNonQuery ()
            ())
        cmd.Parameters.Clear ()
        // post_revision.as_of
        cmd.CommandText <- "SELECT * FROM post_revision"
        use! postRevRdr = cmd.ExecuteReaderAsync ()
        let toUpdate =
            seq {
                while postRevRdr.Read () do
                    let asOf = Map.getDateTime "as_of" postRevRdr
                    Map.getString "post_id" postRevRdr, asOf, inst asOf, Map.getString "revision_text" postRevRdr
            } |> List.ofSeq
        postRevRdr.Close ()
        cmd.CommandText <-
            "DELETE FROM post_revision WHERE post_id = @postId AND as_of = @oldAsOf;
             INSERT INTO post_revision (post_id, as_of, revision_text) VALUES (@postId, @asOf, @text)"
        [   cmd.Parameters.Add ("@postId",  SqliteType.Text)
            cmd.Parameters.Add ("@oldAsOf", SqliteType.Text)
            cmd.Parameters.Add ("@asOf",    SqliteType.Text)
            cmd.Parameters.Add ("@text",    SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.iter (fun (postId, oldAsOf, asOf, text) ->
            cmd.Parameters["@postId" ].Value <- postId
            cmd.Parameters["@oldAsOf"].Value <- oldAsOf
            cmd.Parameters["@asOf"   ].Value <- instantParam asOf
            cmd.Parameters["@text"   ].Value <- text
            let _ = cmd.ExecuteNonQuery ()
            ())
        cmd.Parameters.Clear ()
        // theme_asset.updated_on
        cmd.CommandText <- "SELECT theme_id, path, updated_on FROM theme_asset"
        use! assetRdr = cmd.ExecuteReaderAsync ()
        let toUpdate =
            seq {
                while assetRdr.Read () do
                    Map.getString "theme_id" assetRdr, Map.getString "path" assetRdr,
                    inst (Map.getDateTime "updated_on" assetRdr)
            } |> List.ofSeq
        assetRdr.Close ()
        cmd.CommandText <- "UPDATE theme_asset SET updated_on = @updatedOn WHERE theme_id = @themeId AND path = @path"
        [   cmd.Parameters.Add ("@updatedOn", SqliteType.Text)
            cmd.Parameters.Add ("@themeId",   SqliteType.Text)
            cmd.Parameters.Add ("@path",      SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.iter (fun (themeId, path, updatedOn) ->
            cmd.Parameters["@themeId"  ].Value <- themeId
            cmd.Parameters["@path"     ].Value <- path
            cmd.Parameters["@updatedOn"].Value <- instantParam updatedOn
            let _ = cmd.ExecuteNonQuery ()
            ())
        cmd.Parameters.Clear ()
        // upload.updated_on
        cmd.CommandText <- "SELECT id, updated_on FROM upload"
        use! upRdr = cmd.ExecuteReaderAsync ()
        let toUpdate =
            seq {
                while upRdr.Read () do
                    Map.getString "id" upRdr, inst (Map.getDateTime "updated_on" upRdr)
            } |> List.ofSeq
        upRdr.Close ()
        cmd.CommandText <- "UPDATE upload SET updated_on = @updatedOn WHERE id = @id"
        [   cmd.Parameters.Add ("@updatedOn", SqliteType.Text)
            cmd.Parameters.Add ("@id",        SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.iter (fun (upId, updatedOn) ->
            cmd.Parameters["@id"       ].Value <- upId
            cmd.Parameters["@updatedOn"].Value <- instantParam updatedOn
            let _ = cmd.ExecuteNonQuery ()
            ())
        cmd.Parameters.Clear ()
        // web_log_user.created_on, web_log_user.last_seen_on (opt)
        cmd.CommandText <- "SELECT id, created_on, last_seen_on FROM web_log_user"
        use! userRdr = cmd.ExecuteReaderAsync ()
        let toUpdate =
            seq {
                while userRdr.Read () do
                    Map.getString "id" userRdr,
                    inst (Map.getDateTime "created_on" userRdr),
                    (Map.tryDateTime "last_seen_on" userRdr |> Option.map inst)
            } |> List.ofSeq
        userRdr.Close ()
        cmd.CommandText <- "UPDATE web_log_user SET created_on = @createdOn, last_seen_on = @lastSeenOn WHERE id = @id"
        [   cmd.Parameters.Add ("@id",         SqliteType.Text)
            cmd.Parameters.Add ("@createdOn",  SqliteType.Text)
            cmd.Parameters.Add ("@lastSeenOn", SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.iter (fun (userId, createdOn, lastSeenOn) ->
            cmd.Parameters["@id"        ].Value <- userId
            cmd.Parameters["@createdOn" ].Value <- instantParam createdOn
            cmd.Parameters["@lastSeenOn"].Value <- maybeInstant lastSeenOn
            let _ = cmd.ExecuteNonQuery ()
            ())
        cmd.Parameters.Clear ()
        
        conn.Close ()
        conn.Open ()
        
        logStep "Dropping old tables and columns"
        cmd.CommandText <-
            "ALTER TABLE web_log_user DROP COLUMN salt;
             DROP  TABLE post_episode;
             DROP  TABLE post_meta;
             DROP  TABLE page_meta;
             DROP  TABLE web_log_feed_podcast"
        do! write cmd
        
        logStep "Setting database version to v2-rc2"
        do! setDbVersion "v2-rc2"
    }
    
    /// Migrate data among versions (up only)
    let migrate version = backgroundTask {
        
        match version with
        | Some v when v = "v2-rc2" -> ()
        | Some v when v = "v2-rc1" -> do! migrateV2Rc1ToV2Rc2 ()
        | Some _
        | None ->
            log.LogWarning $"Unknown database version; assuming {Utils.currentDbVersion}"
            do! setDbVersion Utils.currentDbVersion
    }

    /// The connection for this instance
    member _.Conn = conn
    
    /// Make a SQLite connection ready to execute commends
    static member setUpConnection (conn : SqliteConnection) = backgroundTask {
        do! conn.OpenAsync ()
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "PRAGMA foreign_keys = TRUE"
        let! _ = cmd.ExecuteNonQueryAsync ()
        ()
    }
    
    interface IData with
    
        member _.Category   = SQLiteCategoryData   conn
        member _.Page       = SQLitePageData       (conn, ser)
        member _.Post       = SQLitePostData       (conn, ser)
        member _.TagMap     = SQLiteTagMapData     conn
        member _.Theme      = SQLiteThemeData      conn
        member _.ThemeAsset = SQLiteThemeAssetData conn
        member _.Upload     = SQLiteUploadData     conn
        member _.WebLog     = SQLiteWebLogData     (conn, ser)
        member _.WebLogUser = SQLiteWebLogUserData conn
        
        member _.Serializer = ser
        
        member _.StartUp () = backgroundTask {
            do! ensureTables ()
            
            use cmd = conn.CreateCommand ()
            cmd.CommandText <- "SELECT id FROM db_version"
            use! rdr = cmd.ExecuteReaderAsync ()
            let version = if rdr.Read () then Some (Map.getString "id" rdr) else None
            match version with
            | Some v when v = "v2-rc2" -> ()
            | Some _
            | None -> do! migrate version
        }
