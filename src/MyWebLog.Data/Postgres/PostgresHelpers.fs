/// Helper functions for the PostgreSQL data implementation
[<AutoOpen>]
module MyWebLog.Data.Postgres.PostgresHelpers

open System
open System.Threading.Tasks
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open NodaTime
open Npgsql
open Npgsql.FSharp

/// Create a SQL parameter for the web log ID
let webLogIdParam webLogId =
    "@webLogId", Sql.string (WebLogId.toString webLogId)

/// The name of the field to select to be able to use Map.toCount
let countName = "the_count"

/// The name of the field to select to be able to use Map.toExists
let existsName = "does_exist"

/// Create the SQL and parameters for an IN clause
let inClause<'T> colNameAndPrefix paramName (valueFunc: 'T -> string) (items : 'T list) =
    if List.isEmpty items then "", []
    else
        let mutable idx = 0
        items
        |> List.skip 1
        |> List.fold (fun (itemS, itemP) it ->
            idx <- idx + 1
            $"{itemS}, @%s{paramName}{idx}", ($"@%s{paramName}{idx}", Sql.string (valueFunc it)) :: itemP)
            (Seq.ofList items
             |> Seq.map (fun it ->
                 $"%s{colNameAndPrefix} IN (@%s{paramName}0", [ $"@%s{paramName}0", Sql.string (valueFunc it) ])
             |> Seq.head)
        |> function sql, ps -> $"{sql})", ps

/// Create the SQL and parameters for the array equivalent of an IN clause
let arrayInClause<'T> name (valueFunc : 'T -> string) (items : 'T list) =
    if List.isEmpty items then "TRUE = FALSE", []
    else
        let mutable idx = 0
        items
        |> List.skip 1
        |> List.fold (fun (itemS, itemP) it ->
            idx <- idx + 1
            $"{itemS} OR %s{name} && ARRAY[@{name}{idx}]",
            ($"@{name}{idx}", Sql.string (valueFunc it)) :: itemP)
            (Seq.ofList items
             |> Seq.map (fun it ->
                 $"{name} && ARRAY[@{name}0]", [ $"@{name}0", Sql.string (valueFunc it) ])
             |> Seq.head)
    
/// Get the first result of the given query
let tryHead<'T> (query : Task<'T list>) = backgroundTask {
    let! results = query
    return List.tryHead results
}

/// Create a parameter for a non-standard type
let typedParam<'T> name (it : 'T) =
    $"@%s{name}", Sql.parameter (NpgsqlParameter ($"@{name}", it))

/// Create a parameter for a possibly-missing non-standard type
let optParam<'T> name (it : 'T option) =
    let p = NpgsqlParameter ($"@%s{name}", if Option.isSome it then box it.Value else DBNull.Value)
    p.ParameterName, Sql.parameter p

