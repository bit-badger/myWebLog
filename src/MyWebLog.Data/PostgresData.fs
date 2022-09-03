namespace MyWebLog.Data

open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data.Postgres
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// Data implementation for PostgreSQL
type PostgresData (conn : NpgsqlConnection, log : ILogger<PostgresData>, ser : JsonSerializer) =
    
    /// Create any needed tables
    let ensureTables () = backgroundTask {
        let _ = NpgsqlConnection.GlobalTypeMapper.UseNodaTime ()
        
        let! tables =
            Sql.existingConnection conn
            |> Sql.query "SELECT tablename FROM pg_tables WHERE schemaname = 'public'"
            |> Sql.executeAsync (fun row -> row.string "tablename")
        let needsTable table = not (List.contains table tables)
        // Create a document table
        let docTable table = $"CREATE TABLE %s{table} (id TEXT NOT NULL PRIMARY KEY, data JSONB NOT NULL)"
        let mutable isNew = false
        
        let sql = seq {
            // Theme tables
            if needsTable Table.Theme then
                isNew <- true
                docTable Table.Theme
            if needsTable Table.ThemeAsset then
                $"CREATE TABLE {Table.ThemeAsset} (
                    theme_id    TEXT        NOT NULL REFERENCES {Table.Theme} (id) ON DELETE CASCADE,
                    path        TEXT        NOT NULL,
                    updated_on  TIMESTAMPTZ NOT NULL,
                    data        BYTEA       NOT NULL,
                    PRIMARY KEY (theme_id, path))"
            
            // Web log table
            if needsTable Table.WebLog then
                docTable Table.WebLog
                $"CREATE INDEX web_log_theme_idx ON {Table.WebLog} (data ->> '{nameof WebLog.empty.ThemeId}')"
            
            // Category table
            if needsTable Table.Category then
                docTable Table.Category
                $"CREATE INDEX category_web_log_idx ON {Table.Category} (data ->> '{nameof Category.empty.WebLogId}')"
            
            // Web log user table
            if needsTable Table.WebLogUser then
                docTable Table.WebLogUser
                $"CREATE INDEX web_log_user_web_log_idx ON {Table.WebLogUser}
                    (data ->> '{nameof WebLogUser.empty.WebLogId}')"
                $"CREATE INDEX web_log_user_email_idx   ON {Table.WebLogUser}
                    (data ->> '{nameof WebLogUser.empty.WebLogId}', data ->> '{nameof WebLogUser.empty.Email}')"
            
            // Page tables
            if needsTable Table.Page then
                docTable Table.Page
                $"CREATE INDEX page_web_log_idx   ON {Table.Page} (data ->> '{nameof Page.empty.WebLogId}')"
                $"CREATE INDEX page_author_idx    ON {Table.Page} (data ->> '{nameof Page.empty.AuthorId}')"
                $"CREATE INDEX page_permalink_idx ON {Table.Page}
                    (data ->> '{nameof Page.empty.WebLogId}', data ->> '{nameof Page.empty.Permalink}')"
            if needsTable Table.PageRevision then
                $"CREATE TABLE {Table.PageRevision} (
                    page_id        TEXT        NOT NULL REFERENCES {Table.Page} (id) ON DELETE CASCADE,
                    as_of          TIMESTAMPTZ NOT NULL,
                    revision_text  TEXT        NOT NULL,
                    PRIMARY KEY (page_id, as_of))"
            
            // Post tables
            if needsTable Table.Post then
                docTable Table.Post
                $"CREATE INDEX post_web_log_idx   ON {Table.Post} (data ->> '{nameof Post.empty.WebLogId}')"
                $"CREATE INDEX post_author_idx    ON {Table.Post} (data ->> '{nameof Post.empty.AuthorId}')"
                $"CREATE INDEX post_status_idx    ON {Table.Post}
                    (data ->> '{nameof Post.empty.WebLogId}', data ->> '{nameof Post.empty.Status}',
                     data ->> '{nameof Post.empty.UpdatedOn}')"
                $"CREATE INDEX post_permalink_idx ON {Table.Post}
                    (data ->> '{nameof Post.empty.WebLogId}', data ->> '{nameof Post.empty.Permalink}')"
                $"CREATE INDEX post_category_idx  ON {Table.Post} USING GIN
                    (data ->> '{nameof Post.empty.CategoryIds}')"
                $"CREATE INDEX post_tag_idx       ON {Table.Post} USING GIN (data ->> '{nameof Post.empty.Tags}')"
            if needsTable Table.PostRevision then
                $"CREATE TABLE {Table.PostRevision} (
                    post_id        TEXT        NOT NULL REFERENCES {Table.Post} (id) ON DELETE CASCADE,
                    as_of          TIMESTAMPTZ NOT NULL,
                    revision_text  TEXT        NOT NULL,
                    PRIMARY KEY (post_id, as_of))"
            if needsTable Table.PostComment then
                docTable Table.PostComment
                $"CREATE INDEX post_comment_post_idx ON {Table.PostComment} (data ->> '{nameof Comment.empty.PostId}')"
            
            // Tag map table
            if needsTable Table.TagMap then
                docTable Table.TagMap
                $"CREATE INDEX tag_map_web_log_idx ON {Table.TagMap} (data ->> '{nameof TagMap.empty.WebLogId}')"
            
            // Uploaded file table
            if needsTable Table.Upload then
                $"CREATE TABLE {Table.Upload} (
                    id          TEXT        NOT NULL PRIMARY KEY,
                    web_log_id  TEXT        NOT NULL REFERENCES {Table.WebLog} (id),
                    path        TEXT        NOT NULL,
                    updated_on  TIMESTAMPTZ NOT NULL,
                    data        BYTEA       NOT NULL)"
                $"CREATE INDEX upload_web_log_idx ON {Table.Upload} (web_log_id)"
                $"CREATE INDEX upload_path_idx    ON {Table.Upload} (web_log_id, path)"
            
            // Database version table
            if needsTable Table.DbVersion then
                $"CREATE TABLE {Table.DbVersion} (id TEXT NOT NULL PRIMARY KEY)"
                $"INSERT INTO {Table.DbVersion} VALUES ('{Utils.currentDbVersion}')"
        }
        
        Sql.existingConnection conn
        |> Sql.executeTransactionAsync
            (sql
             |> Seq.map (fun s ->
                let parts = s.Split ' '
                if parts[1].ToLowerInvariant () = "table" then
                    log.LogInformation $"Creating {parts[2]} table..."
                s, [ [] ])
             |> List.ofSeq)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    }
    
    /// Set a specific database version
    let setDbVersion version = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"DELETE FROM db_version; INSERT INTO db_version VALUES ('%s{version}')"
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Do required data migration between versions
    let migrate version = backgroundTask {
        match version with
        | Some "v2-rc2" -> ()
        // Future versions will be inserted here
        | Some _
        | None ->
            log.LogWarning $"Unknown database version; assuming {Utils.currentDbVersion}"
            do! setDbVersion Utils.currentDbVersion
    }
        
    interface IData with
        
        member _.Category   = PostgresCategoryData   (conn, ser)
        member _.Page       = PostgresPageData       (conn, ser)
        member _.Post       = PostgresPostData       (conn, ser)
        member _.TagMap     = PostgresTagMapData     (conn, ser)
        member _.Theme      = PostgresThemeData      (conn, ser)
        member _.ThemeAsset = PostgresThemeAssetData conn
        member _.Upload     = PostgresUploadData     conn
        member _.WebLog     = PostgresWebLogData     (conn, ser)
        member _.WebLogUser = PostgresWebLogUserData (conn, ser)
        
        member _.Serializer = ser
        
        member _.StartUp () = backgroundTask {
            do! ensureTables ()
            
            let! version =
                Sql.existingConnection conn
                |> Sql.query "SELECT id FROM db_version"
                |> Sql.executeAsync (fun row -> row.string "id")
                |> tryHead
            match version with
            | Some v when v = Utils.currentDbVersion -> ()
            | Some _
            | None -> do! migrate version 
        }
