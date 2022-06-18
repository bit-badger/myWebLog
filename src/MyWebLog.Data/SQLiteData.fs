namespace MyWebLog.Data

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.ViewModels

type SQLiteData (conn : SqliteConnection) =
    
    member _.x = ""
    
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
                        "INSERT INTO Category VALUES (@id, @webLogId, @name, @slug, @description, @parentId)"
                    [ cmd.Parameters.AddWithValue ("@id", CategoryId.toString cat.id)
                      cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString cat.webLogId)
                      cmd.Parameters.AddWithValue ("@name", cat.name)
                      cmd.Parameters.AddWithValue ("@slug", cat.slug)
                      cmd.Parameters.AddWithValue ("@description",
                        match cat.description with
                        | Some d -> d :> obj
                        | None -> DBNull.Value)
                      cmd.Parameters.AddWithValue ("@parentId",
                        match cat.parentId with
                        | Some (CategoryId parentId) -> parentId :> obj
                        | None -> DBNull.Value)
                      ]
                    |> ignore
                    let! _ = cmd.ExecuteNonQueryAsync ()
                    ()
                }
                
                member _.countAll webLogId = backgroundTask {
                    use cmd = conn.CreateCommand ()
                    cmd.CommandText <- "SELECT COUNT(id) FROM Category WHERE webLogId = @webLpgId"
                    cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString webLogId) |> ignore
                    let! result = cmd.ExecuteScalarAsync ()
                    return result :?> int
                }

                member _.countTopLevel webLogId =
                    Collection.Category.Count(fun cat -> cat.webLogId = webLogId && Option.isNone cat.parentId)
                    |> Task.FromResult
                
                member _.findAllForView webLogId = backgroundTask {
                    let cats =
                        Collection.Category.Find (fun cat -> cat.webLogId = webLogId)
                        |> Seq.sortBy (fun cat -> cat.name.ToLowerInvariant ())
                        |> List.ofSeq
                    let  ordered = orderByHierarchy cats None None []
                    let! counts  =
                        ordered
                        |> Seq.map (fun it -> backgroundTask {
                            // Parent category post counts include posts in subcategories
                            let catIds =
                                ordered
                                |> Seq.filter (fun cat -> cat.parentNames |> Array.contains it.name)
                                |> Seq.map (fun cat -> cat.id :> obj)
                                |> Seq.append (Seq.singleton it.id)
                                |> List.ofSeq
                            let count =
                                Collection.Post.Count (fun p ->
                                    p.webLogId = webLogId
                                    && p.status = Published
                                    && p.categoryIds |> List.exists (fun cId -> catIds |> List.contains cId))
                            return it.id, count
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
                
                member _.findById catId webLogId =
                    Collection.Category.FindById (CategoryIdMapping.toBson catId)
                    |> verifyWebLog webLogId (fun c -> c.webLogId)
                
                member _.findByWebLog webLogId =
                    Collection.Category.Find (fun c -> c.webLogId = webLogId)
                    |> toList
                
                member this.delete catId webLogId = backgroundTask {
                    match! this.findById catId webLogId with
                    | Some _ ->
                        // Delete the category off all posts where it is assigned
                        Collection.Post.Find (fun p -> p.webLogId = webLogId && p.categoryIds |> List.contains catId)
                        |> Seq.map (fun p ->
                            { p with categoryIds = p.categoryIds |> List.filter (fun cId -> cId <> catId) })
                        |> Collection.Post.Update
                        |> ignore
                        // Delete the category itself
                        let _ = Collection.Category.Delete (CategoryIdMapping.toBson catId)
                        do! checkpoint ()
                        return true
                    | None -> return false
                }
                
                member _.restore cats = backgroundTask {
                    let _ = Collection.Category.InsertBulk cats
                    do! checkpoint ()
                }
                
                member _.update cat = backgroundTask {
                    let _ = Collection.Category.Update cat
                    do! checkpoint ()
                }
        }
        
        member _.Page = {
            new IPageData with
                
                member _.add page = backgroundTask {
                    let _ = Collection.Page.Insert page
                    do! checkpoint ()
                }

                member _.all webLogId =
                    Collection.Page.Find (fun p -> p.webLogId = webLogId)
                    |> Seq.map pageWithoutText
                    |> Seq.sortBy pageSort
                    |> toList
                
                member _.countAll webLogId =
                    Collection.Page.Count (fun p -> p.webLogId = webLogId)
                    |> Task.FromResult

                member _.countListed webLogId =
                    Collection.Page.Count (fun p -> p.webLogId = webLogId && p.showInPageList)
                    |> Task.FromResult

                member _.findFullById pageId webLogId =
                    Collection.Page.FindById (PageIdMapping.toBson pageId)
                    |> verifyWebLog webLogId (fun it -> it.webLogId)

                member this.findById pageId webLogId = backgroundTask {
                    let! page = this.findFullById pageId webLogId
                    return page |> Option.map pageWithoutRevisions
                }

                member this.delete pageId webLogId = backgroundTask {
                    match! this.findById pageId webLogId with
                    | Some _ ->
                        let _ = Collection.Page.Delete (PageIdMapping.toBson pageId)
                        do! checkpoint ()
                        return true
                    | None -> return false
                }
                
                member _.findByPermalink permalink webLogId = backgroundTask {
                    let! page =
                        Collection.Page.Find (fun p -> p.webLogId = webLogId && p.permalink = permalink)
                        |> tryFirst
                    return page |> Option.map pageWithoutRevisions
                }
                
                member _.findCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        Collection.Page.Find (fun p ->
                            p.webLogId = webLogId
                            && p.priorPermalinks |> List.exists (fun link -> permalinks |> List.contains link))
                        |> tryFirst
                    return result |> Option.map (fun pg -> pg.permalink)
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
                    |> verifyWebLog webLogId (fun p -> p.webLogId)

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
                    |> verifyWebLog webLogId (fun tm -> tm.webLogId)
                
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
                    |> verifyWebLog webLogId (fun u -> u.webLogId)
                
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

            let _ = Collection.Category.EnsureIndex   (fun   c -> c.webLogId)
            let _ = Collection.Comment.EnsureIndex    (fun   c -> c.postId)
            let _ = Collection.Page.EnsureIndex       (fun   p -> p.webLogId)
            let _ = Collection.Page.EnsureIndex       (fun   p -> p.authorId)
            let _ = Collection.Post.EnsureIndex       (fun   p -> p.webLogId)
            let _ = Collection.Post.EnsureIndex       (fun   p -> p.authorId)
            let _ = Collection.TagMap.EnsureIndex     (fun  tm -> tm.webLogId)
            let _ = Collection.WebLog.EnsureIndex     (fun  wl -> wl.urlBase)
            let _ = Collection.WebLogUser.EnsureIndex (fun wlu -> wlu.webLogId)
            
            do! checkpoint ()
        }

