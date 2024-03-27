namespace MyWebLog.Data

open BitBadger.Documents
open BitBadger.Documents.Postgres
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data.Postgres
open Newtonsoft.Json
open Npgsql.FSharp

/// Data implementation for PostgreSQL
type PostgresData(log: ILogger<PostgresData>, ser: JsonSerializer) =
    
    /// Create any needed tables
    let ensureTables () = backgroundTask {
        // Set up the PostgreSQL document store
        Configuration.useSerializer (Utils.createDocumentSerializer ser)
        
        let! tables =
            Custom.list
                "SELECT tablename FROM pg_tables WHERE schemaname = 'public'" [] (fun row -> row.string "tablename")
        let needsTable table = not (List.contains table tables)
        
        let sql = seq {
            // Theme tables
            if needsTable Table.Theme then
                Query.Definition.ensureTable Table.Theme
                Query.Definition.ensureKey   Table.Theme
            if needsTable Table.ThemeAsset then
                $"CREATE TABLE {Table.ThemeAsset} (
                    theme_id    TEXT        NOT NULL,
                    path        TEXT        NOT NULL,
                    updated_on  TIMESTAMPTZ NOT NULL,
                    data        BYTEA       NOT NULL,
                    PRIMARY KEY (theme_id, path))"
            
            // Web log table
            if needsTable Table.WebLog then
                Query.Definition.ensureTable         Table.WebLog
                Query.Definition.ensureKey           Table.WebLog
                Query.Definition.ensureDocumentIndex Table.WebLog Optimized
            
            // Category table
            if needsTable Table.Category then
                Query.Definition.ensureTable         Table.Category
                Query.Definition.ensureKey           Table.Category
                Query.Definition.ensureDocumentIndex Table.Category Optimized
            
            // Web log user table
            if needsTable Table.WebLogUser then
                Query.Definition.ensureTable         Table.WebLogUser
                Query.Definition.ensureKey           Table.WebLogUser
                Query.Definition.ensureDocumentIndex Table.WebLogUser Optimized
            
            // Page tables
            if needsTable Table.Page then
                Query.Definition.ensureTable   Table.Page
                Query.Definition.ensureKey     Table.Page
                Query.Definition.ensureIndexOn Table.Page "author" [ nameof Page.Empty.AuthorId ]
                Query.Definition.ensureIndexOn
                    Table.Page "permalink" [ nameof Page.Empty.WebLogId; nameof Page.Empty.Permalink ]
            if needsTable Table.PageRevision then
                $"CREATE TABLE {Table.PageRevision} (
                    page_id        TEXT        NOT NULL,
                    as_of          TIMESTAMPTZ NOT NULL,
                    revision_text  TEXT        NOT NULL,
                    PRIMARY KEY (page_id, as_of))"
            
            // Post tables
            if needsTable Table.Post then
                Query.Definition.ensureTable   Table.Post
                Query.Definition.ensureKey     Table.Post
                Query.Definition.ensureIndexOn Table.Post "author" [ nameof Post.Empty.AuthorId ]
                Query.Definition.ensureIndexOn
                    Table.Post "permalink" [ nameof Post.Empty.WebLogId; nameof Post.Empty.Permalink ]
                Query.Definition.ensureIndexOn
                    Table.Post
                    "status"
                    [ nameof Post.Empty.WebLogId; nameof Post.Empty.Status; nameof Post.Empty.UpdatedOn ]
                $"CREATE INDEX idx_post_category ON {Table.Post} USING GIN ((data['{nameof Post.Empty.CategoryIds}']))"
                $"CREATE INDEX idx_post_tag      ON {Table.Post} USING GIN ((data['{nameof Post.Empty.Tags}']))"
            if needsTable Table.PostRevision then
                $"CREATE TABLE {Table.PostRevision} (
                    post_id        TEXT        NOT NULL,
                    as_of          TIMESTAMPTZ NOT NULL,
                    revision_text  TEXT        NOT NULL,
                    PRIMARY KEY (post_id, as_of))"
            if needsTable Table.PostComment then
                Query.Definition.ensureTable   Table.PostComment
                Query.Definition.ensureKey     Table.PostComment
                Query.Definition.ensureIndexOn Table.PostComment "post" [ nameof Comment.Empty.PostId ]
            
            // Tag map table
            if needsTable Table.TagMap then
                Query.Definition.ensureTable         Table.TagMap
                Query.Definition.ensureKey           Table.TagMap
                Query.Definition.ensureDocumentIndex Table.TagMap Optimized
            
            // Uploaded file table
            if needsTable Table.Upload then
                $"CREATE TABLE {Table.Upload} (
                    id          TEXT        NOT NULL PRIMARY KEY,
                    web_log_id  TEXT        NOT NULL,
                    path        TEXT        NOT NULL,
                    updated_on  TIMESTAMPTZ NOT NULL,
                    data        BYTEA       NOT NULL)"
                $"CREATE INDEX idx_upload_web_log ON {Table.Upload} (web_log_id)"
                $"CREATE INDEX idx_upload_path    ON {Table.Upload} (web_log_id, path)"
            
            // Database version table
            if needsTable Table.DbVersion then
                $"CREATE TABLE {Table.DbVersion} (id TEXT NOT NULL PRIMARY KEY)"
                $"INSERT INTO {Table.DbVersion} VALUES ('{Utils.Migration.currentDbVersion}')"
        }
        
        Configuration.dataSource ()
        |> Sql.fromDataSource
        |> Sql.executeTransactionAsync
            (sql
             |> Seq.map (fun s ->
                let parts = s.Replace(" IF NOT EXISTS", "", System.StringComparison.OrdinalIgnoreCase).Split ' '
                if parts[1].ToLowerInvariant() = "table" then
                    log.LogInformation $"Creating {parts[2]} table..."
                s, [ [] ])
             |> List.ofSeq)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
    }
    
    /// Set a specific database version
    let setDbVersion version =
        Custom.nonQuery $"DELETE FROM db_version; INSERT INTO db_version VALUES ('%s{version}')" []
    
    /// Migrate from v2-rc2 to v2 (manual migration required)
    let migrateV2Rc2ToV2 () = backgroundTask {
        let! webLogs =
            Custom.list
                $"SELECT url_base, slug FROM {Table.WebLog}" [] (fun row -> row.string "url_base", row.string "slug")
        Utils.Migration.backupAndRestoreRequired log "v2-rc2" "v2" webLogs
    }

    /// Migrate from v2 to v2.1
    let migrateV2ToV2point1 () = backgroundTask {
        let migration = "v2 to v2.1"
        Utils.Migration.logStep log migration "Adding empty redirect rule set to all weblogs"
        do! Custom.nonQuery $"""UPDATE {Table.WebLog} SET data = data + '{{ "RedirectRules": [] }}'::json""" []

        let tables =
            [ Table.Category; Table.Page; Table.Post; Table.PostComment; Table.TagMap; Table.Theme; Table.WebLog
              Table.WebLogUser ]
        
        Utils.Migration.logStep log migration "Adding unique indexes on ID fields"
        do! Custom.nonQuery (tables |> List.map Query.Definition.ensureKey |> String.concat "; ") []
        
        Utils.Migration.logStep log migration "Dropping old ID columns"
        do! Custom.nonQuery (tables |> List.map (sprintf "ALTER TABLE %s DROP COLUMN id") |> String.concat "; ") []
        
        Utils.Migration.logStep log migration "Adjusting indexes"
        let toDrop = [ "page_web_log_idx"; "post_web_log_idx" ]
        do! Custom.nonQuery (toDrop |> List.map (sprintf "DROP INDEX %s") |> String.concat "; ") []
        
        let toRename =
            [ "idx_category",          "idx_category_document"
              "idx_tag_map",           "idx_tag_map_document"
              "idx_web_log",           "idx_web_log_document"
              "idx_web_log_user",      "idx_web_log_user_document"
              "page_author_idx",       "idx_page_author"
              "page_permalink_idx",    "idx_page_permalink"
              "post_author_idx",       "idx_post_author"
              "post_status_idx",       "idx_post_status"
              "post_permalink_idx",    "idx_post_permalink"
              "post_category_idx",     "idx_post_category"
              "post_tag_idx",          "idx_post_tag"
              "post_comment_post_idx", "idx_post_comment_post"
              "upload_web_log_idx",    "idx_upload_web_log"
              "upload_path_idx",       "idx_upload_path" ]
        do! Custom.nonQuery
                (toRename
                 |> List.map (fun (oldName, newName) -> $"ALTER INDEX {oldName} RENAME TO {newName}")
                 |> String.concat "; ")
                []
        
        Utils.Migration.logStep log migration "Setting database to version 2.1"
        do! setDbVersion "v2.1"
    }

    /// Do required data migration between versions
    let migrate version = backgroundTask {
        let mutable v = defaultArg version ""

        if v = "v2-rc2" then 
            do! migrateV2Rc2ToV2 ()
            v <- "v2"
        
        if v = "v2" then
            do! migrateV2ToV2point1 ()
            v <- "v2.1"
        
        if v <> Utils.Migration.currentDbVersion then
            log.LogWarning $"Unknown database version; assuming {Utils.Migration.currentDbVersion}"
            do! setDbVersion Utils.Migration.currentDbVersion
    }
        
    interface IData with
        
        member _.Category   = PostgresCategoryData   log
        member _.Page       = PostgresPageData       log
        member _.Post       = PostgresPostData       log
        member _.TagMap     = PostgresTagMapData     log
        member _.Theme      = PostgresThemeData      log
        member _.ThemeAsset = PostgresThemeAssetData log
        member _.Upload     = PostgresUploadData     log
        member _.WebLog     = PostgresWebLogData     log
        member _.WebLogUser = PostgresWebLogUserData log
        
        member _.Serializer = ser
        
        member _.StartUp () = backgroundTask {
            log.LogTrace "PostgresData.StartUp"
            do! ensureTables ()
            
            let! version = Custom.single "SELECT id FROM db_version" [] (fun row -> row.string "id")
            do! migrate version
        }
