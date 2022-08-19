/// Helper functions for the PostgreSQL data implementation
[<AutoOpen>]
module MyWebLog.Data.Postgres.PostgresHelpers

open System.Threading.Tasks
open MyWebLog
open Newtonsoft.Json
open Npgsql.FSharp

/// Create a SQL parameter for the web log ID
let webLogIdParam webLogId =
    "@webLogId", Sql.string (WebLogId.toString webLogId)

/// The name of the field to select to be able to use Map.toCount
let countName = "the_count"

/// The name of the field to select to be able to use Map.toExists
let existsName = "does_exist"

/// Create the SQL and parameters for an IN clause
let inClause<'T> name (valueFunc: 'T -> string) (items : 'T list) =
    let mutable idx = 0
    items
    |> List.skip 1
    |> List.fold (fun (itemS, itemP) it ->
        idx <- idx + 1
        $"{itemS}, @%s{name}{idx}", ($"@%s{name}{idx}", Sql.string (valueFunc it)) :: itemP)
        (Seq.ofList items
         |> Seq.map (fun it -> $"@%s{name}0", [ $"@%s{name}0", Sql.string (valueFunc it) ])
         |> Seq.head)

/// Create the SQL and parameters for the array equivalent of an IN clause
let arrayInClause<'T> name (valueFunc : 'T -> string) (items : 'T list) =
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
    let toCustomFeed (row : RowReader) : CustomFeed =
        {   Id      = row.string "id"     |> CustomFeedId
            Source  = row.string "source" |> CustomFeedSource.parse
            Path    = row.string "path"   |> Permalink
            Podcast =
                match row.stringOrNone "title" with
                | Some title ->
                    Some {
                        Title             = title
                        Subtitle          = row.stringOrNone "subtitle"
                        ItemsInFeed       = row.int          "items_in_feed"
                        Summary           = row.string       "summary"
                        DisplayedAuthor   = row.string       "displayed_author"
                        Email             = row.string       "email"
                        ImageUrl          = row.string       "image_url"          |> Permalink
                        AppleCategory     = row.string       "apple_category"
                        AppleSubcategory  = row.stringOrNone "apple_subcategory"
                        Explicit          = row.string       "explicit"           |> ExplicitRating.parse
                        DefaultMediaType  = row.stringOrNone "default_media_type"
                        MediaBaseUrl      = row.stringOrNone "media_base_url"
                        PodcastGuid       = row.uuidOrNone   "podcast_guid"
                        FundingUrl        = row.stringOrNone "funding_url"
                        FundingText       = row.stringOrNone "funding_text"
                        Medium            = row.stringOrNone "medium"             |> Option.map PodcastMedium.parse
                    }
                | None -> None
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
    let toPage (row : RowReader) : Page =
        { Page.empty with
            Id              = row.string       "id"         |> PageId
            WebLogId        = row.string       "web_log_id" |> WebLogId
            AuthorId        = row.string       "author_id"  |> WebLogUserId
            Title           = row.string       "title"
            Permalink       = toPermalink row
            PriorPermalinks = row.stringArray  "prior_permalinks" |> Array.map Permalink |> List.ofArray
            PublishedOn     = row.dateTime     "published_on"
            UpdatedOn       = row.dateTime     "updated_on"
            IsInPageList    = row.bool         "is_in_page_list"
            Template        = row.stringOrNone "template"
            Text            = row.string       "page_text"
            Metadata        = row.stringOrNone "meta_items"
                              |> Option.map JsonConvert.DeserializeObject<MetaItem list>
                              |> Option.defaultValue []
        }
    
    /// Create a post from the current row
    let toPost (row : RowReader) : Post =
        { Post.empty with
            Id              = row.string            "id"         |> PostId
            WebLogId        = row.string            "web_log_id" |> WebLogId
            AuthorId        = row.string            "author_id"  |> WebLogUserId
            Status          = row.string            "status"     |> PostStatus.parse
            Title           = row.string            "title"
            Permalink       = toPermalink row
            PriorPermalinks = row.stringArray       "prior_permalinks" |> Array.map Permalink |> List.ofArray
            PublishedOn     = row.dateTimeOrNone    "published_on"
            UpdatedOn       = row.dateTime          "updated_on"
            Template        = row.stringOrNone      "template"
            Text            = row.string            "post_text"
            CategoryIds     = row.stringArrayOrNone "category_ids"
                              |> Option.map (Array.map CategoryId >> List.ofArray)
                              |> Option.defaultValue []
            Tags            = row.stringArrayOrNone "tags"
                              |> Option.map List.ofArray
                              |> Option.defaultValue []
            Metadata        = row.stringOrNone      "meta_items"
                              |> Option.map JsonConvert.DeserializeObject<MetaItem list>
                              |> Option.defaultValue []
            Episode         = row.stringOrNone      "episode" |> Option.map JsonConvert.DeserializeObject<Episode>
        }
    
    /// Create a revision from the current row
    let toRevision (row : RowReader) : Revision =
        {   AsOf = row.dateTime "as_of"
            Text = row.string   "revision_text" |> MarkupText.parse
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
            UpdatedOn = row.dateTime "updated_on"
            Data      = if includeData then row.bytea "data" else [||]
        }
    
    /// Create a theme template from the current row
    let toThemeTemplate includeText (row : RowReader) : ThemeTemplate =
        {   Name = row.string "name"
            Text = if includeText then row.string "template" else ""
        }

    /// Create an uploaded file from the current row
    let toUpload includeData (row : RowReader) : Upload =
        {   Id        = row.string   "id"         |> UploadId
            WebLogId  = row.string   "web_log_id" |> WebLogId
            Path      = row.string   "path"       |> Permalink
            UpdatedOn = row.dateTime "updated_on"
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
        {   Id            = row.string         "id"             |> WebLogUserId
            WebLogId      = row.string         "web_log_id"     |> WebLogId
            Email         = row.string         "email"
            FirstName     = row.string         "first_name"
            LastName      = row.string         "last_name"
            PreferredName = row.string         "preferred_name"
            PasswordHash  = row.string         "password_hash"
            Salt          = row.uuid           "salt"
            Url           = row.stringOrNone   "url"
            AccessLevel   = row.string         "access_level"   |> AccessLevel.parse
            CreatedOn     = row.dateTime       "created_on"
            LastSeenOn    = row.dateTimeOrNone "last_seen_on"
        }