/// Mapping functions for SQL queries
module Map =
    
    /// Map an id field to a category ID
    let toCategoryId (row : RowReader) =
        CategoryId (row.string "id")
    
    /// Create a category from the current row
    let toCategory (row : RowReader) : Category =
        {   Id          = toCategoryId row
            WebLogId    = row.string       "web_log_id" |> WebLogId
            Name        = row.string       "name"
            Slug        = row.string       "slug"
            Description = row.stringOrNone "description"
            ParentId    = row.stringOrNone "parent_id"  |> Option.map CategoryId
        }

    /// Get a count from a row
    let toCount (row : RowReader) =
        row.int countName
    
    /// Create a custom feed from the current row
    let toCustomFeed (ser : JsonSerializer) (row : RowReader) : CustomFeed =
        {   Id      = row.string       "id"      |> CustomFeedId
            Source  = row.string       "source"  |> CustomFeedSource.parse
            Path    = row.string       "path"    |> Permalink
            Podcast = row.stringOrNone "podcast" |> Option.map (Utils.deserialize ser)
        }
    
    /// Get a true/false value as to whether an item exists
    let toExists (row : RowReader) =
        row.bool existsName
    
    /// Create a meta item from the current row
    let toMetaItem (row : RowReader) : MetaItem =
        {   Name  = row.string "name"
            Value = row.string "value"
        }
    
    /// Create a permalink from the current row
    let toPermalink (row : RowReader) =
        Permalink (row.string "permalink")
    
    /// Create a page from the current row
    let toPage (ser : JsonSerializer) (row : RowReader) : Page =
        { Page.empty with
            Id              = row.string              "id"         |> PageId
            WebLogId        = row.string              "web_log_id" |> WebLogId
            AuthorId        = row.string              "author_id"  |> WebLogUserId
            Title           = row.string              "title"
            Permalink       = toPermalink row
            PriorPermalinks = row.stringArray         "prior_permalinks" |> Array.map Permalink |> List.ofArray
            PublishedOn     = row.fieldValue<Instant> "published_on"
            UpdatedOn       = row.fieldValue<Instant> "updated_on"
            IsInPageList    = row.bool                "is_in_page_list"
            Template        = row.stringOrNone        "template"
            Text            = row.string              "page_text"
            Metadata        = row.stringOrNone        "meta_items"
                              |> Option.map (Utils.deserialize ser)
                              |> Option.defaultValue []
        }
    
    /// Create a post from the current row
    let toPost (ser : JsonSerializer) (row : RowReader) : Post =
        { Post.empty with
            Id              = row.string                    "id"         |> PostId
            WebLogId        = row.string                    "web_log_id" |> WebLogId
            AuthorId        = row.string                    "author_id"  |> WebLogUserId
            Status          = row.string                    "status"     |> PostStatus.parse
            Title           = row.string                    "title"
            Permalink       = toPermalink row
            PriorPermalinks = row.stringArray               "prior_permalinks" |> Array.map Permalink |> List.ofArray
            PublishedOn     = row.fieldValueOrNone<Instant> "published_on"
            UpdatedOn       = row.fieldValue<Instant>       "updated_on"
            Template        = row.stringOrNone              "template"
            Text            = row.string                    "post_text"
            Episode         = row.stringOrNone              "episode"          |> Option.map (Utils.deserialize ser)
            CategoryIds     = row.stringArrayOrNone         "category_ids"
                              |> Option.map (Array.map CategoryId >> List.ofArray)
                              |> Option.defaultValue []
            Tags            = row.stringArrayOrNone         "tags"
                              |> Option.map List.ofArray
                              |> Option.defaultValue []
            Metadata        = row.stringOrNone              "meta_items"
                              |> Option.map (Utils.deserialize ser)
                              |> Option.defaultValue []
        }
    
    /// Create a revision from the current row
    let toRevision (row : RowReader) : Revision =
        {   AsOf = row.fieldValue<Instant> "as_of"
            Text = row.string              "revision_text" |> MarkupText.parse
        }
    
    /// Create a tag mapping from the current row
    let toTagMap (row : RowReader) : TagMap =
        {   Id       = row.string "id"         |> TagMapId
            WebLogId = row.string "web_log_id" |> WebLogId
            Tag      = row.string "tag"
            UrlValue = row.string "url_value"
        }
    
    /// Create a theme from the current row (excludes templates)
    let toTheme (row : RowReader) : Theme =
        { Theme.empty with
            Id      = row.string "id" |> ThemeId
            Name    = row.string "name"
            Version = row.string "version"
        }
    
    /// Create a theme asset from the current row
    let toThemeAsset includeData (row : RowReader) : ThemeAsset =
        {   Id        = ThemeAssetId (ThemeId (row.string "theme_id"), row.string "path")
            UpdatedOn = row.fieldValue<Instant> "updated_on"
            Data      = if includeData then row.bytea "data" else [||]
        }
    
    /// Create a theme template from the current row
    let toThemeTemplate includeText (row : RowReader) : ThemeTemplate =
        {   Name = row.string "name"
            Text = if includeText then row.string "template" else ""
        }

    /// Create an uploaded file from the current row
    let toUpload includeData (row : RowReader) : Upload =
        {   Id        = row.string              "id"         |> UploadId
            WebLogId  = row.string              "web_log_id" |> WebLogId
            Path      = row.string              "path"       |> Permalink
            UpdatedOn = row.fieldValue<Instant> "updated_on"
            Data      = if includeData then row.bytea "data" else [||]
        }
    
    /// Create a web log from the current row
    let toWebLog (row : RowReader) : WebLog =
        {   Id           = row.string       "id"             |> WebLogId
            Name         = row.string       "name"
            Slug         = row.string       "slug"
            Subtitle     = row.stringOrNone "subtitle"
            DefaultPage  = row.string       "default_page"
            PostsPerPage = row.int          "posts_per_page"
            ThemeId      = row.string       "theme_id"       |> ThemeId
            UrlBase      = row.string       "url_base"
            TimeZone     = row.string       "time_zone"
            AutoHtmx     = row.bool         "auto_htmx"
            Uploads      = row.string       "uploads"        |> UploadDestination.parse
            Rss          = {
                IsFeedEnabled     = row.bool         "is_feed_enabled"
                FeedName          = row.string       "feed_name"
                ItemsInFeed       = row.intOrNone    "items_in_feed"
                IsCategoryEnabled = row.bool         "is_category_enabled"
                IsTagEnabled      = row.bool         "is_tag_enabled"
                Copyright         = row.stringOrNone "copyright"
                CustomFeeds       = []
            }
        }
    
    /// Create a web log user from the current row
    let toWebLogUser (row : RowReader) : WebLogUser =
        {   Id            = row.string                    "id"             |> WebLogUserId
            WebLogId      = row.string                    "web_log_id"     |> WebLogId
            Email         = row.string                    "email"
            FirstName     = row.string                    "first_name"
            LastName      = row.string                    "last_name"
            PreferredName = row.string                    "preferred_name"
            PasswordHash  = row.string                    "password_hash"
            Url           = row.stringOrNone              "url"
            AccessLevel   = row.string                    "access_level"   |> AccessLevel.parse
            CreatedOn     = row.fieldValue<Instant>       "created_on"
            LastSeenOn    = row.fieldValueOrNone<Instant> "last_seen_on"
        }
