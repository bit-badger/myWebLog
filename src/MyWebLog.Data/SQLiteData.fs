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
        let getBoolean (rdr : SqliteDataReader) col = rdr.GetBoolean (rdr.GetOrdinal col)
        
        /// Get a date/time value from a data reader
        let getDateTime (rdr : SqliteDataReader) col = rdr.GetDateTime (rdr.GetOrdinal col)
        
        /// Get a string value from a data reader
        let getString (rdr : SqliteDataReader) col = rdr.GetString (rdr.GetOrdinal col)
        
        /// Get a possibly null string value from a data reader
        let tryString (rdr : SqliteDataReader) col =
            if rdr.IsDBNull (rdr.GetOrdinal col) then None else Some (getString rdr col)
        
        /// Create a category from the current row in the given data reader
        let toCategory (rdr : SqliteDataReader) : Category =
            { id          = CategoryId (getString rdr "id")
              webLogId    = WebLogId (getString rdr "web_log_id")
              name        = getString rdr "name"
              slug        = getString rdr "slug"
              description = tryString rdr "description"
              parentId    = tryString rdr "parent_id" |> Option.map CategoryId
            }
        
        /// Create a meta item from the current row in the given data reader
        let toMetaItem (rdr : SqliteDataReader) : MetaItem =
            { name  = getString rdr "name"
              value = getString rdr "value"
            }
        
        /// Create a permalink from the current row in the given data reader
        let toPermalink (rdr : SqliteDataReader) : Permalink =
            Permalink (getString rdr "permalink")
        
        /// Create a page from the current row in the given data reader
        let toPage (rdr : SqliteDataReader) : Page =
            { Page.empty with
                id             = PageId (getString rdr "id")
                webLogId       = WebLogId (getString rdr "web_log_id")
                authorId       = WebLogUserId (getString rdr "author_id")
                title          = getString rdr "title"
                permalink      = toPermalink rdr
                publishedOn    = getDateTime rdr "published_on"
                updatedOn      = getDateTime rdr "updated_on"
                showInPageList = getBoolean rdr "show_in_page_list"
                template       = tryString rdr "template"
                text           = getString rdr "page_text"
            }
        
        /// Create a revision from the current row in the given data reader
        let toRevision (rdr : SqliteDataReader) : Revision =
            { asOf = getDateTime rdr "as_of"
              text = MarkupText.parse (getString rdr "revision_text")
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
    
    /// Add a web log ID parameter
    let addWebLogId (cmd : SqliteCommand) webLogId =
        cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore

    /// Append meta items to a page
    let appendPageMeta (page : Page) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT name, value FROM page_meta WHERE page_id = @id"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { page with metadata = toList Map.toMetaItem rdr }
    }
    
    /// Return a page with no revisions or prior permalinks
    let pageWithoutRevisions (page : Page) =
        { page with revisions = []; priorPermalinks = [] }
    
    /// Return a page with no revisions, prior permalinks, or text
    let pageWithoutText page =
        { pageWithoutRevisions page with text = "" }
    
    /// Sort function for pages
    let pageSort (page : Page) =
        page.title.ToLowerInvariant ()
    
    /// Return a post with no revisions or prior permalinks
    let postWithoutRevisions (post : Post) =
        { post with revisions = []; priorPermalinks = [] }
    
    /// Return a post with no revisions, prior permalinks, or text
    let postWithoutText post =
        { postWithoutRevisions post with text = "" }
    
    
    
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
                    // Metadata
                    cmd.CommandText <- "INSERT INTO page_meta VALUES (@pageId, @name, @value)"
                    for meta in page.metadata do
                        cmd.Parameters.Clear ()
                        [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString page.id)
                          cmd.Parameters.AddWithValue ("@name", meta.name)
                          cmd.Parameters.AddWithValue ("@value", meta.value)
                        ] |> ignore
                        do! write cmd
                    // Revisions
                    cmd.CommandText <- "INSERT INTO page_revision VALUES (@pageId, @asOf, @text)"
                    for rev in page.revisions do
                        cmd.Parameters.Clear ()
                        [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString page.id)
                          cmd.Parameters.AddWithValue ("@asOf", rev.asOf)
                          cmd.Parameters.AddWithValue ("@text", MarkupText.toString rev.text)
                        ] |> ignore
                        do! write cmd
                }

                member _.all webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT * FROM page WHERE web_log_id = @webLogId ORDER BY LOWER(title)"
                    cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore
                    let noText rdr = { Map.toPage rdr with text = "" }
                    use! rdr = cmd.ExecuteReaderAsync ()
                    return toList noText rdr
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
                        use cmd = conn.CreateCommand ()
                        cmd.CommandText <- "SELECT * FROM page_permalink WHERE page_id = @pageId"
                        cmd.Parameters.AddWithValue ("@pageId", PageId.toString page.id) |> ignore
                        use! linkRdr = cmd.ExecuteReaderAsync ()
                        let page = { page with priorPermalinks = toList Map.toPermalink linkRdr }
                        cmd.CommandText <- "SELECT * FROM page_revision WHERE page_id = @pageId"
                        use! revRdr = cmd.ExecuteReaderAsync ()
                        return Some { page with revisions = toList Map.toRevision revRdr }
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
                
                member _.findFullByWebLog webLogId =
                    Collection.Page.Find (fun p -> p.webLogId = webLogId)
                    |> toList
                
                member _.findListed webLogId =
                    Collection.Page.Find (fun p -> p.webLogId = webLogId && p.showInPageList)
                    |> Seq.map pageWithoutText
                    |> Seq.sortBy pageSort
                    |> toList

                member _.findPageOfPages webLogId pageNbr =
                    Collection.Page.Find (fun p -> p.webLogId = webLogId)
                    |> Seq.map pageWithoutRevisions
                    |> Seq.sortBy pageSort
                    |> toPagedList pageNbr 25
                
                member _.restore pages = backgroundTask {
                    let _ = Collection.Page.InsertBulk pages
                    do! checkpoint ()
                }
                
                member _.update page = backgroundTask {
                    let _ = Collection.Page.Update page
                    do! checkpoint ()
                }
                
                member this.updatePriorPermalinks pageId webLogId permalinks = backgroundTask {
                    match! this.findFullById pageId webLogId with
                    | Some page ->
                        do! this.update { page with priorPermalinks = permalinks }
                        return true
                    | None -> return false
                }
        }
        
        member _.Post = {
            new IPostData with
                
                member _.add post = backgroundTask {
                    let _ = Collection.Post.Insert post
                    do! checkpoint ()
                }
                
                member _.countByStatus status webLogId =
                    Collection.Post.Count (fun p -> p.webLogId = webLogId && p.status = status)
                    |> Task.FromResult
                
                member _.findByPermalink permalink webLogId =
                    Collection.Post.Find (fun p -> p.webLogId = webLogId && p.permalink = permalink)
                    |> tryFirst
                
                member _.findFullById postId webLogId =
                    Collection.Post.FindById (PostIdMapping.toBson postId)
                    //|> verifyWebLog webLogId (fun p -> p.webLogId)

                member this.delete postId webLogId = backgroundTask {
                    match! this.findFullById postId webLogId with
                    | Some _ ->
                        let _ = Collection.Post.Delete (PostIdMapping.toBson postId)
                        do! checkpoint ()
                        return true
                    | None -> return false
                }
                
                member _.findCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        Collection.Post.Find (fun p ->
                            p.webLogId = webLogId
                            && p.priorPermalinks |> List.exists (fun link -> permalinks |> List.contains link))
                        |> tryFirst
                    return result |> Option.map (fun post -> post.permalink)
                }

                member _.findFullByWebLog webLogId =
                    Collection.Post.Find (fun p -> p.webLogId = webLogId)
                    |> toList

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
                
                member _.restore posts = backgroundTask {
                    let _ = Collection.Post.InsertBulk posts
                    do! checkpoint ()
                }
                
                member _.update post = backgroundTask {
                    let _ = Collection.Post.Update post
                    do! checkpoint ()
                }

                member this.updatePriorPermalinks postId webLogId permalinks = backgroundTask {
                    match! this.findFullById postId webLogId with
                    | Some post ->
                        do! this.update { post with priorPermalinks = permalinks }
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

