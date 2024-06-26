namespace MyWebLog.Data

open System
open System.Threading.Tasks
open BitBadger.Documents
open BitBadger.Documents.Sqlite
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data.SQLite
open Newtonsoft.Json
open NodaTime

/// SQLite myWebLog data implementation
type SQLiteData(conn: SqliteConnection, log: ILogger<SQLiteData>, ser: JsonSerializer) =
    
    /// Create tables (and their associated indexes) if they do not exist
    let ensureTables () = backgroundTask {
        
        Configuration.useSerializer (Utils.createDocumentSerializer ser)
        
        let! tables = conn.customList "SELECT name FROM sqlite_master WHERE type = 'table'" [] _.GetString(0)
        
        let needsTable table =
            not (List.contains table tables)
        
        let jsonTable table =
            $"{Query.Definition.ensureTable table}; {Query.Definition.ensureKey table}"
        
        let tasks =
            seq {
                // Theme tables
                if needsTable Table.Theme then jsonTable Table.Theme
                if needsTable Table.ThemeAsset then
                    $"CREATE TABLE {Table.ThemeAsset} (
                        theme_id    TEXT NOT NULL,
                        path        TEXT NOT NULL,
                        updated_on  TEXT NOT NULL,
                        data        BLOB NOT NULL,
                        PRIMARY KEY (theme_id, path))"
                
                // Web log table
                if needsTable Table.WebLog then jsonTable Table.WebLog
                
                // Category table
                if needsTable Table.Category then
                    $"""{jsonTable Table.Category};
                        {Query.Definition.ensureIndexOn Table.Category "web_log" [ nameof Category.Empty.WebLogId ]}"""
                
                // Web log user table
                if needsTable Table.WebLogUser then
                    $"""{jsonTable Table.WebLogUser};
                        {Query.Definition.ensureIndexOn
                             Table.WebLogUser
                             "email"
                             [ nameof WebLogUser.Empty.WebLogId; nameof WebLogUser.Empty.Email ]}"""
                
                // Page tables
                if needsTable Table.Page then
                    $"""{jsonTable Table.Page};
                        {Query.Definition.ensureIndexOn Table.Page "author" [ nameof Page.Empty.AuthorId ]};
                        {Query.Definition.ensureIndexOn
                             Table.Page "permalink" [ nameof Page.Empty.WebLogId; nameof Page.Empty.Permalink ]}"""
                if needsTable Table.PageRevision then
                    $"CREATE TABLE {Table.PageRevision} (
                        page_id        TEXT NOT NULL,
                        as_of          TEXT NOT NULL,
                        revision_text  TEXT NOT NULL,
                        PRIMARY KEY (page_id, as_of))"
                
                // Post tables
                if needsTable Table.Post then
                    $"""{jsonTable Table.Post};
                        {Query.Definition.ensureIndexOn Table.Post "author" [ nameof Post.Empty.AuthorId ]};
                        {Query.Definition.ensureIndexOn
                             Table.Post "permalink" [ nameof Post.Empty.WebLogId; nameof Post.Empty.Permalink ]};
                        {Query.Definition.ensureIndexOn
                             Table.Post
                             "status"
                             [ nameof Post.Empty.WebLogId; nameof Post.Empty.Status; nameof Post.Empty.UpdatedOn ]}"""
                      // TODO: index categories by post?
                if needsTable Table.PostRevision then
                    $"CREATE TABLE {Table.PostRevision} (
                        post_id        TEXT NOT NULL,
                        as_of          TEXT NOT NULL,
                        revision_text  TEXT NOT NULL,
                        PRIMARY KEY (post_id, as_of))"
                if needsTable Table.PostComment then
                    $"""{jsonTable Table.PostComment};
                        {Query.Definition.ensureIndexOn Table.PostComment "post" [ nameof Comment.Empty.PostId ]}"""
                
                // Tag map table
                if needsTable Table.TagMap then
                    $"""{jsonTable Table.TagMap};
                        {Query.Definition.ensureIndexOn
                             Table.TagMap "url" [ nameof TagMap.Empty.WebLogId; nameof TagMap.Empty.UrlValue ]}"""
                
                // Uploaded file table
                if needsTable Table.Upload then
                    $"CREATE TABLE {Table.Upload} (
                        id          TEXT PRIMARY KEY,
                        web_log_id  TEXT NOT NULL,
                        path        TEXT NOT NULL,
                        updated_on  TEXT NOT NULL,
                        data        BLOB NOT NULL);
                      CREATE INDEX idx_{Table.Upload}_path ON {Table.Upload} (web_log_id, path)"
                
                // Database version table
                if needsTable Table.DbVersion then
                    $"CREATE TABLE {Table.DbVersion} (id TEXT PRIMARY KEY);
                      INSERT INTO {Table.DbVersion} VALUES ('{Utils.Migration.currentDbVersion}')"
            }
            |> Seq.map (fun sql ->
                log.LogInformation $"""Creating {(sql.Replace("IF NOT EXISTS ", "").Split ' ')[2]} table..."""
                conn.customNonQuery sql [])
        
        let! _ = Task.WhenAll tasks
        ()
    }
    
    /// Set the database version to the specified version
    let setDbVersion version =
        conn.customNonQuery $"DELETE FROM {Table.DbVersion}; INSERT INTO {Table.DbVersion} VALUES ('%s{version}')" []
        
    /// Implement the changes between v2-rc1 and v2-rc2
    let migrateV2Rc1ToV2Rc2 () = backgroundTask {
        let logStep = Utils.Migration.logStep log "v2-rc1 to v2-rc2"
        // Move meta items, podcast settings, and episode details to JSON-encoded text fields
        use cmd = conn.CreateCommand()
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
            use! metaRdr = cmd.ExecuteReaderAsync()
            let allMetas =
                seq {
                    while metaRdr.Read() do
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
                [ cmd.Parameters.AddWithValue("@metaItems", Utils.serialize ser items)
                  cmd.Parameters.AddWithValue("@id",        entityId) ] |> ignore
                let _ = cmd.ExecuteNonQuery()
                cmd.Parameters.Clear())
        }
        do! migrateMeta "page"
        do! migrateMeta "post"
        logStep "Migrating podcasts and episodes"
        cmd.CommandText <- "SELECT * FROM web_log_feed_podcast"
        use! podcastRdr = cmd.ExecuteReaderAsync()
        let podcasts =
            seq {
                while podcastRdr.Read() do
                    CustomFeedId (Map.getString "feed_id" podcastRdr),
                    { Title             = Map.getString "title"              podcastRdr
                      Subtitle          = Map.tryString "subtitle"           podcastRdr
                      ItemsInFeed       = Map.getInt    "items_in_feed"      podcastRdr
                      Summary           = Map.getString "summary"            podcastRdr
                      DisplayedAuthor   = Map.getString "displayed_author"   podcastRdr
                      Email             = Map.getString "email"              podcastRdr
                      ImageUrl          = Map.getString "image_url"          podcastRdr |> Permalink
                      AppleCategory     = Map.getString "apple_category"     podcastRdr
                      AppleSubcategory  = Map.tryString "apple_subcategory"  podcastRdr
                      Explicit          = Map.getString "explicit"           podcastRdr |> ExplicitRating.Parse
                      DefaultMediaType  = Map.tryString "default_media_type" podcastRdr
                      MediaBaseUrl      = Map.tryString "media_base_url"     podcastRdr
                      PodcastGuid       = Map.tryGuid   "podcast_guid"       podcastRdr
                      FundingUrl        = Map.tryString "funding_url"        podcastRdr
                      FundingText       = Map.tryString "funding_text"       podcastRdr
                      Medium            = Map.tryString "medium"             podcastRdr
                                          |> Option.map PodcastMedium.Parse }
            } |> List.ofSeq
        podcastRdr.Close()
        podcasts
        |> List.iter (fun (feedId, podcast) ->
            cmd.CommandText <- "UPDATE web_log_feed SET podcast = @podcast WHERE id = @id"
            [ cmd.Parameters.AddWithValue("@podcast", Utils.serialize ser podcast)
              cmd.Parameters.AddWithValue("@id",      string feedId) ] |> ignore
            let _ = cmd.ExecuteNonQuery()
            cmd.Parameters.Clear())
        cmd.CommandText <- "SELECT * FROM post_episode"
        use! epRdr = cmd.ExecuteReaderAsync()
        let episodes =
            seq {
                while epRdr.Read() do
                    PostId (Map.getString "post_id" epRdr),
                    { Media              = Map.getString   "media"               epRdr
                      Length             = Map.getLong     "length"              epRdr
                      Duration           = Map.tryTimeSpan "duration"            epRdr
                                           |> Option.map Duration.FromTimeSpan
                      MediaType          = Map.tryString   "media_type"          epRdr
                      ImageUrl           = Map.tryString   "image_url"           epRdr
                      Subtitle           = Map.tryString   "subtitle"            epRdr
                      Explicit           = Map.tryString   "explicit"            epRdr
                                           |> Option.map ExplicitRating.Parse
                      Chapters           = Map.tryString   "chapters"            epRdr
                                           |> Option.map (Utils.deserialize<Chapter list> ser)
                      ChapterFile        = Map.tryString   "chapter_file"        epRdr
                      ChapterType        = Map.tryString   "chapter_type"        epRdr
                      ChapterWaypoints   = None 
                      TranscriptUrl      = Map.tryString   "transcript_url"      epRdr
                      TranscriptType     = Map.tryString   "transcript_type"     epRdr
                      TranscriptLang     = Map.tryString   "transcript_lang"     epRdr
                      TranscriptCaptions = Map.tryBoolean  "transcript_captions" epRdr
                      SeasonNumber       = Map.tryInt      "season_number"       epRdr
                      SeasonDescription  = Map.tryString   "season_description"  epRdr
                      EpisodeNumber      = Map.tryString   "episode_number"      epRdr |> Option.map Double.Parse
                      EpisodeDescription = Map.tryString   "episode_description" epRdr }
            } |> List.ofSeq
        epRdr.Close()
        episodes
        |> List.iter (fun (postId, episode) ->
            cmd.CommandText <- "UPDATE post SET episode = @episode WHERE id = @id"
            [ cmd.Parameters.AddWithValue("@episode", Utils.serialize ser episode)
              cmd.Parameters.AddWithValue("@id",      string postId) ] |> ignore
            let _ = cmd.ExecuteNonQuery()
            cmd.Parameters.Clear())
        
        logStep "Migrating dates/times"
        let inst (dt: DateTime) =
            DateTime(dt.Ticks, DateTimeKind.Utc)
            |> (Instant.FromDateTimeUtc >> Noda.toSecondsPrecision)
        // page.updated_on, page.published_on
        cmd.CommandText <- "SELECT id, updated_on, published_on FROM page"
        use! pageRdr = cmd.ExecuteReaderAsync()
        let toUpdate =
            seq {
                while pageRdr.Read() do
                    Map.getString "id" pageRdr,
                    inst (Map.getDateTime "updated_on"   pageRdr),
                    inst (Map.getDateTime "published_on" pageRdr)
            } |> List.ofSeq
        pageRdr.Close()
        cmd.CommandText <- "UPDATE page SET updated_on = @updatedOn, published_on = @publishedOn WHERE id = @id"
        [ cmd.Parameters.Add("@id",          SqliteType.Text)
          cmd.Parameters.Add("@updatedOn",   SqliteType.Text)
          cmd.Parameters.Add("@publishedOn", SqliteType.Text) ] |> ignore
        toUpdate
        |> List.iter (fun (pageId, updatedOn, publishedOn) ->
            cmd.Parameters["@id"         ].Value <- pageId
            cmd.Parameters["@updatedOn"  ].Value <- instantParam updatedOn
            cmd.Parameters["@publishedOn"].Value <- instantParam publishedOn
            let _ = cmd.ExecuteNonQuery()
            ())
        cmd.Parameters.Clear()
        // page_revision.as_of
        cmd.CommandText <- "SELECT * FROM page_revision"
        use! pageRevRdr = cmd.ExecuteReaderAsync()
        let toUpdate =
            seq {
                while pageRevRdr.Read() do
                    let asOf = Map.getDateTime "as_of" pageRevRdr
                    Map.getString "page_id" pageRevRdr, asOf, inst asOf, Map.getString "revision_text" pageRevRdr
            } |> List.ofSeq
        pageRevRdr.Close ()
        cmd.CommandText <-
            "DELETE FROM page_revision WHERE page_id = @pageId AND as_of = @oldAsOf;
             INSERT INTO page_revision (page_id, as_of, revision_text) VALUES (@pageId, @asOf, @text)"
        [ cmd.Parameters.Add("@pageId",  SqliteType.Text)
          cmd.Parameters.Add("@oldAsOf", SqliteType.Text)
          cmd.Parameters.Add("@asOf",    SqliteType.Text)
          cmd.Parameters.Add("@text",    SqliteType.Text) ] |> ignore
        toUpdate
        |> List.iter (fun (pageId, oldAsOf, asOf, text) ->
            cmd.Parameters["@pageId" ].Value <- pageId
            cmd.Parameters["@oldAsOf"].Value <- oldAsOf
            cmd.Parameters["@asOf"   ].Value <- instantParam asOf
            cmd.Parameters["@text"   ].Value <- text
            let _ = cmd.ExecuteNonQuery()
            ())
        cmd.Parameters.Clear()
        // post.updated_on, post.published_on (opt)
        cmd.CommandText <- "SELECT id, updated_on, published_on FROM post"
        use! postRdr = cmd.ExecuteReaderAsync()
        let toUpdate =
            seq {
                while postRdr.Read() do
                    Map.getString "id" postRdr,
                    inst (Map.getDateTime "updated_on" postRdr),
                    (Map.tryDateTime "published_on" postRdr |> Option.map inst)
            } |> List.ofSeq
        postRdr.Close()
        cmd.CommandText <- "UPDATE post SET updated_on = @updatedOn, published_on = @publishedOn WHERE id = @id"
        [ cmd.Parameters.Add("@id",          SqliteType.Text)
          cmd.Parameters.Add("@updatedOn",   SqliteType.Text)
          cmd.Parameters.Add("@publishedOn", SqliteType.Text) ] |> ignore
        toUpdate
        |> List.iter (fun (postId, updatedOn, publishedOn) ->
            cmd.Parameters["@id"         ].Value <- postId
            cmd.Parameters["@updatedOn"  ].Value <- instantParam updatedOn
            cmd.Parameters["@publishedOn"].Value <- maybeInstant publishedOn
            let _ = cmd.ExecuteNonQuery()
            ())
        cmd.Parameters.Clear()
        // post_revision.as_of
        cmd.CommandText <- "SELECT * FROM post_revision"
        use! postRevRdr = cmd.ExecuteReaderAsync()
        let toUpdate =
            seq {
                while postRevRdr.Read() do
                    let asOf = Map.getDateTime "as_of" postRevRdr
                    Map.getString "post_id" postRevRdr, asOf, inst asOf, Map.getString "revision_text" postRevRdr
            } |> List.ofSeq
        postRevRdr.Close()
        cmd.CommandText <-
            "DELETE FROM post_revision WHERE post_id = @postId AND as_of = @oldAsOf;
             INSERT INTO post_revision (post_id, as_of, revision_text) VALUES (@postId, @asOf, @text)"
        [ cmd.Parameters.Add("@postId",  SqliteType.Text)
          cmd.Parameters.Add("@oldAsOf", SqliteType.Text)
          cmd.Parameters.Add("@asOf",    SqliteType.Text)
          cmd.Parameters.Add("@text",    SqliteType.Text) ] |> ignore
        toUpdate
        |> List.iter (fun (postId, oldAsOf, asOf, text) ->
            cmd.Parameters["@postId" ].Value <- postId
            cmd.Parameters["@oldAsOf"].Value <- oldAsOf
            cmd.Parameters["@asOf"   ].Value <- instantParam asOf
            cmd.Parameters["@text"   ].Value <- text
            let _ = cmd.ExecuteNonQuery()
            ())
        cmd.Parameters.Clear()
        // theme_asset.updated_on
        cmd.CommandText <- "SELECT theme_id, path, updated_on FROM theme_asset"
        use! assetRdr = cmd.ExecuteReaderAsync()
        let toUpdate =
            seq {
                while assetRdr.Read() do
                    Map.getString "theme_id" assetRdr, Map.getString "path" assetRdr,
                    inst (Map.getDateTime "updated_on" assetRdr)
            } |> List.ofSeq
        assetRdr.Close ()
        cmd.CommandText <- "UPDATE theme_asset SET updated_on = @updatedOn WHERE theme_id = @themeId AND path = @path"
        [ cmd.Parameters.Add("@updatedOn", SqliteType.Text)
          cmd.Parameters.Add("@themeId",   SqliteType.Text)
          cmd.Parameters.Add("@path",      SqliteType.Text) ] |> ignore
        toUpdate
        |> List.iter (fun (themeId, path, updatedOn) ->
            cmd.Parameters["@themeId"  ].Value <- themeId
            cmd.Parameters["@path"     ].Value <- path
            cmd.Parameters["@updatedOn"].Value <- instantParam updatedOn
            let _ = cmd.ExecuteNonQuery()
            ())
        cmd.Parameters.Clear()
        // upload.updated_on
        cmd.CommandText <- "SELECT id, updated_on FROM upload"
        use! upRdr = cmd.ExecuteReaderAsync()
        let toUpdate =
            seq {
                while upRdr.Read() do
                    Map.getString "id" upRdr, inst (Map.getDateTime "updated_on" upRdr)
            } |> List.ofSeq
        upRdr.Close ()
        cmd.CommandText <- "UPDATE upload SET updated_on = @updatedOn WHERE id = @id"
        [ cmd.Parameters.Add("@updatedOn", SqliteType.Text)
          cmd.Parameters.Add("@id",        SqliteType.Text) ] |> ignore
        toUpdate
        |> List.iter (fun (upId, updatedOn) ->
            cmd.Parameters["@id"       ].Value <- upId
            cmd.Parameters["@updatedOn"].Value <- instantParam updatedOn
            let _ = cmd.ExecuteNonQuery()
            ())
        cmd.Parameters.Clear()
        // web_log_user.created_on, web_log_user.last_seen_on (opt)
        cmd.CommandText <- "SELECT id, created_on, last_seen_on FROM web_log_user"
        use! userRdr = cmd.ExecuteReaderAsync()
        let toUpdate =
            seq {
                while userRdr.Read() do
                    Map.getString "id" userRdr,
                    inst (Map.getDateTime "created_on" userRdr),
                    (Map.tryDateTime "last_seen_on" userRdr |> Option.map inst)
            } |> List.ofSeq
        userRdr.Close()
        cmd.CommandText <- "UPDATE web_log_user SET created_on = @createdOn, last_seen_on = @lastSeenOn WHERE id = @id"
        [ cmd.Parameters.Add("@id",         SqliteType.Text)
          cmd.Parameters.Add("@createdOn",  SqliteType.Text)
          cmd.Parameters.Add("@lastSeenOn", SqliteType.Text) ] |> ignore
        toUpdate
        |> List.iter (fun (userId, createdOn, lastSeenOn) ->
            cmd.Parameters["@id"        ].Value <- userId
            cmd.Parameters["@createdOn" ].Value <- instantParam createdOn
            cmd.Parameters["@lastSeenOn"].Value <- maybeInstant lastSeenOn
            let _ = cmd.ExecuteNonQuery()
            ())
        cmd.Parameters.Clear()
        
        conn.Close()
        conn.Open()
        
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
    
    /// Migrate from v2-rc2 to v2
    let migrateV2Rc2ToV2 () = backgroundTask {
        Utils.Migration.logStep log "v2-rc2 to v2" "Setting database version; no migration required"
        do! setDbVersion "v2"
    }

    /// Migrate from v2 to v2.1
    let migrateV2ToV2point1 () = backgroundTask {
        let! webLogs =
            Custom.list $"SELECT url_base, slug FROM {Table.WebLog}" [] (fun rdr -> rdr.GetString(0), rdr.GetString(1))
        Utils.Migration.backupAndRestoreRequired log "v2" "v2.1" webLogs
    }

    /// Migrate from v2.1 to v2.1.1
    let migrateV2ToV2point1point1 () = backgroundTask {
        Utils.Migration.logStep log "v2.1 to v2.1.1" "Setting database version; no migration required"
        do! setDbVersion "v2.1.1"
    }

    /// Migrate data among versions (up only)
    let migrate version = backgroundTask {
        let mutable v = defaultArg version ""

        if v = "v2-rc1" then
            do! migrateV2Rc1ToV2Rc2 ()
            v <- "v2-rc2"
        
        if v = "v2-rc2" then
            do! migrateV2Rc2ToV2 ()
            v <- "v2"
        
        if v = "v2" then
            do! migrateV2ToV2point1 ()
            v <- "v2.1"
        
        if v = "v2.1" then
            do! migrateV2ToV2point1point1 ()
            v <- "v2.1.1"
        
        if v <> Utils.Migration.currentDbVersion then
            log.LogWarning $"Unknown database version; assuming {Utils.Migration.currentDbVersion}"
            do! setDbVersion Utils.Migration.currentDbVersion
    }
    
    /// The connection for this instance
    member _.Conn = conn
    
    interface IData with
    
        member _.Category   = SQLiteCategoryData   (conn, ser, log)
        member _.Page       = SQLitePageData       (conn, log)
        member _.Post       = SQLitePostData       (conn, log)
        member _.TagMap     = SQLiteTagMapData     (conn, log)
        member _.Theme      = SQLiteThemeData      (conn, log)
        member _.ThemeAsset = SQLiteThemeAssetData (conn, log)
        member _.Upload     = SQLiteUploadData     (conn, log)
        member _.WebLog     = SQLiteWebLogData     (conn, log)
        member _.WebLogUser = SQLiteWebLogUserData (conn, log)
        
        member _.Serializer = ser
        
        member _.StartUp () = backgroundTask {
            do! ensureTables ()
            let! version = conn.customSingle<string> $"SELECT id FROM {Table.DbVersion}" [] _.GetString(0)
            do! migrate version
        }
