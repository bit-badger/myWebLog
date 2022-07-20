/// Helper functions for the SQLite data implementation
[<AutoOpen>]
module MyWebLog.Data.SQLite.Helpers

open System
open Microsoft.Data.Sqlite
open MyWebLog

/// Run a command that returns a count
let count (cmd : SqliteCommand) = backgroundTask {
    let! it = cmd.ExecuteScalarAsync ()
    return int (it :?> int64)
}

/// Get lists of items removed from and added to the given lists
let diffLists<'T, 'U when 'U : equality> oldItems newItems (f : 'T -> 'U) =
    let diff compList = fun item -> not (compList |> List.exists (fun other -> f item = f other))
    List.filter (diff newItems) oldItems, List.filter (diff oldItems) newItems

/// Find meta items added and removed
let diffMetaItems (oldItems : MetaItem list) newItems =
    diffLists oldItems newItems (fun item -> $"{item.Name}|{item.Value}")

/// Find the permalinks added and removed
let diffPermalinks oldLinks newLinks =
    diffLists oldLinks newLinks Permalink.toString

/// Find the revisions added and removed
let diffRevisions oldRevs newRevs =
    diffLists oldRevs newRevs (fun (rev : Revision) -> $"{rev.AsOf.Ticks}|{MarkupText.toString rev.Text}")

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
    let toCustomFeed rdr : CustomFeed =
        {   Id      = getString "id"     rdr |> CustomFeedId
            Source  = getString "source" rdr |> CustomFeedSource.parse
            Path    = getString "path"   rdr |> Permalink
            Podcast =
                if rdr.IsDBNull (rdr.GetOrdinal "title") then
                    None
                else
                    Some {
                        Title             = getString "title"              rdr
                        Subtitle          = tryString "subtitle"           rdr
                        ItemsInFeed       = getInt    "items_in_feed"      rdr
                        Summary           = getString "summary"            rdr
                        DisplayedAuthor   = getString "displayed_author"   rdr
                        Email             = getString "email"              rdr
                        ImageUrl          = getString "image_url"          rdr |> Permalink
                        AppleCategory     = getString "apple_category"     rdr
                        AppleSubcategory  = tryString "apple_subcategory"  rdr
                        Explicit          = getString "explicit"           rdr |> ExplicitRating.parse
                        DefaultMediaType  = tryString "default_media_type" rdr
                        MediaBaseUrl      = tryString "media_base_url"     rdr
                        PodcastGuid       = tryGuid   "podcast_guid"       rdr
                        FundingUrl        = tryString "funding_url"        rdr
                        FundingText       = tryString "funding_text"       rdr
                        Medium            = tryString "medium"             rdr |> Option.map PodcastMedium.parse
                    }
        }
    
    /// Create a meta item from the current row in the given data reader
    let toMetaItem rdr : MetaItem =
        {   Name  = getString "name"  rdr
            Value = getString "value" rdr
        }
    
    /// Create a permalink from the current row in the given data reader
    let toPermalink rdr = getString "permalink" rdr |> Permalink
    
    /// Create a page from the current row in the given data reader
    let toPage rdr : Page =
        { Page.empty with
            Id           = getString   "id"              rdr |> PageId
            WebLogId     = getString   "web_log_id"      rdr |> WebLogId
            AuthorId     = getString   "author_id"       rdr |> WebLogUserId
            Title        = getString   "title"           rdr
            Permalink    = toPermalink                   rdr
            PublishedOn  = getDateTime "published_on"    rdr
            UpdatedOn    = getDateTime "updated_on"      rdr
            IsInPageList = getBoolean  "is_in_page_list" rdr
            Template     = tryString   "template"        rdr
            Text         = getString   "page_text"       rdr
        }
    
    /// Create a post from the current row in the given data reader
    let toPost rdr : Post =
        { Post.empty with
            Id             = getString   "id"           rdr |> PostId
            WebLogId       = getString   "web_log_id"   rdr |> WebLogId
            AuthorId       = getString   "author_id"    rdr |> WebLogUserId
            Status         = getString   "status"       rdr |> PostStatus.parse
            Title          = getString   "title"        rdr
            Permalink      = toPermalink                rdr
            PublishedOn    = tryDateTime "published_on" rdr
            UpdatedOn      = getDateTime "updated_on"   rdr
            Template       = tryString   "template"     rdr
            Text           = getString   "post_text"    rdr
            Episode        =
                match tryString "media" rdr with
                | Some media ->
                    Some {
                        Media              = media
                        Length             = getLong     "length"              rdr
                        Duration           = tryTimeSpan "duration"            rdr
                        MediaType          = tryString   "media_type"          rdr
                        ImageUrl           = tryString   "image_url"           rdr
                        Subtitle           = tryString   "subtitle"            rdr
                        Explicit           = tryString   "explicit"            rdr |> Option.map ExplicitRating.parse
                        ChapterFile        = tryString   "chapter_file"        rdr
                        ChapterType        = tryString   "chapter_type"        rdr
                        TranscriptUrl      = tryString   "transcript_url"      rdr
                        TranscriptType     = tryString   "transcript_type"     rdr
                        TranscriptLang     = tryString   "transcript_lang"     rdr
                        TranscriptCaptions = tryBoolean  "transcript_captions" rdr
                        SeasonNumber       = tryInt      "season_number"       rdr
                        SeasonDescription  = tryString   "season_description"  rdr
                        EpisodeNumber      = tryString   "episode_number"      rdr |> Option.map Double.Parse
                        EpisodeDescription = tryString   "episode_description" rdr
                    }
                | None -> None
        }
    
    /// Create a revision from the current row in the given data reader
    let toRevision rdr : Revision =
        {   AsOf = getDateTime "as_of"         rdr
            Text = getString   "revision_text" rdr |> MarkupText.parse
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
        { Theme.empty with
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
            UpdatedOn = getDateTime "updated_on" rdr
            Data      = assetData
        }
    
    /// Create a theme template from the current row in the given data reader
    let toThemeTemplate rdr : ThemeTemplate =
        {   Name = getString "name"     rdr
            Text = getString "template" rdr
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
        {   Id        = getString   "id"           rdr |> UploadId
            WebLogId  = getString   "web_log_id"   rdr |> WebLogId
            Path      = getString   "path"         rdr |> Permalink
            UpdatedOn = getDateTime "updated_on" rdr
            Data      = data
        }
    
    /// Create a web log from the current row in the given data reader
    let toWebLog rdr : WebLog =
        {   Id           = getString  "id"             rdr |> WebLogId
            Name         = getString  "name"           rdr
            Slug         = getString  "slug"           rdr
            Subtitle     = tryString  "subtitle"       rdr
            DefaultPage  = getString  "default_page"   rdr
            PostsPerPage = getInt     "posts_per_page" rdr
            ThemeId      = getString  "theme_id"       rdr |> ThemeId
            UrlBase      = getString  "url_base"       rdr
            TimeZone     = getString  "time_zone"      rdr
            AutoHtmx     = getBoolean "auto_htmx"      rdr
            Uploads      = getString  "uploads"        rdr |> UploadDestination.parse
            Rss          = {
                IsFeedEnabled     = getBoolean "is_feed_enabled"     rdr
                FeedName          = getString  "feed_name"           rdr
                ItemsInFeed       = tryInt     "items_in_feed"       rdr
                IsCategoryEnabled = getBoolean "is_category_enabled" rdr
                IsTagEnabled      = getBoolean "is_tag_enabled"      rdr
                Copyright         = tryString  "copyright"           rdr
                CustomFeeds       = []
            }
        }
    
    /// Create a web log user from the current row in the given data reader
    let toWebLogUser rdr : WebLogUser =
        {   Id            = getString   "id"             rdr |> WebLogUserId
            WebLogId      = getString   "web_log_id"     rdr |> WebLogId
            Email         = getString   "email"          rdr
            FirstName     = getString   "first_name"     rdr
            LastName      = getString   "last_name"      rdr
            PreferredName = getString   "preferred_name" rdr
            PasswordHash  = getString   "password_hash"  rdr
            Salt          = getGuid     "salt"           rdr
            Url           = tryString   "url"            rdr
            AccessLevel   = getString   "access_level"   rdr |> AccessLevel.parse
            CreatedOn     = getDateTime "created_on"     rdr
            LastSeenOn    = tryDateTime "last_seen_on"   rdr
        }

/// Add a possibly-missing parameter, substituting null for None
let maybe<'T> (it : 'T option) : obj = match it with Some x -> x :> obj | None -> DBNull.Value

/// Add a web log ID parameter
let addWebLogId (cmd : SqliteCommand) webLogId =
    cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore
