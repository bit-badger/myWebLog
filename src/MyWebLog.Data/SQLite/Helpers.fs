/// Helper functions for the SQLite data implementation
[<AutoOpen>]
module MyWebLog.Data.SQLite.Helpers

/// The table names used in the SQLite implementation
[<RequireQualifiedAccess>]
module Table =
    
    /// Categories
    [<Literal>]
    let Category = "category"
    
    /// Database Version
    [<Literal>]
    let DbVersion = "db_version"
    
    /// Pages
    [<Literal>]
    let Page = "page"
    
    /// Page Revisions
    [<Literal>]
    let PageRevision = "page_revision"
    
    /// Posts
    [<Literal>]
    let Post = "post"
    
    /// Post Comments
    [<Literal>]
    let PostComment = "post_comment"
    
    /// Post Revisions
    [<Literal>]
    let PostRevision = "post_revision"
    
    /// Tag/URL Mappings
    [<Literal>]
    let TagMap = "tag_map"
    
    /// Themes
    [<Literal>]
    let Theme = "theme"
    
    /// Theme Assets
    [<Literal>]
    let ThemeAsset = "theme_asset"
    
    /// Uploads
    [<Literal>]
    let Upload = "upload"
    
    /// Web Logs
    [<Literal>]
    let WebLog = "web_log"
    
    /// Users
    [<Literal>]
    let WebLogUser = "web_log_user"


open System
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data
open NodaTime.Text

/// Run a command that returns a count
let count (cmd : SqliteCommand) = backgroundTask {
    let! it = cmd.ExecuteScalarAsync ()
    return int (it :?> int64)
}

/// Create a list of items from the given data reader
let toList<'T> (it : SqliteDataReader -> 'T) (rdr : SqliteDataReader) =
    seq { while rdr.Read () do it rdr }
    |> List.ofSeq

/// Verify that the web log ID matches before returning an item
let verifyWebLog<'T> webLogId (prop : 'T -> WebLogId) (it : SqliteDataReader -> 'T) (rdr : SqliteDataReader) =
    if rdr.Read () then
        let item = it rdr
        if prop item = webLogId then Some item else None
    else None

/// Execute a command that returns no data
let write (cmd : SqliteCommand) = backgroundTask {
    let! _ = cmd.ExecuteNonQueryAsync ()
    ()
}

/// Add a possibly-missing parameter, substituting null for None
let maybe<'T> (it : 'T option) : obj = match it with Some x -> x :> obj | None -> DBNull.Value

/// Create a value for a Duration
let durationParam =
    DurationPattern.Roundtrip.Format

/// Create a value for an Instant
let instantParam =
    InstantPattern.General.Format

/// Create an optional value for a Duration
let maybeDuration =
    Option.map durationParam >> maybe

/// Create an optional value for an Instant
let maybeInstant =
    Option.map instantParam >> maybe

/// Create the SQL and parameters for an IN clause
let inClause<'T> colNameAndPrefix paramName (valueFunc: 'T -> string) (items : 'T list) =
    if List.isEmpty items then "", []
    else
        let mutable idx = 0
        items
        |> List.skip 1
        |> List.fold (fun (itemS, itemP) it ->
            idx <- idx + 1
            $"{itemS}, @%s{paramName}{idx}", (SqliteParameter ($"@%s{paramName}{idx}", valueFunc it) :: itemP))
            (Seq.ofList items
             |> Seq.map (fun it ->
                 $"%s{colNameAndPrefix} IN (@%s{paramName}0", [ SqliteParameter ($"@%s{paramName}0", valueFunc it) ])
             |> Seq.head)
        |> function sql, ps -> $"{sql})", ps


/// Functions to map domain items from a data reader
module Map =
    
    open System.IO
    
    /// Get a boolean value from a data reader
    let getBoolean col (rdr : SqliteDataReader) = rdr.GetBoolean (rdr.GetOrdinal col)
    
    /// Get a date/time value from a data reader
    let getDateTime col (rdr : SqliteDataReader) = rdr.GetDateTime (rdr.GetOrdinal col)
    
    /// Get a Guid value from a data reader
    let getGuid col (rdr : SqliteDataReader) = rdr.GetGuid (rdr.GetOrdinal col)
    
    /// Get an int value from a data reader
    let getInt col (rdr : SqliteDataReader) = rdr.GetInt32 (rdr.GetOrdinal col)
    
    /// Get a long (64-bit int) value from a data reader
    let getLong col (rdr : SqliteDataReader) = rdr.GetInt64 (rdr.GetOrdinal col)
    
    /// Get a BLOB stream value from a data reader
    let getStream col (rdr : SqliteDataReader) = rdr.GetStream (rdr.GetOrdinal col)
    
    /// Get a string value from a data reader
    let getString col (rdr : SqliteDataReader) = rdr.GetString (rdr.GetOrdinal col)
    
    /// Parse a Duration from the given value
    let parseDuration value =
        match DurationPattern.Roundtrip.Parse value with
        | it when it.Success -> it.Value
        | it -> raise it.Exception
    
    /// Get a Duration value from a data reader
    let getDuration col rdr =
        getString col rdr |> parseDuration
    
    /// Parse an Instant from the given value
    let parseInstant value =
        match InstantPattern.General.Parse value with
        | it when it.Success -> it.Value
        | it -> raise it.Exception
    
    /// Get an Instant value from a data reader
    let getInstant col rdr =
        getString col rdr |> parseInstant
    
    /// Get a timespan value from a data reader
    let getTimeSpan col (rdr : SqliteDataReader) = rdr.GetTimeSpan (rdr.GetOrdinal col)
    
    /// Get a possibly null boolean value from a data reader
    let tryBoolean col (rdr : SqliteDataReader) =
        if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getBoolean col rdr)
    
    /// Get a possibly null date/time value from a data reader
    let tryDateTime col (rdr : SqliteDataReader) =
        if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getDateTime col rdr)
    
    /// Get a possibly null Guid value from a data reader
    let tryGuid col (rdr : SqliteDataReader) =
        if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getGuid col rdr)
    
    /// Get a possibly null int value from a data reader
    let tryInt col (rdr : SqliteDataReader) =
        if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getInt col rdr)
    
    /// Get a possibly null string value from a data reader
    let tryString col (rdr : SqliteDataReader) =
        if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getString col rdr)
    
    /// Get a possibly null Duration value from a data reader
    let tryDuration col rdr =
        tryString col rdr |> Option.map parseDuration
    
    /// Get a possibly null Instant value from a data reader
    let tryInstant col rdr =
        tryString col rdr |> Option.map parseInstant
    
    /// Get a possibly null timespan value from a data reader
    let tryTimeSpan col (rdr : SqliteDataReader) =
        if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getTimeSpan col rdr)
    
    /// Map an id field to a category ID
    let toCategoryId rdr = getString "id" rdr |> CategoryId
    
    /// Create a category from the current row in the given data reader
    let toCategory rdr : Category =
        {   Id          = toCategoryId            rdr
            WebLogId    = getString "web_log_id"  rdr |> WebLogId
            Name        = getString "name"        rdr
            Slug        = getString "slug"        rdr
            Description = tryString "description" rdr
            ParentId    = tryString "parent_id"   rdr |> Option.map CategoryId
        }
    
    /// Create a custom feed from the current row in the given data reader
    let toCustomFeed ser rdr : CustomFeed =
        {   Id      = getString "id"      rdr |> CustomFeedId
            Source  = getString "source"  rdr |> CustomFeedSource.Parse
            Path    = getString "path"    rdr |> Permalink
            Podcast = tryString "podcast" rdr |> Option.map (Utils.deserialize ser)
        }
    
    /// Create a permalink from the current row in the given data reader
    let toPermalink rdr = getString "permalink" rdr |> Permalink
    
    /// Create a page from the current row in the given data reader
    let toPage ser rdr : Page =
        { Page.Empty with
            Id           = getString   "id"              rdr |> PageId
            WebLogId     = getString   "web_log_id"      rdr |> WebLogId
            AuthorId     = getString   "author_id"       rdr |> WebLogUserId
            Title        = getString   "title"           rdr
            Permalink    = toPermalink                   rdr
            PublishedOn  = getInstant  "published_on"    rdr
            UpdatedOn    = getInstant  "updated_on"      rdr
            IsInPageList = getBoolean  "is_in_page_list" rdr
            Template     = tryString   "template"        rdr
            Text         = getString   "page_text"       rdr
            Metadata     = tryString   "meta_items"   rdr
                           |> Option.map (Utils.deserialize ser)
                           |> Option.defaultValue []
        }
    
    /// Create a post from the current row in the given data reader
    let toPost ser rdr : Post =
        { Post.Empty with
            Id          = getString   "id"           rdr |> PostId
            WebLogId    = getString   "web_log_id"   rdr |> WebLogId
            AuthorId    = getString   "author_id"    rdr |> WebLogUserId
            Status      = getString   "status"       rdr |> PostStatus.Parse
            Title       = getString   "title"        rdr
            Permalink   = toPermalink                rdr
            PublishedOn = tryInstant  "published_on" rdr
            UpdatedOn   = getInstant  "updated_on"   rdr
            Template    = tryString   "template"     rdr
            Text        = getString   "post_text"    rdr
            Episode     = tryString   "episode"      rdr |> Option.map (Utils.deserialize ser)
            Metadata    = tryString   "meta_items"   rdr
                          |> Option.map (Utils.deserialize ser)
                          |> Option.defaultValue []
        }
    
    /// Create a revision from the current row in the given data reader
    let toRevision rdr : Revision =
        {   AsOf = getInstant "as_of"         rdr
            Text = getString  "revision_text" rdr |> MarkupText.Parse
        }
    
    /// Create a tag mapping from the current row in the given data reader
    let toTagMap rdr : TagMap =
        {   Id       = getString "id"         rdr |> TagMapId
            WebLogId = getString "web_log_id" rdr |> WebLogId
            Tag      = getString "tag"        rdr
            UrlValue = getString "url_value"  rdr
        }
    
    /// Create a theme from the current row in the given data reader (excludes templates)
    let toTheme rdr : Theme =
        { Theme.Empty with
            Id      = getString "id"      rdr |> ThemeId
            Name    = getString "name"    rdr
            Version = getString "version" rdr
        }
    
    /// Create a theme asset from the current row in the given data reader
    let toThemeAsset includeData rdr : ThemeAsset =
        let assetData =
            if includeData then
                use dataStream = new MemoryStream ()
                use blobStream = getStream "data" rdr
                blobStream.CopyTo dataStream
                dataStream.ToArray ()
            else
                [||]
        {   Id        = ThemeAssetId (ThemeId (getString "theme_id" rdr), getString "path" rdr)
            UpdatedOn = getInstant "updated_on" rdr
            Data      = assetData
        }
    
    /// Create a theme template from the current row in the given data reader
    let toThemeTemplate includeText rdr : ThemeTemplate =
        {   Name = getString "name" rdr
            Text = if includeText then getString "template" rdr else ""
        }
    
    /// Create an uploaded file from the current row in the given data reader
    let toUpload includeData rdr : Upload =
        let data =
            if includeData then
                use dataStream = new MemoryStream ()
                use blobStream = getStream "data" rdr
                blobStream.CopyTo dataStream
                dataStream.ToArray ()
            else
                [||]
        {   Id        = getString  "id"         rdr |> UploadId
            WebLogId  = getString  "web_log_id" rdr |> WebLogId
            Path      = getString  "path"       rdr |> Permalink
            UpdatedOn = getInstant "updated_on" rdr
            Data      = data
        }
    
    /// Create a web log from the current row in the given data reader
    let toWebLog ser rdr : WebLog =
        {   Id            = getString  "id"             rdr |> WebLogId
            Name          = getString  "name"           rdr
            Slug          = getString  "slug"           rdr
            Subtitle      = tryString  "subtitle"       rdr
            DefaultPage   = getString  "default_page"   rdr
            PostsPerPage  = getInt     "posts_per_page" rdr
            ThemeId       = getString  "theme_id"       rdr |> ThemeId
            UrlBase       = getString  "url_base"       rdr
            TimeZone      = getString  "time_zone"      rdr
            AutoHtmx      = getBoolean "auto_htmx"      rdr
            Uploads       = getString  "uploads"        rdr |> UploadDestination.Parse
            Rss           = {
                IsFeedEnabled     = getBoolean "is_feed_enabled"     rdr
                FeedName          = getString  "feed_name"           rdr
                ItemsInFeed       = tryInt     "items_in_feed"       rdr
                IsCategoryEnabled = getBoolean "is_category_enabled" rdr
                IsTagEnabled      = getBoolean "is_tag_enabled"      rdr
                Copyright         = tryString  "copyright"           rdr
                CustomFeeds       = []
            }
            RedirectRules = getString "redirect_rules" rdr |> Utils.deserialize ser
        }
    
    /// Create a web log user from the current row in the given data reader
    let toWebLogUser rdr : WebLogUser =
        {   Id            = getString  "id"             rdr |> WebLogUserId
            WebLogId      = getString  "web_log_id"     rdr |> WebLogId
            Email         = getString  "email"          rdr
            FirstName     = getString  "first_name"     rdr
            LastName      = getString  "last_name"      rdr
            PreferredName = getString  "preferred_name" rdr
            PasswordHash  = getString  "password_hash"  rdr
            Url           = tryString  "url"            rdr
            AccessLevel   = getString  "access_level"   rdr |> AccessLevel.Parse
            CreatedOn     = getInstant "created_on"     rdr
            LastSeenOn    = tryInstant "last_seen_on"   rdr
        }

/// Add a web log ID parameter
let addWebLogId (cmd: SqliteCommand) (webLogId: WebLogId) =
    cmd.Parameters.AddWithValue ("@webLogId", string webLogId) |> ignore
