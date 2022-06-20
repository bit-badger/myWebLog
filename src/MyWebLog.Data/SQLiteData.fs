namespace MyWebLog.Data

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.ViewModels

/// Helper functions for the SQLite data implementation
[<AutoOpen>]
module private SqliteHelpers =
    
    /// Run a command that returns a count
    let count (cmd : SqliteCommand) = backgroundTask {
        let! it = cmd.ExecuteScalarAsync ()
        return int (it :?> int64)
    }
    
    /// Get lists of items removed from and added to the given lists
    let diffLists<'T, 'U when 'U : equality> oldItems newItems (f : 'T -> 'U) =
        let diff compList = fun item -> not (compList |> List.exists (fun other -> f item = f other))
        List.filter (diff newItems) oldItems, List.filter (diff oldItems) newItems
    
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
        
        /// Get a boolean value from a data reader
        let getBoolean col (rdr : SqliteDataReader) = rdr.GetBoolean (rdr.GetOrdinal col)
        
        /// Get a date/time value from a data reader
        let getDateTime col (rdr : SqliteDataReader) = rdr.GetDateTime (rdr.GetOrdinal col)
        
        /// Get a Guid value from a data reader
        let getGuid col (rdr : SqliteDataReader) = rdr.GetGuid (rdr.GetOrdinal col)
        
        /// Get an int value from a data reader
        let getInt col (rdr : SqliteDataReader) = rdr.GetInt32 (rdr.GetOrdinal col)
        
        /// Get a BLOB stream value from a data reader
        let getStream col (rdr : SqliteDataReader) = rdr.GetStream (rdr.GetOrdinal col)
        
        /// Get a string value from a data reader
        let getString col (rdr : SqliteDataReader) = rdr.GetString (rdr.GetOrdinal col)
        
        /// Get a possibly null date/time value from a data reader
        let tryDateTime col (rdr : SqliteDataReader) =
            if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getDateTime col rdr)
        
        /// Get a possibly null int value from a data reader
        let tryInt col (rdr : SqliteDataReader) =
            if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getInt col rdr)
        
        /// Get a possibly null string value from a data reader
        let tryString col (rdr : SqliteDataReader) =
            if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getString col rdr)
        
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
        
        /// Create a web log from the current row in the given data reader
        let toWebLog (rdr : SqliteDataReader) : WebLog =
            { id           = WebLogId (getString "id" rdr)
              name         = getString "name" rdr
              subtitle     = tryString "subtitle" rdr
              defaultPage  = getString "default_page" rdr
              postsPerPage = getInt "posts_per_page" rdr
              themePath    = getString "theme_id" rdr
              urlBase      = getString "url_base" rdr
              timeZone     = getString "time_zone" rdr
              autoHtmx     = getBoolean "auto_htmx" rdr
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


/// SQLite myWebLog data implementation        
type SQLiteData (conn : SqliteConnection) =
    
    /// Add parameters for category INSERT or UPDATE statements
    let addCategoryParameters (cmd : SqliteCommand) (cat : Category) =
        [ cmd.Parameters.AddWithValue ("@id", CategoryId.toString cat.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString cat.webLogId)
          cmd.Parameters.AddWithValue ("@name", cat.name)
          cmd.Parameters.AddWithValue ("@slug", cat.slug)
          cmd.Parameters.AddWithValue ("@description", maybe cat.description)
          cmd.Parameters.AddWithValue ("@parentId", maybe (cat.parentId |> Option.map CategoryId.toString))
        ] |> ignore
    
    /// Add parameters for page INSERT or UPDATE statements
    let addPageParameters (cmd : SqliteCommand) (page : Page) =
        [ cmd.Parameters.AddWithValue ("@id", PageId.toString page.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString page.webLogId)
          cmd.Parameters.AddWithValue ("@authorId", WebLogUserId.toString page.authorId)
          cmd.Parameters.AddWithValue ("@title", page.title)
          cmd.Parameters.AddWithValue ("@permalink", Permalink.toString page.permalink)
          cmd.Parameters.AddWithValue ("@publishedOn", page.publishedOn)
          cmd.Parameters.AddWithValue ("@updatedOn", page.updatedOn)
          cmd.Parameters.AddWithValue ("@showInPageList", page.showInPageList)
          cmd.Parameters.AddWithValue ("@template", maybe page.template)
          cmd.Parameters.AddWithValue ("@text", page.text)
        ] |> ignore
    
    /// Add parameters for post INSERT or UPDATE statements
    let addPostParameters (cmd : SqliteCommand) (post : Post) =
        [ cmd.Parameters.AddWithValue ("@id", PostId.toString post.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString post.webLogId)
          cmd.Parameters.AddWithValue ("@authorId", WebLogUserId.toString post.authorId)
          cmd.Parameters.AddWithValue ("@status", PostStatus.toString post.status)
          cmd.Parameters.AddWithValue ("@title", post.title)
          cmd.Parameters.AddWithValue ("@permalink", Permalink.toString post.permalink)
          cmd.Parameters.AddWithValue ("@publishedOn", maybe post.publishedOn)
          cmd.Parameters.AddWithValue ("@updatedOn", post.updatedOn)
          cmd.Parameters.AddWithValue ("@template", maybe post.template)
          cmd.Parameters.AddWithValue ("@text", post.text)
        ] |> ignore
    
    /// Add parameters for web log INSERT or web log/RSS options UPDATE statements
    let addWebLogRssParameters (cmd : SqliteCommand) (webLog : WebLog) =
        [ cmd.Parameters.AddWithValue ("@feedEnabled", webLog.rss.feedEnabled)
          cmd.Parameters.AddWithValue ("@feedName", webLog.rss.feedName)
          cmd.Parameters.AddWithValue ("@itemsInFeed", maybe webLog.rss.itemsInFeed)
          cmd.Parameters.AddWithValue ("@categoryEnabled", webLog.rss.categoryEnabled)
          cmd.Parameters.AddWithValue ("@tagEnabled", webLog.rss.tagEnabled)
          cmd.Parameters.AddWithValue ("@copyright", maybe webLog.rss.copyright)
        ] |> ignore
    
    /// Add parameters for web log INSERT or UPDATE statements
    let addWebLogParameters (cmd : SqliteCommand) (webLog : WebLog) =
        [ cmd.Parameters.AddWithValue ("@id", WebLogId.toString webLog.id)
          cmd.Parameters.AddWithValue ("@name", webLog.name)
          cmd.Parameters.AddWithValue ("@subtitle", maybe webLog.subtitle)
          cmd.Parameters.AddWithValue ("@defaultPage", webLog.defaultPage)
          cmd.Parameters.AddWithValue ("@postsPerPage", webLog.postsPerPage)
          cmd.Parameters.AddWithValue ("@themeId", webLog.themePath)
          cmd.Parameters.AddWithValue ("@urlBase", webLog.urlBase)
          cmd.Parameters.AddWithValue ("@timeZone", webLog.timeZone)
          cmd.Parameters.AddWithValue ("@autoHtmx", webLog.autoHtmx)
        ] |> ignore
        addWebLogRssParameters cmd webLog
    
    /// Add parameters for web log user INSERT or UPDATE statements
    let addWebLogUserParameters (cmd : SqliteCommand) (user : WebLogUser) =
        [ cmd.Parameters.AddWithValue ("@id", WebLogUserId.toString user.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString user.webLogId)
          cmd.Parameters.AddWithValue ("@userName", user.userName)
          cmd.Parameters.AddWithValue ("@firstName", user.firstName)
          cmd.Parameters.AddWithValue ("@lastName", user.lastName)
          cmd.Parameters.AddWithValue ("@preferredName", user.preferredName)
          cmd.Parameters.AddWithValue ("@passwordHash", user.passwordHash)
          cmd.Parameters.AddWithValue ("@salt", user.salt)
          cmd.Parameters.AddWithValue ("@url", maybe user.url)
          cmd.Parameters.AddWithValue ("@authorizationLevel", AuthorizationLevel.toString user.authorizationLevel)
        ] |> ignore
    
    /// Add a web log ID parameter
    let addWebLogId (cmd : SqliteCommand) webLogId =
        cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore

    // -- PAGE STUFF --
    
    /// Append meta items to a page
    let appendPageMeta (page : Page) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT name, value FROM page_meta WHERE page_id = @id"
        cmd.Parameters.AddWithValue ("@id", PageId.toString page.id) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return { page with metadata = toList Map.toMetaItem rdr }
    }
    
    /// Append revisions and permalinks to a page
    let appendPageRevisionsAndPermalinks (page : Page) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@pageId", PageId.toString page.id) |> ignore
        
        cmd.CommandText <- "SELECT permalink FROM page_permalink WHERE page_id = @pageId"
        use! rdr = cmd.ExecuteReaderAsync ()
        let page = { page with priorPermalinks = toList Map.toPermalink rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT as_of, revision_text FROM page_revision WHERE page_id = @pageId ORDER BY as_of DESC"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { page with revisions = toList Map.toRevision rdr }
    }
    
    /// Return a page with no text (or meta items, prior permalinks, or revisions)
    let pageWithoutTextOrMeta rdr =
        { Map.toPage rdr with text = "" }
    
    /// Find meta items added and removed
    let diffMetaItems (oldItems : MetaItem list) newItems =
        diffLists oldItems newItems (fun item -> $"{item.name}|{item.value}")
    
    /// Find the permalinks added and removed
    let diffPermalinks oldLinks newLinks =
        diffLists oldLinks newLinks Permalink.toString
    
    /// Find the revisions added and removed
    let diffRevisions oldRevs newRevs =
        diffLists oldRevs newRevs (fun (rev : Revision) -> $"{rev.asOf.Ticks}|{MarkupText.toString rev.text}")
        
    /// Update a page's metadata items
    let updatePageMeta pageId oldItems newItems = backgroundTask {
        let toDelete, toAdd = diffMetaItems oldItems newItems
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
              cmd.Parameters.Add ("@name", SqliteType.Text)
              cmd.Parameters.Add ("@value", SqliteType.Text)
            ] |> ignore
            let runCmd (item : MetaItem) = backgroundTask {
                cmd.Parameters["@name" ].Value <- item.name
                cmd.Parameters["@value"].Value <- item.value
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM page_meta WHERE page_id = @pageId AND name = @name AND value = @value" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO page_meta VALUES (@pageId, @name, @value)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a page's prior permalinks
    let updatePagePermalinks pageId oldLinks newLinks = backgroundTask {
        let toDelete, toAdd = diffPermalinks oldLinks newLinks
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
              cmd.Parameters.Add ("@link", SqliteType.Text)
            ] |> ignore
            let runCmd link = backgroundTask {
                cmd.Parameters["@link"].Value <- Permalink.toString link
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM page_permalink WHERE page_id = @pageId AND permalink = @link" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO page_permalink VALUES (@pageId, @link)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a page's revisions
    let updatePageRevisions pageId oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = diffRevisions oldRevs newRevs
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            let runCmd withText rev = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
                  cmd.Parameters.AddWithValue ("@asOf", rev.asOf)
                ] |> ignore
                if withText then cmd.Parameters.AddWithValue ("@text", MarkupText.toString rev.text) |> ignore
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM page_revision WHERE page_id = @pageId AND as_of = @asOf" 
            toDelete
            |> List.map (runCmd false)
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO page_revision VALUES (@pageId, @asOf, @text)"
            toAdd
            |> List.map (runCmd true)
            |> Task.WhenAll
            |> ignore
    }
    
    // -- POST STUFF --
    
    /// Append category IDs, tags, and meta items to a post
    let appendPostCategoryTagAndMeta (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@id", PostId.toString post.id) |> ignore
        
        cmd.CommandText <- "SELECT category_id AS id FROM post_category WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with categoryIds = toList Map.toCategoryId rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT tag FROM post_tag WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with tags = toList (Map.getString "tag") rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT name, value FROM post_meta WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { post with metadata = toList Map.toMetaItem rdr }
    }
    
    /// Append revisions and permalinks to a post
    let appendPostRevisionsAndPermalinks (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@postId", PostId.toString post.id) |> ignore
        
        cmd.CommandText <- "SELECT permalink FROM post_permalink WHERE post_id = @postId"
        use! rdr = cmd.ExecuteReaderAsync ()
        let post = { post with priorPermalinks = toList Map.toPermalink rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT as_of, revision_text FROM post_revision WHERE post_id = @postId ORDER BY as_of DESC"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { post with revisions = toList Map.toRevision rdr }
    }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText rdr =
        { Map.toPost rdr with text = "" }
    
    /// Update a post's assigned categories
    let updatePostCategories postId oldCats newCats = backgroundTask {
        let toDelete, toAdd = diffLists oldCats newCats CategoryId.toString
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@categoryId", SqliteType.Text)
            ] |> ignore
            let runCmd catId = backgroundTask {
                cmd.Parameters["@categoryId"].Value <- CategoryId.toString catId
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_category WHERE post_id = @postId AND category_id = @categoryId" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_category VALUES (@postId, @categoryId)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    
    /// Update a post's assigned categories
    let updatePostTags postId (oldTags : string list) newTags = backgroundTask {
        let toDelete, toAdd = diffLists oldTags newTags id
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@tag", SqliteType.Text)
            ] |> ignore
            let runCmd (tag : string) = backgroundTask {
                cmd.Parameters["@tag"].Value <- tag
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_tag WHERE post_id = @postId AND tag = @tag" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_tag VALUES (@postId, @tag)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a post's metadata items
    let updatePostMeta postId oldItems newItems = backgroundTask {
        let toDelete, toAdd = diffMetaItems oldItems newItems
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@name", SqliteType.Text)
              cmd.Parameters.Add ("@value", SqliteType.Text)
            ] |> ignore
            let runCmd (item : MetaItem) = backgroundTask {
                cmd.Parameters["@name" ].Value <- item.name
                cmd.Parameters["@value"].Value <- item.value
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_meta WHERE post_id = @postId AND name = @name AND value = @value" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_meta VALUES (@postId, @name, @value)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a post's prior permalinks
    let updatePostPermalinks postId oldLinks newLinks = backgroundTask {
        let toDelete, toAdd = diffPermalinks oldLinks newLinks
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
              cmd.Parameters.Add ("@link", SqliteType.Text)
            ] |> ignore
            let runCmd link = backgroundTask {
                cmd.Parameters["@link"].Value <- Permalink.toString link
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_permalink WHERE post_id = @postId AND permalink = @link" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_permalink VALUES (@postId, @link)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a post's revisions
    let updatePostRevisions postId oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = diffRevisions oldRevs newRevs
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            let runCmd withText rev = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                  cmd.Parameters.AddWithValue ("@asOf", rev.asOf)
                ] |> ignore
                if withText then cmd.Parameters.AddWithValue ("@text", MarkupText.toString rev.text) |> ignore
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM post_revision WHERE post_id = @postId AND as_of = @asOf" 
            toDelete
            |> List.map (runCmd false)
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO post_revision VALUES (@postId, @asOf, @text)"
            toAdd
            |> List.map (runCmd true)
            |> Task.WhenAll
            |> ignore
    }
    
    /// Append custom feeds to a web log
    let appendCustomFeeds (webLog : WebLog) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            """SELECT f.*, p.*
                 FROM web_log_feed f
                      LEFT JOIN web_log_feed_podcast p ON p.feed_id = f.id
                WHERE f.web_log_id = @webLogId"""
        addWebLogId cmd webLog.id
        use! rdr = cmd.ExecuteReaderAsync ()
        return { webLog with rss = { webLog.rss with customFeeds = toList Map.toCustomFeed rdr } }
    }
    
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
    
        member _.Category = {
            new ICategoryData with
                
                member _.add cat = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """INSERT INTO category (
                               id, web_log_id, name, slug, description, parent_id
                           ) VALUES (
                               @id, @webLogId, @name, @slug, @description, @parentId
                           )"""
                    addCategoryParameters cmd cat
                    let! _ = cmd.ExecuteNonQueryAsync ()
                    ()
                }
                
                member _.countAll webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT COUNT(id) FROM category WHERE web_log_id = @webLogId"
                    addWebLogId cmd webLogId
                    return! count cmd
                }

                member _.countTopLevel webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        "SELECT COUNT(id) FROM category WHERE web_log_id = @webLogId AND parent_id IS NULL"
                    addWebLogId cmd webLogId
                    return! count cmd
                }
                
                member _.findAllForView webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM category WHERE web_log_id = @webLogId"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let cats =
                        seq {
                            while rdr.Read () do
                                Map.toCategory rdr
                        }
                        |> Seq.sortBy (fun cat -> cat.name.ToLowerInvariant ())
                        |> List.ofSeq
                    do! rdr.CloseAsync ()
                    let  ordered = Utils.orderByHierarchy cats None None []
                    let! counts  =
                        ordered
                        |> Seq.map (fun it -> backgroundTask {
                            // Parent category post counts include posts in subcategories
                            cmd.Parameters.Clear ()
                            addWebLogId cmd webLogId
                            cmd.CommandText <-
                                """SELECT COUNT(DISTINCT p.id)
                                     FROM post p
                                          INNER JOIN post_category pc ON pc.post_id = p.id
                                    WHERE p.web_log_id = @webLogId
                                      AND p.status     = 'Published'
                                      AND pc.category_id IN ("""
                            ordered
                            |> Seq.filter (fun cat -> cat.parentNames |> Array.contains it.name)
                            |> Seq.map (fun cat -> cat.id)
                            |> Seq.append (Seq.singleton it.id)
                            |> Seq.iteri (fun idx item ->
                                if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
                                cmd.CommandText <- $"{cmd.CommandText}@catId{idx}"
                                cmd.Parameters.AddWithValue ($"@catId{idx}", item) |> ignore)
                            cmd.CommandText <- $"{cmd.CommandText})"
                            let! postCount = count cmd
                            return it.id, postCount
                            })
                        |> Task.WhenAll
                    return
                        ordered
                        |> Seq.map (fun cat ->
                            { cat with
                                postCount = counts
                                            |> Array.tryFind (fun c -> fst c = cat.id)
                                            |> Option.map snd
                                            |> Option.defaultValue 0
                            })
                        |> Array.ofSeq
                }
                
                member _.findById catId webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM category WHERE id = @id"
                    cmd.Parameters.AddWithValue ("@id", CategoryId.toString catId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return verifyWebLog<Category> webLogId (fun c -> c.webLogId) Map.toCategory rdr
                }
                
                member _.findByWebLog webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM category WHERE web_log_id = @webLogId"
                    cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList Map.toCategory rdr
                }
                
                member this.delete catId webLogId = backgroundTask {
                    match! this.findById catId webLogId with
                    | Some _ ->
                        use cmd = conn.CreateCommand ()
                        // Delete the category off all posts where it is assigned
                        cmd.CommandText <-
                            """DELETE FROM post_category
                                WHERE category_id = @id
                                  AND post_id IN (SELECT id FROM post WHERE web_log_id = @webLogId)"""
                        let catIdParameter = cmd.Parameters.AddWithValue ("@id", CategoryId.toString catId)
                        cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore
                        do! write cmd
                        // Delete the category itself
                        cmd.CommandText <- "DELETE FROM category WHERE id = @id"
                        cmd.Parameters.Clear ()
                        cmd.Parameters.Add catIdParameter |> ignore
                        do! write cmd
                        return true
                    | None -> return false
                }
                
                member this.restore cats = backgroundTask {
                    for cat in cats do
                        do! this.add cat
                }
                
                member _.update cat = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """UPDATE category
                              SET name        = @name,
                                  slug        = @slug,
                                  description = @description,
                                  parent_id   = @parentId
                            WHERE id         = @id
                              AND web_log_id = @webLogId"""
                    addCategoryParameters cmd cat
                    do! write cmd
                }
        }
        
        member _.Page = {
            new IPageData with
                
                member _.add page = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    // The page itself
                    cmd.CommandText <-
                        """INSERT INTO page (
                               id, web_log_id, author_id, title, permalink, published_on, updated_on, show_in_page_list,
                               template, page_text
                           ) VALUES (
                               @id, @webLogId, @authorId, @title, @permalink, @publishedOn, @updatedOn, @showInPageList,
                               @template, @text
                           )"""
                    addPageParameters cmd page
                    do! write cmd
                    do! updatePageMeta       page.id [] page.metadata
                    do! updatePagePermalinks page.id [] page.priorPermalinks
                    do! updatePageRevisions  page.id [] page.revisions
                }

                member _.all webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM page WHERE web_log_id = @webLogId ORDER BY LOWER(title)"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList pageWithoutTextOrMeta rdr
                }
                
                member _.countAll webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT COUNT(id) FROM page WHERE web_log_id = @webLogId"
                    addWebLogId cmd webLogId
                    return! count cmd
                }

                member _.countListed webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT COUNT(id)
                             FROM page
                            WHERE web_log_id        = @webLogId
                              AND show_in_page_list = @showInPageList"""
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@showInPageList", true) |> ignore
                    return! count cmd
                }

                member _.findById pageId webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM page WHERE id = @id"
                    cmd.Parameters.AddWithValue ("@id", PageId.toString pageId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    match verifyWebLog<Page> webLogId (fun it -> it.webLogId) Map.toPage rdr with
                    | Some page ->
                        let! page = appendPageMeta page
                        return Some page
                    | None -> return None
                }

                member this.findFullById pageId webLogId = backgroundTask {
                    match! this.findById pageId webLogId with
                    | Some page ->
                        let! page = appendPageRevisionsAndPermalinks page
                        return Some page
                    | None -> return None
                }
                
                member this.delete pageId webLogId = backgroundTask {
                    match! this.findById pageId webLogId with
                    | Some _ ->
                        use cmd = conn.CreateCommand ()
                        cmd.Parameters.AddWithValue ("@id", PageId.toString pageId) |> ignore
                        cmd.CommandText <- "DELETE FROM page_revision WHERE page_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM page_permalink WHERE page_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM page_meta WHERE page_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM page WHERE id = @id"
                        do! write cmd
                        return true
                    | None -> return false
                }
                
                member _.findByPermalink permalink webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM page WHERE web_log_id = @webLogId AND permalink = @link"
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@link", Permalink.toString permalink) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    if rdr.Read () then
                        let! page = appendPageMeta (Map.toPage rdr)
                        return Some page
                    else
                        return None
                }
                
                member _.findCurrentPermalink permalinks webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT p.permalink
                             FROM page p
                                  INNER JOIN page_permalink pp ON pp.page_id = p.id
                            WHERE p.web_log_id = @webLogId
                              AND pp.permalink IN ("""
                    permalinks
                    |> List.iteri (fun idx link ->
                        if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
                        cmd.CommandText <- $"{cmd.CommandText}@link{idx}"
                        cmd.Parameters.AddWithValue ($"@link{idx}", Permalink.toString link) |> ignore)
                    cmd.CommandText <- $"{cmd.CommandText})"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return if rdr.Read () then Some (Map.toPermalink rdr) else None
                }
                
                member _.findFullByWebLog webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM page WHERE web_log_id = @webLogId"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! pages =
                        toList Map.toPage rdr
                        |> List.map (fun page -> backgroundTask {
                            let! page = appendPageMeta page
                            return! appendPageRevisionsAndPermalinks page
                        })
                        |> Task.WhenAll
                    return List.ofArray pages
                }
                
                member _.findListed webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT *
                             FROM page
                            WHERE web_log_id        = @webLogId
                              AND show_in_page_list = @showInPageList
                            ORDER BY LOWER(title)"""
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@showInPageList", true) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! pages =
                        toList pageWithoutTextOrMeta rdr
                        |> List.map (fun page -> backgroundTask { return! appendPageMeta page })
                        |> Task.WhenAll
                    return List.ofArray pages
                }

                member _.findPageOfPages webLogId pageNbr = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT *
                             FROM page
                            WHERE web_log_id = @webLogId
                            ORDER BY LOWER(title)
                            LIMIT @pageSize OFFSET @toSkip"""
                    addWebLogId cmd webLogId
                    [ cmd.Parameters.AddWithValue ("@pageSize", 26)
                      cmd.Parameters.AddWithValue ("@toSkip", (pageNbr - 1) * 25)
                    ] |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList Map.toPage rdr
                }
                
                member this.restore pages = backgroundTask {
                    for page in pages do
                        do! this.add page
                }
                
                member this.update page = backgroundTask {
                    match! this.findFullById page.id page.webLogId with
                    | Some oldPage ->
                        use cmd = conn.CreateCommand ()
                        cmd.CommandText <-
                            """UPDATE page
                                  SET author_id         = @authorId,
                                      title             = @title,
                                      permalink         = @permalink,
                                      published_on      = @publishedOn,
                                      updated_on        = @updatedOn,
                                      show_in_page_list = @showInPageList,
                                      template          = @template,
                                      page_text         = @text
                                WHERE id         = @pageId
                                  AND web_log_id = @webLogId"""
                        addPageParameters cmd page
                        do! write cmd
                        do! updatePageMeta       page.id oldPage.metadata        page.metadata
                        do! updatePagePermalinks page.id oldPage.priorPermalinks page.priorPermalinks
                        do! updatePageRevisions  page.id oldPage.revisions       page.revisions
                        return ()
                    | None -> return ()
                }
                
                member this.updatePriorPermalinks pageId webLogId permalinks = backgroundTask {
                    match! this.findFullById pageId webLogId with
                    | Some page ->
                        do! updatePagePermalinks pageId page.priorPermalinks permalinks
                        return true
                    | None -> return false
                }
        }
        
        member _.Post = {
            new IPostData with
                
                member _.add post = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """INSERT INTO post (
                               id, web_log_id, author_id, status, title, permalink, published_on, updated_on,
                               template, post_text
                           ) VALUES (
                               @id, @webLogId, @authorId, @status, @title, @permalink, @publishedOn, @updatedOn,
                               @template, @text
                           )"""
                    addPostParameters cmd post
                    do! write cmd
                    do! updatePostCategories post.id [] post.categoryIds
                    do! updatePostTags       post.id [] post.tags
                    do! updatePostMeta       post.id [] post.metadata
                    do! updatePostPermalinks post.id [] post.priorPermalinks
                    do! updatePostRevisions  post.id [] post.revisions
                }
                
                member _.countByStatus status webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        "SELECT COUNT(id) FROM post WHERE web_log_id = @webLogId AND status = @status"
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@status", PostStatus.toString status) |> ignore
                    return! count cmd
                }
                
                member _.findByPermalink permalink webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM post WHERE web_log_id = @webLogId AND permalink = @link"
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@link", Permalink.toString permalink) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    if rdr.Read () then
                        let! post = appendPostCategoryTagAndMeta (Map.toPost rdr)
                        return Some post
                    else
                        return None
                }
                
                member _.findFullById postId webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM post WHERE id = @id"
                    cmd.Parameters.AddWithValue ("@id", PostId.toString postId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    match verifyWebLog<Post> webLogId (fun p -> p.webLogId) Map.toPost rdr with
                    | Some post ->
                        let! post = appendPostCategoryTagAndMeta     post
                        let! post = appendPostRevisionsAndPermalinks post
                        return Some post
                    | None ->
                        return None
                }

                member this.delete postId webLogId = backgroundTask {
                    match! this.findFullById postId webLogId with
                    | Some _ ->
                        use cmd = conn.CreateCommand ()
                        cmd.Parameters.AddWithValue ("@id", PostId.toString postId) |> ignore
                        cmd.CommandText <- "DELETE FROM post_revision WHERE post_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM post_permalink WHERE post_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM post_meta WHERE post_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM post_tag WHERE post_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM post_category WHERE post_id = @id"
                        do! write cmd
                        cmd.CommandText <- "DELETE FROM post WHERE id = @id"
                        do! write cmd
                        return true
                    | None -> return false
                }
                
                member _.findCurrentPermalink permalinks webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT p.permalink
                             FROM post p
                                  INNER JOIN post_permalink pp ON pp.post_id = p.id
                            WHERE p.web_log_id = @webLogId
                              AND pp.permalink IN ("""
                    permalinks
                    |> List.iteri (fun idx link ->
                        if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
                        cmd.CommandText <- $"{cmd.CommandText}@link{idx}"
                        cmd.Parameters.AddWithValue ($"@link{idx}", Permalink.toString link) |> ignore)
                    cmd.CommandText <- $"{cmd.CommandText})"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return if rdr.Read () then Some (Map.toPermalink rdr) else None
                }

                member _.findFullByWebLog webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM post WHERE web_log_id = @webLogId"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! posts =
                        toList Map.toPost rdr
                        |> List.map (fun post -> backgroundTask {
                            let! post = appendPostCategoryTagAndMeta post
                            return! appendPostRevisionsAndPermalinks post
                        })
                        |> Task.WhenAll
                    return List.ofArray posts
                }

                member _.findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT p.*
                             FROM post p
                                  INNER JOIN post_category pc ON pc.post_id = p.id
                            WHERE p.web_log_id = @webLogId
                              AND p.status     = @status
                              AND pc.category_id IN ("""
                    categoryIds
                    |> List.iteri (fun idx catId ->
                        if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
                        cmd.CommandText <- $"{cmd.CommandText}@catId{idx}"
                        cmd.Parameters.AddWithValue ($"@catId{idx}", CategoryId.toString catId) |> ignore)
                    cmd.CommandText <-
                        $"""{cmd.CommandText})
                            ORDER BY published_on DESC
                            LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! posts =
                        toList Map.toPost rdr
                        |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
                        |> Task.WhenAll
                    return List.ofArray posts
                }
                
                member _.findPageOfPosts webLogId pageNbr postsPerPage = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        $"""SELECT p.*
                              FROM post p
                             WHERE p.web_log_id = @webLogId
                             ORDER BY published_on DESC NULLS FIRST, updated_on
                             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! posts =
                        toList postWithoutText rdr
                        |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
                        |> Task.WhenAll
                    return List.ofArray posts
                }

                member _.findPageOfPublishedPosts webLogId pageNbr postsPerPage = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        $"""SELECT p.*
                              FROM post p
                             WHERE p.web_log_id = @webLogId
                               AND p.status     = @status
                             ORDER BY published_on DESC
                             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! posts =
                        toList Map.toPost rdr
                        |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
                        |> Task.WhenAll
                    return List.ofArray posts
                }
                
                member _.findPageOfTaggedPosts webLogId tag pageNbr postsPerPage = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        $"""SELECT p.*
                              FROM post p
                                   INNER JOIN post_tag pt ON pt.post_id = p.id
                             WHERE p.web_log_id = @webLogId
                               AND p.status     = @status
                               AND pt.tag       = @tag
                             ORDER BY published_on DESC
                             LIMIT {postsPerPage + 1} OFFSET {(pageNbr - 1) * postsPerPage}"""
                    addWebLogId cmd webLogId
                    [ cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published)
                      cmd.Parameters.AddWithValue ("@tag", tag)
                    ] |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! posts =
                        toList Map.toPost rdr
                        |> List.map (fun post -> backgroundTask { return! appendPostCategoryTagAndMeta post })
                        |> Task.WhenAll
                    return List.ofArray posts
                }
                
                member _.findSurroundingPosts webLogId publishedOn = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT *
                             FROM post
                            WHERE web_log_id   = @webLogId
                              AND status       = @status
                              AND published_on < @publishedOn
                            ORDER BY published_on DESC
                            LIMIT 1"""
                    addWebLogId cmd webLogId
                    [ cmd.Parameters.AddWithValue ("@status", PostStatus.toString Published)
                      cmd.Parameters.AddWithValue ("@publishedOn", publishedOn)
                    ] |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! older = backgroundTask {
                        if rdr.Read () then
                            let! post = appendPostCategoryTagAndMeta (postWithoutText rdr)
                            return Some post
                        else
                            return None
                    }
                    do! rdr.CloseAsync ()
                    cmd.CommandText <-
                        """SELECT *
                             FROM post
                            WHERE web_log_id   = @webLogId
                              AND status       = @status
                              AND published_on > @publishedOn
                            ORDER BY published_on
                            LIMIT 1"""
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! newer = backgroundTask {
                        if rdr.Read () then
                            let! post = appendPostCategoryTagAndMeta (postWithoutText rdr)
                            return Some post
                        else
                            return None
                    }
                    return older, newer
                }
                
                member this.restore posts = backgroundTask {
                    for post in posts do
                        do! this.add post
                }
                
                member this.update post = backgroundTask {
                    match! this.findFullById post.id post.webLogId with
                    | Some oldPost ->
                        use cmd = conn.CreateCommand ()
                        cmd.CommandText <-
                            """UPDATE post
                                  SET author_id    = @author_id,
                                      status       = @status,
                                      title        = @title,
                                      permalink    = @permalink,
                                      published_on = @publishedOn,
                                      updated_on   = @updatedOn,
                                      template     = @template,
                                      post_text    = @text
                                WHERE id         = @id
                                  AND web_log_id = @webLogId"""
                        addPostParameters cmd post
                        do! write cmd
                        do! updatePostCategories post.id oldPost.categoryIds     post.categoryIds
                        do! updatePostTags       post.id oldPost.tags            post.tags
                        do! updatePostMeta       post.id oldPost.metadata        post.metadata
                        do! updatePostPermalinks post.id oldPost.priorPermalinks post.priorPermalinks
                        do! updatePostRevisions  post.id oldPost.revisions       post.revisions
                    | None -> return ()
                }

                member this.updatePriorPermalinks postId webLogId permalinks = backgroundTask {
                    match! this.findFullById postId webLogId with
                    | Some post ->
                        do! updatePostPermalinks postId post.priorPermalinks permalinks
                        return true
                    | None -> return false
                }
        }
        
        member _.TagMap = {
            new ITagMapData with
                
                member _.findById tagMapId webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM tag_map WHERE id = @id"
                    cmd.Parameters.AddWithValue ("@id", TagMapId.toString tagMapId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return verifyWebLog<TagMap> webLogId (fun tm -> tm.webLogId) Map.toTagMap rdr
                }
                
                member this.delete tagMapId webLogId = backgroundTask {
                    match! this.findById tagMapId webLogId with
                    | Some _ ->
                        use cmd = conn.CreateCommand ()
                        cmd.CommandText <- "DELETE FROM tag_map WHERE id = @id"
                        cmd.Parameters.AddWithValue ("@id", TagMapId.toString tagMapId) |> ignore
                        do! write cmd
                        return true
                    | None -> return false
                }
                
                member _.findByUrlValue urlValue webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM tag_map WHERE web_log_id = @webLogId AND url_value = @urlValue"
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@urlValue", urlValue) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return if rdr.Read () then Some (Map.toTagMap rdr) else None
                }
                
                member _.findByWebLog webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM tag_map WHERE web_log_id = @webLogId ORDER BY tag"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList Map.toTagMap rdr
                }
                
                member _.findMappingForTags tags webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """SELECT *
                             FROM tag_map
                            WHERE web_log_id = @webLogId
                              AND tag IN ("""
                    tags
                    |> List.iteri (fun idx tag ->
                        if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
                        cmd.CommandText <- $"{cmd.CommandText}@tag{idx}"
                        cmd.Parameters.AddWithValue ($"@tag{idx}", tag) |> ignore)
                    cmd.CommandText <- $"{cmd.CommandText})"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList Map.toTagMap rdr
                }
                
                member this.save tagMap = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    match! this.findById tagMap.id tagMap.webLogId with
                    | Some _ ->
                        cmd.CommandText <-
                            """UPDATE tag_map
                                  SET tag       = @tag,
                                      url_value = @urlValue
                                WHERE id         = @id
                                  AND web_log_id = @webLogId"""
                    | None ->
                        cmd.CommandText <-
                            """INSERT INTO tag_map (
                                   id, web_log_id, tag, url_value
                               ) VALUES (
                                   @id, @webLogId, @tag, @urlValue
                               )"""
                    addWebLogId cmd tagMap.webLogId
                    [ cmd.Parameters.AddWithValue ("@id", TagMapId.toString tagMap.id)
                      cmd.Parameters.AddWithValue ("@tag", tagMap.tag)
                      cmd.Parameters.AddWithValue ("@urlValue", tagMap.urlValue)
                    ] |> ignore
                    do! write cmd
                }
                
                member this.restore tagMaps = backgroundTask {
                    for tagMap in tagMaps do
                        do! this.save tagMap
                }
        }
        
        member _.Theme = {
            new IThemeData with
                
                member _.all () = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM theme WHERE id <> 'admin' ORDER BY id"
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList Map.toTheme rdr
                }
                
                member _.findById themeId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM theme WHERE id = @id"
                    cmd.Parameters.AddWithValue ("@id", ThemeId.toString themeId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    if rdr.Read () then
                        let theme = Map.toTheme rdr
                        let templateCmd = conn.CreateCommand ()
                        templateCmd.CommandText <- "SELECT * FROM theme_template WHERE theme_id = @id"
                        templateCmd.Parameters.Add cmd.Parameters["@id"] |> ignore
                        use! templateRdr = templateCmd.ExecuteReaderAsync ()
                        return Some { theme with templates = toList Map.toThemeTemplate templateRdr }
                    else
                        return None
                }
                
                member this.findByIdWithoutText themeId = backgroundTask {
                    match! this.findById themeId with
                    | Some theme ->
                        return Some {
                            theme with templates = theme.templates |> List.map (fun t -> { t with text = "" })
                        }
                    | None -> return None
                }
                
                member this.save theme = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    let! oldTheme = this.findById theme.id
                    cmd.CommandText <-
                        match oldTheme with
                        | Some _ -> "UPDATE theme SET name = @name, version = @version WHERE id = @id"
                        | None -> "INSERT INTO theme VALUES (@id, @name, @version)"
                    [ cmd.Parameters.AddWithValue ("@id", ThemeId.toString theme.id)
                      cmd.Parameters.AddWithValue ("@name", theme.name)
                      cmd.Parameters.AddWithValue ("@version", theme.version)
                    ] |> ignore
                    do! write cmd
                    
                    let toDelete, toAdd =
                        diffLists (oldTheme |> Option.map (fun t -> t.templates) |> Option.defaultValue [])
                                  theme.templates (fun t -> t.name)
                    let toUpdate =
                        theme.templates
                        |> List.filter (fun t ->
                            not (toDelete |> List.exists (fun d -> d.name = t.name))
                            && not (toAdd |> List.exists (fun a -> a.name = t.name)))
                    cmd.CommandText <-
                        "UPDATE theme_template SET template = @template WHERE theme_id = @themeId AND name = @name"
                    cmd.Parameters.Clear ()
                    [ cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString theme.id)
                      cmd.Parameters.Add ("@name", SqliteType.Text)
                      cmd.Parameters.Add ("@template", SqliteType.Text)
                    ] |> ignore
                    toUpdate
                    |> List.map (fun template -> backgroundTask {
                        cmd.Parameters["@name"    ].Value <- template.name
                        cmd.Parameters["@template"].Value <- template.text
                        do! write cmd
                    })
                    |> Task.WhenAll
                    |> ignore
                    cmd.CommandText <- "INSERT INTO theme_template VALUES (@themeId, @name, @template)"
                    toAdd
                    |> List.map (fun template -> backgroundTask {
                        cmd.Parameters["@name"    ].Value <- template.name
                        cmd.Parameters["@template"].Value <- template.text
                        do! write cmd
                    })
                    |> Task.WhenAll
                    |> ignore
                    cmd.CommandText <- "DELETE FROM theme_template WHERE theme_id = @themeId AND name = @name"
                    cmd.Parameters.Remove cmd.Parameters["@template"]
                    toDelete
                    |> List.map (fun template -> backgroundTask {
                        cmd.Parameters["@name"].Value <- template.name
                        do! write cmd
                    })
                    |> Task.WhenAll
                    |> ignore
                }
        }
        
        member _.ThemeAsset = {
            new IThemeAssetData with
                
                member _.all () = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT theme_id, path, updated_on FROM theme_asset"
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList (Map.toThemeAsset false) rdr
                }
                
                member _.deleteByTheme themeId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "DELETE FROM theme_asset WHERE theme_id = @themeId"
                    cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString themeId) |> ignore
                    do! write cmd
                }
                
                member _.findById assetId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT *, ROWID FROM theme_asset WHERE theme_id = @themeId AND path = @path"
                    let (ThemeAssetId (ThemeId themeId, path)) = assetId
                    [ cmd.Parameters.AddWithValue ("@themeId", themeId)
                      cmd.Parameters.AddWithValue ("@path", path)
                    ] |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return if rdr.Read () then Some (Map.toThemeAsset true rdr) else None
                }
                
                member _.findByTheme themeId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT theme_id, path, updated_on FROM theme_asset WHERE theme_id = @themeId"
                    cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString themeId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList (Map.toThemeAsset false) rdr
                }
                
                member _.findByThemeWithData themeId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT *, ROWID FROM theme_asset WHERE theme_id = @themeId"
                    cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString themeId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList (Map.toThemeAsset true) rdr
                }
                
                member _.save asset = backgroundTask {
                    use sideCmd = conn.CreateCommand ()
                    sideCmd.CommandText <-
                        "SELECT COUNT(path) FROM theme_asset WHERE theme_id = @themeId AND path = @path"
                    let (ThemeAssetId (ThemeId themeId, path)) = asset.id
                    [ sideCmd.Parameters.AddWithValue ("@themeId", themeId)
                      sideCmd.Parameters.AddWithValue ("@path", path)
                    ] |> ignore
                    let! exists = count sideCmd
                    
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        if exists = 1 then
                            """UPDATE theme_asset
                                  SET updated_on = @updatedOn,
                                      data       = ZEROBLOB(@dataLength)
                                WHERE theme_id = @themeId
                                  AND path     = @path"""
                        else
                            """INSERT INTO theme_asset (
                                   theme_id, path, updated_on, data
                               ) VALUES (
                                   @themeId, @path, @updatedOn, ZEROBLOB(@dataLength)
                               )"""
                    [ cmd.Parameters.AddWithValue ("@themeId", themeId)
                      cmd.Parameters.AddWithValue ("@path", path)
                      cmd.Parameters.AddWithValue ("@updatedOn", asset.updatedOn)
                      cmd.Parameters.AddWithValue ("@dataLength", asset.data.Length)
                    ] |> ignore
                    do! write cmd
                    
                    sideCmd.CommandText <- "SELECT ROWID FROM theme_asset WHERE theme_id = @themeId AND path = @path"
                    let! rowId = sideCmd.ExecuteScalarAsync ()
                    
                    use dataStream = new MemoryStream (asset.data)
                    use blobStream = new SqliteBlob (conn, "theme_asset", "data", rowId :?> int64)
                    do! dataStream.CopyToAsync blobStream
                }
        }
        
        member _.WebLog = {
            new IWebLogData with
                
                member _.add webLog = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """INSERT INTO web_log (
                               id, name, subtitle, default_page, posts_per_page, theme_id, url_base, time_zone,
                               auto_htmx, feed_enabled, feed_name, items_in_feed, category_enabled, tag_enabled,
                               copyright
                           ) VALUES (
                               @id, @name, @subtitle, @defaultPage, @postsPerPage, @themeId, @urlBase, @timeZone,
                               @autoHtmx, @feedEnabled, @feedName, @itemsInFeed, @categoryEnabled, @tagEnabled,
                               @copyright
                           )"""
                    addWebLogParameters cmd webLog
                    do! write cmd
                    webLog.rss.customFeeds
                    |> List.map (fun feed -> backgroundTask {
                        cmd.CommandText <-
                            """INSERT INTO web_log_feed (
                                   id, web_log_id, source, path
                               ) VALUES (
                                   @id, @webLogId, @source, @path
                               )"""
                        cmd.Parameters.Clear ()
                        [ cmd.Parameters.AddWithValue ("@id", CustomFeedId.toString feed.id)
                          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLog.id)
                          cmd.Parameters.AddWithValue ("@source", CustomFeedSource.toString feed.source)
                          cmd.Parameters.AddWithValue ("@path", Permalink.toString feed.path)
                        ] |> ignore
                        do! write cmd
                        match feed.podcast with
                        | Some podcast ->
                            cmd.CommandText <-
                                """INSERT INTO web_log_feed_podcast (
                                       feed_id, title, subtitle, items_in_feed, summary, displayed_author, email,
                                       image_url, itunes_category, itunes_subcategory, explicit, default_media_type,
                                       media_base_url
                                   ) VALUES (
                                       @feedId, @title, @subtitle, @itemsInFeed, @summary, @displayedAuthor, @email,
                                       @imageUrl, @iTunesCategory, @iTunesSubcategory, @explicit, @defaultMediaType,
                                       @mediaBaseUrl
                                   )"""
                            cmd.Parameters.Clear ()
                            [ cmd.Parameters.AddWithValue ("@feedId", CustomFeedId.toString feed.id)
                              cmd.Parameters.AddWithValue ("@title", podcast.title)
                              cmd.Parameters.AddWithValue ("@subtitle", maybe podcast.subtitle)
                              cmd.Parameters.AddWithValue ("@itemsInFeed", podcast.itemsInFeed)
                              cmd.Parameters.AddWithValue ("@summary", podcast.summary)
                              cmd.Parameters.AddWithValue ("@displayedAuthor", podcast.displayedAuthor)
                              cmd.Parameters.AddWithValue ("@email", podcast.email)
                              cmd.Parameters.AddWithValue ("@imageUrl", Permalink.toString podcast.imageUrl)
                              cmd.Parameters.AddWithValue ("@iTunesCategory", podcast.iTunesCategory)
                              cmd.Parameters.AddWithValue ("@iTunesSubcategory", maybe podcast.iTunesSubcategory)
                              cmd.Parameters.AddWithValue ("@explicit", ExplicitRating.toString podcast.explicit)
                              cmd.Parameters.AddWithValue ("@defaultMediaType", maybe podcast.defaultMediaType)
                              cmd.Parameters.AddWithValue ("@mediaBaseUrl", maybe podcast.mediaBaseUrl)
                            ] |> ignore
                            do! write cmd
                        | None -> ()
                    })
                    |> Task.WhenAll
                    |> ignore
                }
                
                member _.all () = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM web_log"
                    use! rdr = cmd.ExecuteReaderAsync ()
                    let! webLogs =
                        toList Map.toWebLog rdr
                        |> List.map (fun webLog -> backgroundTask { return! appendCustomFeeds webLog })
                        |> Task.WhenAll
                    return List.ofArray webLogs
                }
                
                member _.delete webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    addWebLogId cmd webLogId
                    let subQuery table = $"(SELECT id FROM {table} WHERE web_log_id = @webLogId)"
                    let postSubQuery = subQuery "post"
                    let pageSubQuery = subQuery "page"
                    [ $"DELETE FROM post_comment WHERE post_id IN {postSubQuery}"
                      $"DELETE FROM post_revision WHERE post_id IN {postSubQuery}"
                      $"DELETE FROM post_tag WHERE post_id IN {postSubQuery}"
                      $"DELETE FROM post_category WHERE post_id IN {postSubQuery}"
                      $"DELETE FROM post_meta WHERE post_id IN {postSubQuery}"
                      "DELETE FROM post WHERE web_log_id = @webLogId"
                      $"DELETE FROM page_revision WHERE page_id IN {pageSubQuery}"
                      $"DELETE FROM page_meta WHERE page_id IN {pageSubQuery}"
                      "DELETE FROM page WHERE web_log_id = @webLogId"
                      "DELETE FROM category WHERE web_log_id = @webLogId"
                      "DELETE FROM tag_map WHERE web_log_id = @webLogId"
                      "DELETE FROM web_log_user WHERE web_log_id = @webLogId"
                      $"""DELETE FROM web_log_feed_podcast WHERE feed_id IN {subQuery "web_log_feed"}"""
                      "DELETE FROM web_log_feed WHERE web_log_id = @webLogId"
                      "DELETE FROM web_log WHERE id = @webLogId"
                    ]
                    |> List.map (fun query -> backgroundTask {
                        cmd.CommandText <- query
                        do! write cmd
                    })
                    |> Task.WhenAll
                    |> ignore
                }
                
                member _.findByHost url = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM web_log WHERE url_base = @urlBase"
                    cmd.Parameters.AddWithValue ("@urlBase", url) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    if rdr.Read () then
                        let! webLog = appendCustomFeeds (Map.toWebLog rdr)
                        return Some webLog
                    else
                        return None
                }

                member _.findById webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM web_log WHERE id = @webLogId"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    if rdr.Read () then
                        let! webLog = appendCustomFeeds (Map.toWebLog rdr)
                        return Some webLog
                    else
                        return None
                }
                
                member _.updateSettings webLog = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """UPDATE web_log
                              SET name             = @name,
                                  subtitle         = @subtitle,
                                  default_page     = @defaultPage,
                                  posts_per_page   = @postsPerPage,
                                  theme_id         = @themeId,
                                  url_base         = @urlBase,
                                  time_zone        = @timeZone,
                                  auto_htmx        = @autoHtmx,
                                  feed_enabled     = @feedEnabled,
                                  feed_name        = @feedName,
                                  items_in_feed    = @itemsInFeed,
                                  category_enabled = @categoryEnabled,
                                  tag_enabled      = @tagEnabled,
                                  copyright        = @copyright
                            WHERE id = @id"""
                    addWebLogParameters cmd webLog
                    do! write cmd
                }
                
                member this.updateRssOptions webLog = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """UPDATE web_log
                              SET feed_enabled     = @feedEnabled,
                                  feed_name        = @feedName,
                                  items_in_feed    = @itemsInFeed,
                                  category_enabled = @categoryEnabled,
                                  tag_enabled      = @tagEnabled,
                                  copyright        = @copyright
                            WHERE id = @id"""
                    addWebLogRssParameters cmd webLog
                    do! write cmd
                }
        }
        
        member _.WebLogUser = {
            new IWebLogUserData with
                
                member _.add user = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """INSERT INTO web_log_user (
                               id, web_log_id, user_name, first_name, last_name, preferred_name, password_hash, salt,
                               url, authorization_level
                           ) VALUES (
                               @id, @webLogId, @userName, @firstName, @lastName, @preferredName, @passwordHash, @salt,
                               @url, @authorizationLevel
                           )"""
                    addWebLogUserParameters cmd user
                    do! write cmd
                }
                
                member _.findByEmail email webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        "SELECT * FROM web_log_user WHERE web_log_id = @webLogId AND user_name = @userName"
                    addWebLogId cmd webLogId
                    cmd.Parameters.AddWithValue ("@userName", email) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return if rdr.Read () then Some (Map.toWebLogUser rdr) else None
                }
                
                member _.findById userId webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM web_log_user WHERE id = @id"
                    cmd.Parameters.AddWithValue ("@id", WebLogUserId.toString userId) |> ignore
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return verifyWebLog<WebLogUser> webLogId (fun u -> u.webLogId) Map.toWebLogUser rdr 
                }
                
                member _.findByWebLog webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM web_log_user WHERE web_log_id = @webLogId"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList Map.toWebLogUser rdr
                }
                
                member _.findNames webLogId userIds = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM web_log_user WHERE web_log_id = @webLogId AND id IN ("
                    userIds
                    |> List.iteri (fun idx userId ->
                        if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
                        cmd.CommandText <- $"{cmd.CommandText}@id{idx}"
                        cmd.Parameters.AddWithValue ($"@id{idx}", WebLogUserId.toString userId) |> ignore)
                    cmd.CommandText <- $"{cmd.CommandText})"
                    addWebLogId cmd webLogId
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return
                        toList Map.toWebLogUser rdr
                        |> List.map (fun u -> { name = WebLogUserId.toString u.id; value = WebLogUser.displayName u })
                }
                
                member this.restore users = backgroundTask {
                    for user in users do
                        do! this.add user
                }
                
                member _.update user = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        """UPDATE web_log_user
                              SET user_name           = @userName,
                                  first_name          = @firstName,
                                  last_name           = @lastName,
                                  preferred_name      = @preferredName,
                                  password_hash       = @passwordHash,
                                  salt                = @salt,
                                  url                 = @url,
                                  authorization_level = @authorizationLevel
                            WHERE id         = @id
                              AND web_log_id = @webLogId"""
                    addWebLogUserParameters cmd user
                    do! write cmd
                }
        }
        
        member _.startUp () = backgroundTask {

            let! exists = tableExists "theme"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE theme (
                        id       TEXT PRIMARY KEY,
                        name     TEXT NOT NULL,
                        version  TEXT NOT NULL)"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE theme_template (
                        theme_id  TEXT NOT NULL REFERENCES theme (id),
                        name      TEXT NOT NULL,
                        template  TEXT NOT NULL,
                        PRIMARY KEY (theme_id, name))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE theme_asset (
                        theme_id    TEXT NOT NULL REFERENCES theme (id),
                        path        TEXT NOT NULL,
                        updated_on  TEXT NOT NULL,
                        data        BLOB NOT NULL,
                        PRIMARY KEY (theme_id, path))"""
                do! write cmd
            
            let! exists = tableExists "web_log"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE web_log (
                        id                TEXT PRIMARY KEY,
                        name              TEXT NOT NULL,
                        subtitle          TEXT,
                        default_page      TEXT NOT NULL,
                        posts_per_page    INTEGER NOT NULL,
                        theme_id          TEXT NOT NULL REFERENCES theme (id),
                        url_base          TEXT NOT NULL,
                        time_zone         TEXT NOT NULL,
                        auto_htmx         INTEGER NOT NULL DEFAULT 0,
                        feed_enabled      INTEGER NOT NULL DEFAULT 0,
                        feed_name         TEXT NOT NULL,
                        items_in_feed     INTEGER,
                        category_enabled  INTEGER NOT NULL DEFAULT 0,
                        tag_enabled       INTEGER NOT NULL DEFAULT 0,
                        copyright         TEXT)"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE web_log_feed (
                        id          TEXT PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        source      TEXT NOT NULL,
                        path        TEXT NOT NULL)"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE web_log_feed_podcast (
                        feed_id             TEXT PRIMARY KEY REFERENCES web_log_feed (id),
                        title               TEXT NOT NULL,
                        subtitle            TEXT,
                        items_in_feed       INTEGER NOT NULL,
                        summary             TEXT NOT NULL,
                        displayed_author    TEXT NOT NULL,
                        email               TEXT NOT NULL,
                        image_url           TEXT NOT NULL,
                        itunes_category     TEXT NOT NULL,
                        itunes_subcategory  TEXT,
                        explicit            TEXT NOT NULL,
                        default_media_type  TEXT,
                        media_base_url      TEXT)"""
                do! write cmd
            
            let! exists = tableExists "category"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE category (
                        id          TEXT PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        name        TEXT NOT NULL,
                        slug        TEXT NOT NULL,
                        description TEXT,
                        parent_id   TEXT)"""
                do! write cmd
            
            let! exists = tableExists "web_log_user"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE web_log_user (
                        id                   TEXT PRIMARY KEY,
                        web_log_id           TEXT NOT NULL REFERENCES web_log (id),
                        user_name            TEXT NOT NULL,
                        first_name           TEXT NOT NULL,
                        last_name            TEXT NOT NULL,
                        preferred_name       TEXT NOT NULL,
                        password_hash        TEXT NOT NULL,
                        salt                 TEXT NOT NULL,
                        url                  TEXT,
                        authorization_level  TEXT NOT NULL)"""
                do! write cmd
            
            let! exists = tableExists "page"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE page (
                        id                 TEXT PRIMARY KEY,
                        web_log_id         TEXT NOT NULL REFERENCES web_log (id),
                        author_id          TEXT NOT NULL REFERENCES web_log_user (id),
                        title              TEXT NOT NULL,
                        permalink          TEXT NOT NULL,
                        published_on       TEXT NOT NULL,
                        updated_on         TEXT NOT NULL,
                        show_in_page_list  INTEGER NOT NULL DEFAULT 0,
                        template           TEXT,
                        page_text          TEXT NOT NULL)"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE page_meta (
                        page_id  TEXT NOT NULL REFERENCES page (id),
                        name     TEXT NOT NULL,
                        value    TEXT NOT NULL,
                        PRIMARY KEY (page_id, name, value))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE page_permalink (
                        page_id    TEXT NOT NULL REFERENCES page (id),
                        permalink  TEXT NOT NULL,
                        PRIMARY KEY (page_id, permalink))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE page_revision (
                        page_id        TEXT NOT NULL REFERENCES page (id),
                        as_of          TEXT NOT NULL,
                        revision_text  TEXT NOT NULL,
                        PRIMARY KEY (page_id, as_of))"""
                do! write cmd
            
            let! exists = tableExists "post"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE post (
                        id            TEXT PRIMARY KEY,
                        web_log_id    TEXT NOT NULL REFERENCES web_log (id),
                        author_id     TEXT NOT NULL REFERENCES web_log_user (id),
                        status        TEXT NOT NULL,
                        title         TEXT NOT NULL,
                        permalink     TEXT NOT NULL,
                        published_on  TEXT,
                        updated_on    TEXT NOT NULL,
                        template      TEXT,
                        post_text     TEXT NOT NULL)"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE post_category (
                        post_id      TEXT NOT NULL REFERENCES post (id),
                        category_id  TEXT NOT NULL REFERENCES category (id),
                        PRIMARY KEY (post_id, category_id))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE post_tag (
                        post_id  TEXT NOT NULL REFERENCES post (id),
                        tag      TEXT NOT NULL,
                        PRIMARY KEY (post_id, tag))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE post_meta (
                        post_id  TEXT NOT NULL REFERENCES post (id),
                        name     TEXT NOT NULL,
                        value    TEXT NOT NULL,
                        PRIMARY KEY (post_id, name, value))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE post_permalink (
                        post_id    TEXT NOT NULL REFERENCES post (id),
                        permalink  TEXT NOT NULL,
                        PRIMARY KEY (post_id, permalink))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE post_revision (
                        post_id        TEXT NOT NULL REFERENCES post (id),
                        as_of          TEXT NOT NULL,
                        revision_text  TEXT NOT NULL,
                        PRIMARY KEY (post_id, as_of))"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE post_comment (
                        id              TEXT PRIMARY KEY,
                        post_id         TEXT NOT NULL REFERENCES post(id),
                        in_reply_to_id  TEXT,
                        name            TEXT NOT NULL,
                        email           TEXT NOT NULL,
                        url             TEXT,
                        status          TEXT NOT NULL,
                        posted_on       TEXT NOT NULL,
                        comment_text    TEXT NOT NULL)"""
                do! write cmd
            
            let! exists = tableExists "tag_map"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE tag_map (
                        id          TEXT PRIMARY KEY,
                        web_log_id  TEXT NOT NULL REFERENCES web_log (id),
                        tag         TEXT NOT NULL,
                        url_value   TEXT NOT NULL)"""
                do! write cmd
        }
