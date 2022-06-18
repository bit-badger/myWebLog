namespace MyWebLog.Data

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.ViewModels

[<AutoOpen>]
module private SqliteHelpers =
    do ()
    
    /// Run a command that returns a count
    let count (cmd : SqliteCommand) = backgroundTask {
        let! it = cmd.ExecuteScalarAsync ()
        return it :?> int
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
        
        /// Get a string value from a data reader
        let getString col (rdr : SqliteDataReader) = rdr.GetString (rdr.GetOrdinal col)
        
        /// Get a possibly null date/time value from a data reader
        let tryDateTime col (rdr : SqliteDataReader) =
            if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getDateTime col rdr)
        
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
                text           = getString "page_text" rdr
            }
        
        /// Create a revision from the current row in the given data reader
        let toRevision (rdr : SqliteDataReader) : Revision =
            { asOf = getDateTime "as_of" rdr
              text = MarkupText.parse (getString "revision_text" rdr)
            }
        
        
type SQLiteData (conn : SqliteConnection) =
    
//    /// Shorthand for accessing the collections in the LiteDB database
//    let Collection = {|
//        Category   = db.GetCollection<Category>   "Category"
//        Comment    = db.GetCollection<Comment>    "Comment"
//        Page       = db.GetCollection<Page>       "Page"
//        Post       = db.GetCollection<Post>       "Post"
//        TagMap     = db.GetCollection<TagMap>     "TagMap"
//        Theme      = db.GetCollection<Theme>      "Theme"
//        ThemeAsset = db.GetCollection<ThemeAsset> "ThemeAsset"
//        WebLog     = db.GetCollection<WebLog>     "WebLog"
//        WebLogUser = db.GetCollection<WebLogUser> "WebLogUser"
//    |}
    
    /// Add parameters for category INSERT or UPDATE statements
    let addCategoryParameters (cmd : SqliteCommand) (cat : Category) =
        [ cmd.Parameters.AddWithValue ("@id", CategoryId.toString cat.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString cat.webLogId)
          cmd.Parameters.AddWithValue ("@name", cat.name)
          cmd.Parameters.AddWithValue ("@slug", cat.slug)
          cmd.Parameters.AddWithValue ("@description",
            match cat.description with Some d -> d :> obj | None -> DBNull.Value)
          cmd.Parameters.AddWithValue ("@parentId",
            match cat.parentId with Some (CategoryId parentId) -> parentId :> obj | None -> DBNull.Value)
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
          cmd.Parameters.AddWithValue ("@template",
            match page.template with Some t -> t :> obj | None -> DBNull.Value)
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
          cmd.Parameters.AddWithValue ("@publishedOn",
            match post.publishedOn with Some p -> p :> obj | None -> DBNull.Value)
          cmd.Parameters.AddWithValue ("@updatedOn", post.updatedOn)
          cmd.Parameters.AddWithValue ("@template",
            match post.template with Some t -> t :> obj | None -> DBNull.Value)
          cmd.Parameters.AddWithValue ("@text", post.text)
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
        cmd.CommandText <- "SELECT permalink FROM page_permalink WHERE page_id = @pageId"
        cmd.Parameters.AddWithValue ("@pageId", PageId.toString page.id) |> ignore
        use! linkRdr = cmd.ExecuteReaderAsync ()
        let page = { page with priorPermalinks = toList Map.toPermalink linkRdr }
        
        cmd.CommandText <- "SELECT as_of, revision_text FROM page_revision WHERE page_id = @pageId ORDER BY as_of DESC"
        use! revRdr = cmd.ExecuteReaderAsync ()
        return { page with revisions = toList Map.toRevision revRdr }
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
            let runCmd (item : MetaItem) = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
                  cmd.Parameters.AddWithValue ("@name", item.name)
                  cmd.Parameters.AddWithValue ("@value", item.value)
                ] |> ignore
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
            let runCmd link = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
                  cmd.Parameters.AddWithValue ("@link", Permalink.toString link)
                ] |> ignore
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
        cmd.CommandText <- "SELECT category_id AS id FROM post_category WHERE post_id = @id"
        cmd.Parameters.AddWithValue ("@id", PostId.toString post.id) |> ignore
        use! catRdr = cmd.ExecuteReaderAsync ()
        let post = { post with categoryIds = toList Map.toCategoryId catRdr }
        
        cmd.CommandText <- "SELECT tag FROM post_tag WHERE post_id = @id"
        use! tagRdr = cmd.ExecuteReaderAsync ()
        let post = { post with tags = toList (Map.getString "tag") tagRdr }
        
        cmd.CommandText <- "SELECT name, value FROM post_meta WHERE post_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { post with metadata = toList Map.toMetaItem rdr }
    }
    
    /// Append revisions and permalinks to a post
    let appendPostRevisionsAndPermalinks (post : Post) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT permalink FROM post_permalink WHERE post_id = @postId"
        cmd.Parameters.AddWithValue ("@postId", PostId.toString post.id) |> ignore
        use! linkRdr = cmd.ExecuteReaderAsync ()
        let post = { post with priorPermalinks = toList Map.toPermalink linkRdr }
        
        cmd.CommandText <- "SELECT as_of, revision_text FROM post_revision WHERE post_id = @postId ORDER BY as_of DESC"
        use! revRdr = cmd.ExecuteReaderAsync ()
        return { post with revisions = toList Map.toRevision revRdr }
    }
    
    /// Return a post with no revisions or prior permalinks
    let postWithoutRevisions (post : Post) =
        { post with revisions = []; priorPermalinks = [] }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText post =
        { postWithoutRevisions post with text = "" }
    
    /// Update a post's assigned categories
    let updatePostCategories postId oldCats newCats = backgroundTask {
        let toDelete, toAdd = diffLists oldCats newCats CategoryId.toString
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            let runCmd catId = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                  cmd.Parameters.AddWithValue ("@categoryId", CategoryId.toString catId)
                ] |> ignore
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
    let updatePostTags postId oldTags newTags = backgroundTask {
        let toDelete, toAdd = diffLists oldTags newTags id
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            let runCmd tag = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                  cmd.Parameters.AddWithValue ("@tag", tag)
                ] |> ignore
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
            let runCmd (item : MetaItem) = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                  cmd.Parameters.AddWithValue ("@name", item.name)
                  cmd.Parameters.AddWithValue ("@value", item.value)
                ] |> ignore
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
            let runCmd link = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@postId", PostId.toString postId)
                  cmd.Parameters.AddWithValue ("@link", Permalink.toString link)
                ] |> ignore
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
    
    
    
    
    /// The connection for this instance
    member _.Conn = conn
    
    interface IData with
    
        member _.Category = {
            new ICategoryData with
                
                member _.add cat = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        "INSERT INTO category VALUES (@id, @webLogId, @name, @slug, @description, @parentId)"
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
                    if not rdr.IsClosed then do! rdr.CloseAsync ()
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
                
                member _.restore cats = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <-
                        "INSERT INTO category VALUES (@id, @webLogId, @name, @slug, @description, @parentId)"
                    for cat in cats do
                        cmd.Parameters.Clear ()
                        addCategoryParameters cmd cat
                        do! write cmd
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
                        """INSERT INTO page
                           VALUES (@id, @webLogId, @authorId, @title, @permalink, @publishedOn, @updatedOn,
                                   @showInPageList, @template, @text)"""
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
                        cmd.CommandText <- "DELETE FROM page_revision WHERE page_id = @id"
                        cmd.Parameters.AddWithValue ("@id", PageId.toString pageId) |> ignore
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
                      cmd.Parameters.AddWithValue ("@offset", pageNbr * 25)
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
                        """INSERT INTO post
                           VALUES (@id, @webLogId, @authorId, @status, @title, @permalink, @publishedOn, @updatedOn,
                                   @template, @text)"""
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
                        "SELECT COUNT(page_id) FROM page WHERE web_log_id = @webLogId AND status = @status"
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
                    if rdr.Read () then
                        match verifyWebLog<Post> webLogId (fun p -> p.webLogId) Map.toPost rdr with
                        | Some post ->
                            let! post = appendPostCategoryTagAndMeta     post
                            let! post = appendPostRevisionsAndPermalinks post
                            return Some post
                        | None ->
                            return None
                    else
                        return None
                }

                member this.delete postId webLogId = backgroundTask {
                    match! this.findFullById postId webLogId with
                    | Some _ ->
                        use cmd = conn.CreateCommand ()
                        cmd.CommandText <- "DELETE FROM post_revision WHERE post_id = @id"
                        cmd.Parameters.AddWithValue ("@id", PostId.toString postId) |> ignore
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

                member _.findPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage =
                    Collection.Post.Find (fun p ->
                        p.webLogId = webLogId
                        && p.status = Published
                        && p.categoryIds |> List.exists (fun cId -> categoryIds |> List.contains cId))
                    |> Seq.map postWithoutRevisions
                    |> Seq.sortByDescending (fun p -> p.publishedOn)
                    |> toPagedList pageNbr postsPerPage
                
                member _.findPageOfPosts webLogId pageNbr postsPerPage =
                    Collection.Post.Find (fun p -> p.webLogId = webLogId)
                    |> Seq.map postWithoutText
                    |> Seq.sortByDescending (fun p -> defaultArg p.publishedOn p.updatedOn)
                    |> toPagedList pageNbr postsPerPage

                member _.findPageOfPublishedPosts webLogId pageNbr postsPerPage =
                    Collection.Post.Find (fun p -> p.webLogId = webLogId && p.status = Published)
                    |> Seq.map postWithoutRevisions
                    |> Seq.sortByDescending (fun p -> p.publishedOn)
                    |> toPagedList pageNbr postsPerPage
                
                member _.findPageOfTaggedPosts webLogId tag pageNbr postsPerPage =
                    Collection.Post.Find (fun p ->
                        p.webLogId = webLogId && p.status = Published && p.tags |> List.contains tag)
                    |> Seq.map postWithoutRevisions
                    |> Seq.sortByDescending (fun p -> p.publishedOn)
                    |> toPagedList pageNbr postsPerPage
                
                member _.findSurroundingPosts webLogId publishedOn = backgroundTask {
                    let! older =
                        Collection.Post.Find (fun p ->
                            p.webLogId = webLogId && p.status = Published && p.publishedOn.Value < publishedOn)
                        |> Seq.map postWithoutText
                        |> Seq.sortByDescending (fun p -> p.publishedOn)
                        |> tryFirst
                    let! newer =
                        Collection.Post.Find (fun p ->
                            p.webLogId = webLogId && p.status = Published && p.publishedOn.Value > publishedOn)
                        |> Seq.map postWithoutText
                        |> Seq.sortBy (fun p -> p.publishedOn)
                        |> tryFirst
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
                
                member _.findById tagMapId webLogId =
                    Collection.TagMap.FindById (TagMapIdMapping.toBson tagMapId)
                    //|> verifyWebLog webLogId (fun tm -> tm.webLogId)
                
                member this.delete tagMapId webLogId = backgroundTask {
                    match! this.findById tagMapId webLogId with
                    | Some _ ->
                        let _ = Collection.TagMap.Delete (TagMapIdMapping.toBson tagMapId)
                        do! checkpoint ()
                        return true
                    | None -> return false
                }
                
                member _.findByUrlValue urlValue webLogId =
                    Collection.TagMap.Find (fun tm -> tm.webLogId = webLogId && tm.urlValue = urlValue)
                    |> tryFirst
                
                member _.findByWebLog webLogId =
                    Collection.TagMap.Find (fun tm -> tm.webLogId = webLogId)
                    |> Seq.sortBy (fun tm -> tm.tag)
                    |> toList
                
                member _.findMappingForTags tags webLogId =
                    Collection.TagMap.Find (fun tm -> tm.webLogId = webLogId && tags |> List.contains tm.tag)
                    |> toList
                
                member _.restore tagMaps = backgroundTask {
                    let _ = Collection.TagMap.InsertBulk tagMaps
                    do! checkpoint ()
                }
                
                member _.save tagMap = backgroundTask {
                    let _ = Collection.TagMap.Upsert tagMap
                    do! checkpoint ()
                }
        }
        
        member _.Theme = {
            new IThemeData with
                
                member _.all () =
                    Collection.Theme.Find (fun t -> t.id <> ThemeId "admin")
                    |> Seq.map (fun t -> { t with templates = [] })
                    |> Seq.sortBy (fun t -> t.id)
                    |> toList
                
                member _.findById themeId =
                    Collection.Theme.FindById (ThemeIdMapping.toBson themeId)
                    |> toOption
                
                member this.findByIdWithoutText themeId = backgroundTask {
                    match! this.findById themeId with
                    | Some theme ->
                        return Some {
                            theme with templates = theme.templates |> List.map (fun t -> { t with text = "" })
                        }
                    | None -> return None
                }
                
                member _.save theme = backgroundTask {
                    let _ = Collection.Theme.Upsert theme
                    do! checkpoint ()
                }
        }
        
        member _.ThemeAsset = {
            new IThemeAssetData with
                
                member _.all () =
                    Collection.ThemeAsset.FindAll ()
                    |> Seq.map (fun ta -> { ta with data = [||] })
                    |> toList
                
                member _.deleteByTheme themeId = backgroundTask {
                    (ThemeId.toString
                     >> sprintf "$.id LIKE '%s%%'"
                     >> BsonExpression.Create
                     >> Collection.ThemeAsset.DeleteMany) themeId
                    |> ignore
                    do! checkpoint ()
                }
                
                member _.findById assetId =
                    Collection.ThemeAsset.FindById (ThemeAssetIdMapping.toBson assetId)
                    |> toOption
                
                member _.findByTheme themeId =
                    Collection.ThemeAsset.Find (fun ta ->
                        (ThemeAssetId.toString ta.id).StartsWith (ThemeId.toString themeId))
                    |> Seq.map (fun ta -> { ta with data = [||] })
                    |> toList
                
                member _.findByThemeWithData themeId =
                    Collection.ThemeAsset.Find (fun ta ->
                        (ThemeAssetId.toString ta.id).StartsWith (ThemeId.toString themeId))
                    |> toList
                
                member _.save asset = backgroundTask {
                    let _ = Collection.ThemeAsset.Upsert asset
                    do! checkpoint ()
                }
        }
        
        member _.WebLog = {
            new IWebLogData with
                
                member _.add webLog = backgroundTask {
                    let _ = Collection.WebLog.Insert webLog
                    do! checkpoint ()
                }
                
                member _.all () =
                    Collection.WebLog.FindAll ()
                    |> toList
                
                member _.delete webLogId = backgroundTask {
                    let forWebLog = BsonExpression.Create $"$.webLogId = '{WebLogId.toString webLogId}'"
                    let _ = Collection.Comment.DeleteMany    forWebLog
                    let _ = Collection.Post.DeleteMany       forWebLog
                    let _ = Collection.Page.DeleteMany       forWebLog
                    let _ = Collection.Category.DeleteMany   forWebLog
                    let _ = Collection.TagMap.DeleteMany     forWebLog
                    let _ = Collection.WebLogUser.DeleteMany forWebLog
                    let _ = Collection.WebLog.Delete (WebLogIdMapping.toBson webLogId)
                    do! checkpoint ()
                }
                
                member _.findByHost url =
                    Collection.WebLog.Find (fun wl -> wl.urlBase = url)
                    |> tryFirst

                member _.findById webLogId =
                    Collection.WebLog.FindById (WebLogIdMapping.toBson webLogId)
                    |> toOption
                
                member _.updateSettings webLog = backgroundTask {
                    let _ = Collection.WebLog.Update webLog
                    do! checkpoint ()
                }
                
                member this.updateRssOptions webLog = backgroundTask {
                    match! this.findById webLog.id with
                    | Some wl -> do! this.updateSettings { wl with rss = webLog.rss }
                    | None -> ()
                }
        }
        
        member _.WebLogUser = {
            new IWebLogUserData with
                
                member _.add user = backgroundTask {
                    let _ = Collection.WebLogUser.Insert user
                    do! checkpoint ()
                }
                
                member _.findByEmail email webLogId =
                    Collection.WebLogUser.Find (fun wlu -> wlu.webLogId = webLogId && wlu.userName = email)
                    |> tryFirst
                
                member _.findById userId webLogId =
                    Collection.WebLogUser.FindById (WebLogUserIdMapping.toBson userId)
                    // |> verifyWebLog webLogId (fun u -> u.webLogId)
                
                member _.findByWebLog webLogId =
                    Collection.WebLogUser.Find (fun wlu -> wlu.webLogId = webLogId)
                    |> toList
                
                member _.findNames webLogId userIds =
                    Collection.WebLogUser.Find (fun wlu -> userIds |> List.contains wlu.id)
                    |> Seq.map (fun u -> { name = WebLogUserId.toString u.id; value = WebLogUser.displayName u })
                    |> toList
                
                member _.restore users = backgroundTask {
                    let _ = Collection.WebLogUser.InsertBulk users
                    do! checkpoint ()
                }
                
                member _.update user = backgroundTask {
                    let _ = Collection.WebLogUser.Update user
                    do! checkpoint ()
                }
        }
        
        member _.startUp () = backgroundTask {

            let tableExists table = backgroundTask {
                use cmd = conn.CreateCommand ()
                cmd.CommandText <- "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table"
                cmd.Parameters.AddWithValue ("@table", table) |> ignore
                let! count = cmd.ExecuteScalarAsync ()
                return (count :?> int) = 1
            }
            
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
                        data        BINARY NOT NULL,
                        PRIMARY KEY (theme_id, path))"""
                do! write cmd
            
            let! exists = tableExists "web_log"
            if not exists then
                use cmd = conn.CreateCommand ()
                cmd.CommandText <-
                    """CREATE TABLE web_log (
                        id            TEXT PRIMARY KEY,
                        name          TEXT NOT NULL,
                        subtitle      TEXT,
                        default_page  TEXT NOT NULL,
                        theme_id      TEXT NOT NULL REFERENCES theme (id),
                        url_base      TEXT NOT NULL,
                        time_zone     TEXT NOT NULL,
                        auto_htmx     INTEGER NOT NULL DEFAULT 0)"""
                do! write cmd
                cmd.CommandText <-
                    """CREATE TABLE web_log_rss (
                        web_log_id        TEXT PRIMARY KEY REFERENCES web_log (id),
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
                        PRIMARY KEY (page_id, as_of))"""
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

