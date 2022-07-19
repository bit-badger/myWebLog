namespace MyWebLog.Data

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog.Data.SQLite

/// SQLite myWebLog data implementation        
type SQLiteData (conn : SqliteConnection, log : ILogger<SQLiteData>) =
    
    /// Determine if the given table exists
    let tableExists (table : string) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table"
        cmd.Parameters.AddWithValue ("@table", table) |> ignore
        let! count = count cmd
        return count = 1
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
        member _.Page       = SQLitePageData       conn
        member _.Post       = SQLitePostData       conn
        member _.TagMap     = SQLiteTagMapData     conn
        member _.Theme      = SQLiteThemeData      conn
        member _.ThemeAsset = SQLiteThemeAssetData conn
        member _.Upload     = SQLiteUploadData     conn
        member _.WebLog     = SQLiteWebLogData     conn
        member _.WebLogUser = SQLiteWebLogUserData conn
        
        member _.StartUp () = backgroundTask {

            use cmd = conn.CreateCommand ()
            
            // Theme tables
            match! tableExists "theme" with
            | true -> ()
            | false ->
                log.LogInformation "Creating theme table..."
                cmd.CommandText <- """
                    CREATE TABLE theme (
                        id       TEXT PRIMARY KEY,
                        name     TEXT NOT NULL,
                        version  TEXT NOT NULL)"""
                do! write cmd
            match! tableExists "theme_template" with
            | true -> ()
            | false ->
                log.LogInformation "Creating theme_template table..."
                cmd.CommandText <- """
                    CREATE TABLE theme_template (
                        theme_id  TEXT NOT NULL REFERENCES theme (id),
                        name      TEXT NOT NULL,
                        template  TEXT NOT NULL,
                        PRIMARY KEY (theme_id, name))"""
                do! write cmd
            match! tableExists "theme_asset" with
            | true -> ()
            | false ->
                log.LogInformation "Creating theme_asset table..."
                cmd.CommandText <- """
                    CREATE TABLE theme_asset (
                        theme_id    TEXT NOT NULL REFERENCES theme (id),
                        path        TEXT NOT NULL,
                        updated_on  TEXT NOT NULL,
                        data        BLOB NOT NULL,
                        PRIMARY KEY (theme_id, path))"""
                do! write cmd
            
            // Web log tables
            match! tableExists "web_log" with
            | true -> ()
            | false ->
                log.LogInformation "Creating web_log table..."
                cmd.CommandText <- """
                    CREATE TABLE web_log (
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
                    CREATE INDEX web_log_theme_idx ON web_log (theme_id)"""
                do! write cmd
            match! tableExists "web_log_feed" with
            | true -> ()
            | false ->
                log.LogInformation "Creating web_log_feed table..."
                cmd.CommandText <- """
                    CREATE TABLE web_log_feed (
                        id          TEXT PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        source      TEXT NOT NULL,
                        path        TEXT NOT NULL);
                    CREATE INDEX web_log_feed_web_log_idx ON web_log_feed (web_log_id)"""
                do! write cmd
            match! tableExists "web_log_feed_podcast" with
            | true -> ()
            | false ->
                log.LogInformation "Creating web_log_feed_podcast table..."
                cmd.CommandText <- """
                    CREATE TABLE web_log_feed_podcast (
                        feed_id             TEXT PRIMARY KEY REFERENCES web_log_feed (id),
                        title               TEXT NOT NULL,
                        subtitle            TEXT,
                        items_in_feed       INTEGER NOT NULL,
                        summary             TEXT NOT NULL,
                        displayed_author    TEXT NOT NULL,
                        email               TEXT NOT NULL,
                        image_url           TEXT NOT NULL,
                        apple_category      TEXT NOT NULL,
                        apple_subcategory   TEXT,
                        explicit            TEXT NOT NULL,
                        default_media_type  TEXT,
                        media_base_url      TEXT,
                        podcast_guid        TEXT,
                        funding_url         TEXT,
                        funding_text        TEXT,
                        medium              TEXT)"""
                do! write cmd
            
            // Category table
            match! tableExists "category" with
            | true -> ()
            | false ->
                log.LogInformation "Creating category table..."
                cmd.CommandText <- """
                    CREATE TABLE category (
                        id           TEXT PRIMARY KEY,
                        web_log_id   TEXT NOT NULL REFERENCES web_log (id),
                        name         TEXT NOT NULL,
                        slug         TEXT NOT NULL,
                        description  TEXT,
                        parent_id    TEXT);
                    CREATE INDEX category_web_log_idx ON category (web_log_id)"""
                do! write cmd
            
            // Web log user table
            match! tableExists "web_log_user" with
            | true -> ()
            | false ->
                log.LogInformation "Creating web_log_user table..."
                cmd.CommandText <- """
                    CREATE TABLE web_log_user (
                        id              TEXT PRIMARY KEY,
                        web_log_id      TEXT NOT NULL REFERENCES web_log (id),
                        email           TEXT NOT NULL,
                        first_name      TEXT NOT NULL,
                        last_name       TEXT NOT NULL,
                        preferred_name  TEXT NOT NULL,
                        password_hash   TEXT NOT NULL,
                        salt            TEXT NOT NULL,
                        url             TEXT,
                        access_level    TEXT NOT NULL,
                        created_on      TEXT NOT NULL,
                        last_seen_on    TEXT);
                    CREATE INDEX web_log_user_web_log_idx ON web_log_user (web_log_id);
                    CREATE INDEX web_log_user_email_idx   ON web_log_user (web_log_id, email)"""
                do! write cmd
            
            // Page tables
            match! tableExists "page" with
            | true -> ()
            | false ->
                log.LogInformation "Creating page table..."
                cmd.CommandText <- """
                    CREATE TABLE page (
                        id               TEXT PRIMARY KEY,
                        web_log_id       TEXT NOT NULL REFERENCES web_log (id),
                        author_id        TEXT NOT NULL REFERENCES web_log_user (id),
                        title            TEXT NOT NULL,
                        permalink        TEXT NOT NULL,
                        published_on     TEXT NOT NULL,
                        updated_on       TEXT NOT NULL,
                        is_in_page_list  INTEGER NOT NULL DEFAULT 0,
                        template         TEXT,
                        page_text        TEXT NOT NULL);
                    CREATE INDEX page_web_log_idx   ON page (web_log_id);
                    CREATE INDEX page_author_idx    ON page (author_id);
                    CREATE INDEX page_permalink_idx ON page (web_log_id, permalink)"""
                do! write cmd
            match! tableExists "page_meta" with
            | true -> ()
            | false ->
                log.LogInformation "Creating page_meta table..."
                cmd.CommandText <- """
                    CREATE TABLE page_meta (
                        page_id  TEXT NOT NULL REFERENCES page (id),
                        name     TEXT NOT NULL,
                        value    TEXT NOT NULL,
                        PRIMARY KEY (page_id, name, value))"""
                do! write cmd
            match! tableExists "page_permalink" with
            | true -> ()
            | false ->
                log.LogInformation "Creating page_permalink table..."
                cmd.CommandText <- """
                    CREATE TABLE page_permalink (
                        page_id    TEXT NOT NULL REFERENCES page (id),
                        permalink  TEXT NOT NULL,
                        PRIMARY KEY (page_id, permalink))"""
                do! write cmd
            match! tableExists "page_revision" with
            | true -> ()
            | false ->
                log.LogInformation "Creating page_revision table..."
                cmd.CommandText <- """
                    CREATE TABLE page_revision (
                        page_id        TEXT NOT NULL REFERENCES page (id),
                        as_of          TEXT NOT NULL,
                        revision_text  TEXT NOT NULL,
                        PRIMARY KEY (page_id, as_of))"""
                do! write cmd
            
            // Post tables
            match! tableExists "post" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post table..."
                cmd.CommandText <- """
                    CREATE TABLE post (
                        id            TEXT PRIMARY KEY,
                        web_log_id    TEXT NOT NULL REFERENCES web_log (id),
                        author_id     TEXT NOT NULL REFERENCES web_log_user (id),
                        status        TEXT NOT NULL,
                        title         TEXT NOT NULL,
                        permalink     TEXT NOT NULL,
                        published_on  TEXT,
                        updated_on    TEXT NOT NULL,
                        template      TEXT,
                        post_text     TEXT NOT NULL);
                    CREATE INDEX post_web_log_idx   ON post (web_log_id);
                    CREATE INDEX post_author_idx    ON post (author_id);
                    CREATE INDEX post_status_idx    ON post (web_log_id, status, updated_on);
                    CREATE INDEX post_permalink_idx ON post (web_log_id, permalink)"""
                do! write cmd
            match! tableExists "post_category" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post_category table..."
                cmd.CommandText <- """
                    CREATE TABLE post_category (
                        post_id      TEXT NOT NULL REFERENCES post (id),
                        category_id  TEXT NOT NULL REFERENCES category (id),
                        PRIMARY KEY (post_id, category_id));
                    CREATE INDEX post_category_category_idx ON post_category (category_id)"""
                do! write cmd
            match! tableExists "post_episode" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post_episode table..."
                cmd.CommandText <- """
                    CREATE TABLE post_episode (
                        post_id              TEXT PRIMARY KEY REFERENCES post(id),
                        media                TEXT NOT NULL,
                        length               INTEGER NOT NULL,
                        duration             TEXT,
                        media_type           TEXT,
                        image_url            TEXT,
                        subtitle             TEXT,
                        explicit             TEXT,
                        chapter_file         TEXT,
                        chapter_type         TEXT,
                        transcript_url       TEXT,
                        transcript_type      TEXT,
                        transcript_lang      TEXT,
                        transcript_captions  INTEGER,
                        season_number        INTEGER,
                        season_description   TEXT,
                        episode_number       TEXT,
                        episode_description  TEXT)"""
                do! write cmd
            match! tableExists "post_tag" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post_tag table..."
                cmd.CommandText <- """
                    CREATE TABLE post_tag (
                        post_id  TEXT NOT NULL REFERENCES post (id),
                        tag      TEXT NOT NULL,
                        PRIMARY KEY (post_id, tag))"""
                do! write cmd
            match! tableExists "post_meta" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post_meta table..."
                cmd.CommandText <- """
                    CREATE TABLE post_meta (
                        post_id  TEXT NOT NULL REFERENCES post (id),
                        name     TEXT NOT NULL,
                        value    TEXT NOT NULL,
                        PRIMARY KEY (post_id, name, value))"""
                do! write cmd
            match! tableExists "post_permalink" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post_permalink table..."
                cmd.CommandText <- """
                    CREATE TABLE post_permalink (
                        post_id    TEXT NOT NULL REFERENCES post (id),
                        permalink  TEXT NOT NULL,
                        PRIMARY KEY (post_id, permalink))"""
                do! write cmd
            match! tableExists "post_revision" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post_revision table..."
                cmd.CommandText <- """
                    CREATE TABLE post_revision (
                        post_id        TEXT NOT NULL REFERENCES post (id),
                        as_of          TEXT NOT NULL,
                        revision_text  TEXT NOT NULL,
                        PRIMARY KEY (post_id, as_of))"""
                do! write cmd
            match! tableExists "post_comment" with
            | true -> ()
            | false ->
                log.LogInformation "Creating post_comment table..."
                cmd.CommandText <- """
                    CREATE TABLE post_comment (
                        id              TEXT PRIMARY KEY,
                        post_id         TEXT NOT NULL REFERENCES post(id),
                        in_reply_to_id  TEXT,
                        name            TEXT NOT NULL,
                        email           TEXT NOT NULL,
                        url             TEXT,
                        status          TEXT NOT NULL,
                        posted_on       TEXT NOT NULL,
                        comment_text    TEXT NOT NULL);
                    CREATE INDEX post_comment_post_idx ON post_comment (post_id)"""
                do! write cmd
            
            // Tag map table
            match! tableExists "tag_map" with
            | true -> ()
            | false ->
                log.LogInformation "Creating tag_map table..."
                cmd.CommandText <- """
                    CREATE TABLE tag_map (
                        id          TEXT PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        tag         TEXT NOT NULL,
                        url_value   TEXT NOT NULL);
                    CREATE INDEX tag_map_web_log_idx ON tag_map (web_log_id)"""
                do! write cmd
            
            // Uploaded file table
            match! tableExists "upload" with
            | true -> ()
            | false ->
                log.LogInformation "Creating upload table..."
                cmd.CommandText <- """
                    CREATE TABLE upload (
                        id          TEXT PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        path        TEXT NOT NULL,
                        updated_on  TEXT NOT NULL,
                        data        BLOB NOT NULL);
                    CREATE INDEX upload_web_log_idx ON upload (web_log_id);
                    CREATE INDEX upload_path_idx    ON upload (web_log_id, path)"""
                do! write cmd
        }
