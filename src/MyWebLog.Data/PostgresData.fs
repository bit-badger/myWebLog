namespace MyWebLog.Data

open Microsoft.Extensions.Logging
open MyWebLog.Data.Postgres
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// Data implementation for PostgreSQL
type PostgresData (conn : NpgsqlConnection, log : ILogger<PostgresData>, ser : JsonSerializer) =

    interface IData with
        
        member _.Category   = PostgresCategoryData   conn
        member _.Page       = PostgresPageData       (conn, ser)
        member _.Post       = PostgresPostData       (conn, ser)
        member _.TagMap     = PostgresTagMapData     conn
        member _.Theme      = PostgresThemeData      conn
        member _.ThemeAsset = PostgresThemeAssetData conn
        member _.Upload     = PostgresUploadData     conn
        member _.WebLog     = PostgresWebLogData     (conn, ser)
        member _.WebLogUser = PostgresWebLogUserData conn
        
        member _.Serializer = ser
        
        member _.StartUp () = backgroundTask {
            
            let _ = NpgsqlConnection.GlobalTypeMapper.UseNodaTime ()
            
            let! tables =
                Sql.existingConnection conn
                |> Sql.query "SELECT tablename FROM pg_tables WHERE schemaname = 'public'"
                |> Sql.executeAsync (fun row -> row.string "tablename")
            let needsTable table = not (List.contains table tables)
            
            let sql = seq {
                // Theme tables
                if needsTable "theme" then
                    "CREATE TABLE theme (
                        id       TEXT NOT NULL PRIMARY KEY,
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
                        theme_id    TEXT        NOT NULL REFERENCES theme (id),
                        path        TEXT        NOT NULL,
                        updated_on  TIMESTAMPTZ NOT NULL,
                        data        BYTEA       NOT NULL,
                        PRIMARY KEY (theme_id, path))"
                
                // Web log tables
                if needsTable "web_log" then
                    "CREATE TABLE web_log (
                        id                   TEXT    NOT NULL PRIMARY KEY,
                        name                 TEXT    NOT NULL,
                        slug                 TEXT    NOT NULL,
                        subtitle             TEXT,
                        default_page         TEXT    NOT NULL,
                        posts_per_page       INTEGER NOT NULL,
                        theme_id             TEXT    NOT NULL REFERENCES theme (id),
                        url_base             TEXT    NOT NULL,
                        time_zone            TEXT    NOT NULL,
                        auto_htmx            BOOLEAN NOT NULL DEFAULT FALSE,
                        uploads              TEXT    NOT NULL,
                        is_feed_enabled      BOOLEAN NOT NULL DEFAULT FALSE,
                        feed_name            TEXT    NOT NULL,
                        items_in_feed        INTEGER,
                        is_category_enabled  BOOLEAN NOT NULL DEFAULT FALSE,
                        is_tag_enabled       BOOLEAN NOT NULL DEFAULT FALSE,
                        copyright            TEXT)"
                    "CREATE INDEX web_log_theme_idx ON web_log (theme_id)"
                if needsTable "web_log_feed" then
                    "CREATE TABLE web_log_feed (
                        id          TEXT NOT NULL PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        source      TEXT NOT NULL,
                        path        TEXT NOT NULL,
                        podcast     JSONB)"
                    "CREATE INDEX web_log_feed_web_log_idx ON web_log_feed (web_log_id)"
                
                // Category table
                if needsTable "category" then
                    "CREATE TABLE category (
                        id           TEXT NOT NULL PRIMARY KEY,
                        web_log_id   TEXT NOT NULL REFERENCES web_log (id),
                        name         TEXT NOT NULL,
                        slug         TEXT NOT NULL,
                        description  TEXT,
                        parent_id    TEXT)"
                    "CREATE INDEX category_web_log_idx ON category (web_log_id)"
                
                // Web log user table
                if needsTable "web_log_user" then
                    "CREATE TABLE web_log_user (
                        id              TEXT        NOT NULL PRIMARY KEY,
                        web_log_id      TEXT        NOT NULL REFERENCES web_log (id),
                        email           TEXT        NOT NULL,
                        first_name      TEXT        NOT NULL,
                        last_name       TEXT        NOT NULL,
                        preferred_name  TEXT        NOT NULL,
                        password_hash   TEXT        NOT NULL,
                        salt            UUID        NOT NULL,
                        url             TEXT,
                        access_level    TEXT        NOT NULL,
                        created_on      TIMESTAMPTZ NOT NULL,
                        last_seen_on    TIMESTAMPTZ)"
                    "CREATE INDEX web_log_user_web_log_idx ON web_log_user (web_log_id)"
                    "CREATE INDEX web_log_user_email_idx   ON web_log_user (web_log_id, email)"
                
                // Page tables
                if needsTable "page" then
                    "CREATE TABLE page (
                        id               TEXT        NOT NULL PRIMARY KEY,
                        web_log_id       TEXT        NOT NULL REFERENCES web_log (id),
                        author_id        TEXT        NOT NULL REFERENCES web_log_user (id),
                        title            TEXT        NOT NULL,
                        permalink        TEXT        NOT NULL,
                        prior_permalinks TEXT[]      NOT NULL DEFAULT '{}',
                        published_on     TIMESTAMPTZ NOT NULL,
                        updated_on       TIMESTAMPTZ NOT NULL,
                        is_in_page_list  BOOLEAN     NOT NULL DEFAULT FALSE,
                        template         TEXT,
                        page_text        TEXT        NOT NULL,
                        meta_items       JSONB)"
                    "CREATE INDEX page_web_log_idx   ON page (web_log_id)"
                    "CREATE INDEX page_author_idx    ON page (author_id)"
                    "CREATE INDEX page_permalink_idx ON page (web_log_id, permalink)"
                if needsTable "page_revision" then
                    "CREATE TABLE page_revision (
                        page_id        TEXT        NOT NULL REFERENCES page (id),
                        as_of          TIMESTAMPTZ NOT NULL,
                        revision_text  TEXT        NOT NULL,
                        PRIMARY KEY (page_id, as_of))"
                
                // Post tables
                if needsTable "post" then
                    "CREATE TABLE post (
                        id               TEXT        NOT NULL PRIMARY KEY,
                        web_log_id       TEXT        NOT NULL REFERENCES web_log (id),
                        author_id        TEXT        NOT NULL REFERENCES web_log_user (id),
                        status           TEXT        NOT NULL,
                        title            TEXT        NOT NULL,
                        permalink        TEXT        NOT NULL,
                        prior_permalinks TEXT[]      NOT NULL DEFAULT '{}',
                        published_on     TIMESTAMPTZ,
                        updated_on       TIMESTAMPTZ NOT NULL,
                        template         TEXT,
                        post_text        TEXT        NOT NULL,
                        tags             TEXT[],
                        meta_items       JSONB,
                        episode          JSONB)"
                    "CREATE INDEX post_web_log_idx   ON post (web_log_id)"
                    "CREATE INDEX post_author_idx    ON post (author_id)"
                    "CREATE INDEX post_status_idx    ON post (web_log_id, status, updated_on)"
                    "CREATE INDEX post_permalink_idx ON post (web_log_id, permalink)"
                if needsTable "post_category" then
                    "CREATE TABLE post_category (
                        post_id      TEXT NOT NULL REFERENCES post (id),
                        category_id  TEXT NOT NULL REFERENCES category (id),
                        PRIMARY KEY (post_id, category_id))"
                    "CREATE INDEX post_category_category_idx ON post_category (category_id)"
                if needsTable "post_revision" then
                    "CREATE TABLE post_revision (
                        post_id        TEXT        NOT NULL REFERENCES post (id),
                        as_of          TIMESTAMPTZ NOT NULL,
                        revision_text  TEXT        NOT NULL,
                        PRIMARY KEY (post_id, as_of))"
                if needsTable "post_comment" then
                    "CREATE TABLE post_comment (
                        id              TEXT        NOT NULL PRIMARY KEY,
                        post_id         TEXT        NOT NULL REFERENCES post(id),
                        in_reply_to_id  TEXT,
                        name            TEXT        NOT NULL,
                        email           TEXT        NOT NULL,
                        url             TEXT,
                        status          TEXT        NOT NULL,
                        posted_on       TIMESTAMPTZ NOT NULL,
                        comment_text    TEXT        NOT NULL)"
                    "CREATE INDEX post_comment_post_idx ON post_comment (post_id)"
                
                // Tag map table
                if needsTable "tag_map" then
                    "CREATE TABLE tag_map (
                        id          TEXT NOT NULL PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        tag         TEXT NOT NULL,
                        url_value   TEXT NOT NULL)"
                    "CREATE INDEX tag_map_web_log_idx ON tag_map (web_log_id)"
                
                // Uploaded file table
                if needsTable "upload" then
                    "CREATE TABLE upload (
                        id          TEXT        NOT NULL PRIMARY KEY,
                        web_log_id  TEXT        NOT NULL REFERENCES web_log (id),
                        path        TEXT        NOT NULL,
                        updated_on  TIMESTAMPTZ NOT NULL,
                        data        BYTEA       NOT NULL)"
                    "CREATE INDEX upload_web_log_idx ON upload (web_log_id)"
                    "CREATE INDEX upload_path_idx    ON upload (web_log_id, path)"
            }
            
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync
                (sql
                 |> Seq.map (fun s ->
                    let parts = s.Split ' '
                    log.LogInformation $"Creating {parts[2]} {parts[1].ToLower()}..."
                    s, [ [] ])
                 |> List.ofSeq)
            |> Async.AwaitTask
            |> Async.RunSynchronously
            |> ignore
        }
