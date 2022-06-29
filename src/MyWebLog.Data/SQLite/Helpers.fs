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
    diffLists oldItems newItems (fun item -> $"{item.name}|{item.value}")

/// Find the permalinks added and removed
let diffPermalinks oldLinks newLinks =
    diffLists oldLinks newLinks Permalink.toString

/// Find the revisions added and removed
let diffRevisions oldRevs newRevs =
    diffLists oldRevs newRevs (fun (rev : Revision) -> $"{rev.asOf.Ticks}|{MarkupText.toString rev.text}")

/// Create a list of items from the given data reader
let toList<'T> (it : SqliteDataReader -> 'T) (rdr : SqliteDataReader) =
    seq { while rdr.Read () do it rdr }
    |> List.ofSeq

/// Verify that the web log ID matches before returning an item
let verifyWebLog<'T> webLogId (prop : 'T -> WebLogId) (it : SqliteDataReader -> 'T) (rdr : SqliteDataReader) =
    if rdr.Read () then
        let item = it rdr
        if prop item = webLogId then Some item else None
    else
        None

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
    
    /// Create a category ID from the current row in the given data reader
    let toCategoryId = getString "id" >> CategoryId
    
    /// Create a category from the current row in the given data reader
    let toCategory (rdr : SqliteDataReader) : Category =
        { id          = toCategoryId rdr
          webLogId    = WebLogId (getString "web_log_id" rdr)
          name        = getString "name" rdr
          slug        = getString "slug" rdr
          description = tryString "description" rdr
          parentId    = tryString "parent_id" rdr |> Option.map CategoryId
        }
    
    /// Create a custom feed from the current row in the given data reader
    let toCustomFeed (rdr : SqliteDataReader) : CustomFeed =
        { id      = CustomFeedId (getString "id" rdr)
          source  = CustomFeedSource.parse (getString "source" rdr)
          path    = Permalink (getString "path" rdr)
          podcast =
              if rdr.IsDBNull (rdr.GetOrdinal "title") then
                  None
              else
                  Some {
                      title             = getString "title" rdr
                      subtitle          = tryString "subtitle" rdr
                      itemsInFeed       = getInt "items_in_feed" rdr
                      summary           = getString "summary" rdr
                      displayedAuthor   = getString "displayed_author" rdr
                      email             = getString "email" rdr
                      imageUrl          = Permalink (getString "image_url" rdr)
                      iTunesCategory    = getString "itunes_category" rdr
                      iTunesSubcategory = tryString "itunes_subcategory" rdr
                      explicit          = ExplicitRating.parse (getString "explicit" rdr)
                      defaultMediaType  = tryString "default_media_type" rdr
                      mediaBaseUrl      = tryString "media_base_url" rdr
                      guid              = tryGuid "guid" rdr
                      fundingUrl        = tryString "funding_url" rdr
                      fundingText       = tryString "funding_text" rdr
                      medium            = tryString "medium" rdr |> Option.map PodcastMedium.parse
                  }
        }
    
    /// Create a meta item from the current row in the given data reader
    let toMetaItem (rdr : SqliteDataReader) : MetaItem =
        { name  = getString "name" rdr
          value = getString "value" rdr
        }
    
    /// Create a permalink from the current row in the given data reader
    let toPermalink = getString "permalink" >> Permalink
    
    /// Create a page from the current row in the given data reader
    let toPage (rdr : SqliteDataReader) : Page =
        { Page.empty with
            id             = PageId (getString "id" rdr)
            webLogId       = WebLogId (getString "web_log_id" rdr)
            authorId       = WebLogUserId (getString "author_id" rdr)
            title          = getString "title" rdr
            permalink      = toPermalink rdr
            publishedOn    = getDateTime "published_on" rdr
            updatedOn      = getDateTime "updated_on" rdr
            showInPageList = getBoolean "show_in_page_list" rdr
            template       = tryString "template" rdr
            text           = getString "page_text" rdr
        }
    
    /// Create a post from the current row in the given data reader
    let toPost (rdr : SqliteDataReader) : Post =
        { Post.empty with
            id             = PostId (getString "id" rdr)
            webLogId       = WebLogId (getString "web_log_id" rdr)
            authorId       = WebLogUserId (getString "author_id" rdr)
            status         = PostStatus.parse (getString "status" rdr)
            title          = getString "title" rdr
            permalink      = toPermalink rdr
            publishedOn    = tryDateTime "published_on" rdr
            updatedOn      = getDateTime "updated_on" rdr
            template       = tryString "template" rdr
            text           = getString "post_text" rdr
            episode        =
                match tryString "media" rdr with
                | Some media ->
                    Some {
                        media              = media
                        length             = getLong "length" rdr
                        duration           = tryTimeSpan "duration" rdr
                        mediaType          = tryString "media_type" rdr
                        imageUrl           = tryString "image_url" rdr
                        subtitle           = tryString "subtitle" rdr
                        explicit           = tryString "explicit" rdr |> Option.map ExplicitRating.parse
                        chapterFile        = tryString "chapter_file" rdr
                        chapterType        = tryString "chapter_type" rdr
                        transcriptUrl      = tryString "transcript_url" rdr
                        transcriptType     = tryString "transcript_type" rdr
                        transcriptLang     = tryString "transcript_lang" rdr
                        transcriptCaptions = tryBoolean "transcript_captions" rdr
                        seasonNumber       = tryInt "season_number" rdr
                        seasonDescription  = tryString "season_description" rdr
                        episodeNumber      = tryString "episode_number" rdr |> Option.map Double.Parse
                        episodeDescription = tryString "episode_description" rdr
                    }
                | None -> None
        }
    
    /// Create a revision from the current row in the given data reader
    let toRevision (rdr : SqliteDataReader) : Revision =
        { asOf = getDateTime "as_of" rdr
          text = MarkupText.parse (getString "revision_text" rdr)
        }
    
    /// Create a tag mapping from the current row in the given data reader
    let toTagMap (rdr : SqliteDataReader) : TagMap =
        { id       = TagMapId (getString "id" rdr)
          webLogId = WebLogId (getString "web_log_id" rdr)
          tag      = getString "tag" rdr
          urlValue = getString "url_value" rdr
        }
    
    /// Create a theme from the current row in the given data reader (excludes templates)
    let toTheme (rdr : SqliteDataReader) : Theme =
        { Theme.empty with
            id      = ThemeId (getString "id" rdr)
            name    = getString "name" rdr
            version = getString "version" rdr
        }
    
    /// Create a theme asset from the current row in the given data reader
    let toThemeAsset includeData (rdr : SqliteDataReader) : ThemeAsset =
        let assetData =
            if includeData then
                use dataStream = new MemoryStream ()
                use blobStream = getStream "data" rdr
                blobStream.CopyTo dataStream
                dataStream.ToArray ()
            else
                [||]
        { id        = ThemeAssetId (ThemeId (getString "theme_id" rdr), getString "path" rdr)
          updatedOn = getDateTime "updated_on" rdr
          data      = assetData
        }
    
    /// Create a theme template from the current row in the given data reader
    let toThemeTemplate (rdr : SqliteDataReader) : ThemeTemplate =
        { name = getString "name" rdr
          text = getString "template" rdr
        }
    
    /// Create an uploaded file from the current row in the given data reader
    let toUpload includeData (rdr : SqliteDataReader) : Upload =
        let data =
            if includeData then
                use dataStream = new MemoryStream ()
                use blobStream = getStream "data" rdr
                blobStream.CopyTo dataStream
                dataStream.ToArray ()
            else
                [||]
        { id        = UploadId (getString "id" rdr)
          webLogId  = WebLogId (getString "web_log_id" rdr)
          path      = Permalink (getString "path" rdr)
          updatedOn = getDateTime "updated_on" rdr
          data      = data
        }
    
    /// Create a web log from the current row in the given data reader
    let toWebLog (rdr : SqliteDataReader) : WebLog =
        { id           = WebLogId (getString "id" rdr)
          name         = getString "name" rdr
          slug         = getString "slug" rdr
          subtitle     = tryString "subtitle" rdr
          defaultPage  = getString "default_page" rdr
          postsPerPage = getInt "posts_per_page" rdr
          themePath    = getString "theme_id" rdr
          urlBase      = getString "url_base" rdr
          timeZone     = getString "time_zone" rdr
          autoHtmx     = getBoolean "auto_htmx" rdr
          uploads      = UploadDestination.parse (getString "uploads" rdr)
          rss          = {
              feedEnabled     = getBoolean "feed_enabled" rdr
              feedName        = getString "feed_name" rdr
              itemsInFeed     = tryInt "items_in_feed" rdr
              categoryEnabled = getBoolean "category_enabled" rdr
              tagEnabled      = getBoolean "tag_enabled" rdr
              copyright       = tryString "copyright" rdr
              customFeeds     = []
          }
        }
    
    /// Create a web log user from the current row in the given data reader
    let toWebLogUser (rdr : SqliteDataReader) : WebLogUser =
        { id                 = WebLogUserId (getString "id" rdr)
          webLogId           = WebLogId (getString "web_log_id" rdr)
          userName           = getString "user_name" rdr
          firstName          = getString "first_name" rdr
          lastName           = getString "last_name" rdr
          preferredName      = getString "preferred_name" rdr
          passwordHash       = getString "password_hash" rdr
          salt               = getGuid "salt" rdr
          url                = tryString "url" rdr
          authorizationLevel = AuthorizationLevel.parse (getString "authorization_level" rdr)
        }

/// Add a possibly-missing parameter, substituting null for None
let maybe<'T> (it : 'T option) : obj = match it with Some x -> x :> obj | None -> DBNull.Value

/// Add a web log ID parameter
let addWebLogId (cmd : SqliteCommand) webLogId =
    cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore
