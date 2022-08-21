namespace MyWebLog.Data

open System.Threading.Tasks
open MyWebLog
open RethinkDb.Driver

/// Functions to assist with retrieving data
[<AutoOpen>]
module private RethinkHelpers =
    
    /// Table names
    [<RequireQualifiedAccess>]
    module Table =
        
        /// The category table
        let Category = "Category"

        /// The comment table
        let Comment = "Comment"
        
        /// The database version table
        let DbVersion = "DbVersion"
        
        /// The page table
        let Page = "Page"
        
        /// The post table
        let Post = "Post"
        
        /// The tag map table
        let TagMap = "TagMap"
        
        /// The theme table
        let Theme = "Theme"
        
        /// The theme asset table
        let ThemeAsset = "ThemeAsset"
        
        /// The uploaded file table
        let Upload = "Upload"
        
        /// The web log table
        let WebLog = "WebLog"

        /// The web log user table
        let WebLogUser = "WebLogUser"

        /// A list of all tables
        let all = [ Category; Comment; DbVersion; Page; Post; TagMap; Theme; ThemeAsset; Upload; WebLog; WebLogUser ]

    
    /// Index names for indexes not on a data item's name
    [<RequireQualifiedAccess>]
    module Index =
        
        /// An index by web log ID and e-mail address
        let LogOn = "LogOn"
        
        /// An index by web log ID and uploaded file path
        let WebLogAndPath = "WebLogAndPath"
        
        /// An index by web log ID and mapped tag
        let WebLogAndTag = "WebLogAndTag"
        
        /// An index by web log ID and tag URL value
        let WebLogAndUrl = "WebLogAndUrl"

    
    /// Shorthand for the ReQL starting point
    let r = RethinkDB.R

    /// Verify that the web log ID matches before returning an item
    let verifyWebLog<'T> webLogId (prop : 'T -> WebLogId) (f : Net.IConnection -> Task<'T option>) =
        fun conn -> backgroundTask {
            match! f conn with Some it when (prop it) = webLogId -> return Some it | _ -> return None
        }
    
    /// Get the first item from a list, or None if the list is empty
    let tryFirst<'T> (f : Net.IConnection -> Task<'T list>) =
        fun conn -> backgroundTask {
            let! results = f conn
            return results |> List.tryHead
        }
    
    /// Cast a strongly-typed list to an object list
    let objList<'T> (objects : 'T list) = objects |> List.map (fun it -> it :> obj)
    
    /// A simple type for the database version table
    [<CLIMutable; NoComparison; NoEquality>]
    type DbVersion = { Id : string }


open System
open Microsoft.Extensions.Logging
open MyWebLog.ViewModels
open RethinkDb.Driver.FSharp

/// RethinkDB implementation of data functions for myWebLog
type RethinkDbData (conn : Net.IConnection, config : DataConfig, log : ILogger<RethinkDbData>) =
    
    /// Match theme asset IDs by their prefix (the theme ID)
    let matchAssetByThemeId themeId =
        let keyPrefix = $"^{ThemeId.toString themeId}/"
        fun (row : Ast.ReqlExpr) -> row[nameof ThemeAsset.empty.Id].Match keyPrefix :> obj
    
    /// Function to exclude template text from themes
    let withoutTemplateText (row : Ast.ReqlExpr) : obj =
        {|  Templates = row[nameof Theme.empty.Templates].Without [| nameof ThemeTemplate.empty.Text |] |}
        
    /// Ensure field indexes exist, as well as special indexes for selected tables
    let ensureIndexes table fields = backgroundTask {
        let! indexes = rethink<string list> { withTable table; indexList; result; withRetryOnce conn }
        for field in fields do
            if not (indexes |> List.contains field) then
                log.LogInformation $"Creating index {table}.{field}..."
                do! rethink { withTable table; indexCreate field; write; withRetryOnce; ignoreResult conn }
        // Post and page need index by web log ID and permalink
        if [ Table.Page; Table.Post ] |> List.contains table then
            let permalinkIdx = nameof Page.empty.Permalink
            if not (indexes |> List.contains permalinkIdx) then
                log.LogInformation $"Creating index {table}.{permalinkIdx}..."
                do! rethink {
                    withTable table
                    indexCreate permalinkIdx
                        (fun row -> r.Array (row[nameof Page.empty.WebLogId], row[permalinkIdx].Downcase ()) :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
            // Prior permalinks are searched when a post or page permalink do not match the current URL
            let priorIdx = nameof Post.empty.PriorPermalinks
            if not (indexes |> List.contains priorIdx) then
                log.LogInformation $"Creating index {table}.{priorIdx}..."
                do! rethink {
                    withTable table
                    indexCreate priorIdx (fun row -> row[priorIdx].Downcase () :> obj) [ Multi ]
                    write; withRetryOnce; ignoreResult conn
                }
        // Post needs indexes by category and tag (used for counting and retrieving posts)
        if Table.Post = table then
            for idx in [ nameof Post.empty.CategoryIds; nameof Post.empty.Tags ] do
                if not (List.contains idx indexes) then
                    log.LogInformation $"Creating index {table}.{idx}..."
                    do! rethink {
                        withTable table
                        indexCreate idx [ Multi ]
                        write; withRetryOnce; ignoreResult conn
                    }
        // Tag mapping needs an index by web log ID and both tag and URL values
        if Table.TagMap = table then
            if not (indexes |> List.contains Index.WebLogAndTag) then
                log.LogInformation $"Creating index {table}.{Index.WebLogAndTag}..."
                do! rethink {
                    withTable table
                    indexCreate Index.WebLogAndTag (fun row ->
                        [| row[nameof TagMap.empty.WebLogId]; row[nameof TagMap.empty.Tag] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
            if not (indexes |> List.contains Index.WebLogAndUrl) then
                log.LogInformation $"Creating index {table}.{Index.WebLogAndUrl}..."
                do! rethink {
                    withTable table
                    indexCreate Index.WebLogAndUrl (fun row ->
                        [| row[nameof TagMap.empty.WebLogId]; row[nameof TagMap.empty.UrlValue] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
        // Uploaded files need an index by web log ID and path, as that is how they are retrieved
        if Table.Upload = table then
            if not (indexes |> List.contains Index.WebLogAndPath) then
                log.LogInformation $"Creating index {table}.{Index.WebLogAndPath}..."
                do! rethink {
                    withTable table
                    indexCreate Index.WebLogAndPath (fun row ->
                        [| row[nameof Upload.empty.WebLogId]; row[nameof Upload.empty.Path] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
        // Users log on with e-mail
        if Table.WebLogUser = table then
            if not (indexes |> List.contains Index.LogOn) then
                log.LogInformation $"Creating index {table}.{Index.LogOn}..."
                do! rethink {
                    withTable table
                    indexCreate Index.LogOn (fun row ->
                        [| row[nameof WebLogUser.empty.WebLogId]; row[nameof WebLogUser.empty.Email] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
    }
    
    /// The batch size for restoration methods
    let restoreBatchSize = 100
    
    /// Delete assets for the given theme ID
    let deleteAssetsByTheme themeId = rethink {
        withTable Table.ThemeAsset
        filter (matchAssetByThemeId themeId)
        delete
        write; withRetryDefault; ignoreResult conn
    }
    
    /// Set a specific database version
    let setDbVersion (version : string) = backgroundTask {
        do! rethink {
            withTable Table.DbVersion
            delete
            write; withRetryOnce; ignoreResult conn
        }
        do! rethink {
            withTable Table.DbVersion
            insert { Id = version }
            write; withRetryOnce; ignoreResult conn
        }
    }
    
    /// The connection for this instance
    member _.Conn = conn
    
    interface IData with
    
        member _.Category = {
            new ICategoryData with
                
                member _.Add cat = rethink {
                    withTable Table.Category
                    insert cat
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.CountAll webLogId = rethink<int> {
                    withTable Table.Category
                    getAll [ webLogId ] (nameof Category.empty.WebLogId)
                    count
                    result; withRetryDefault conn
                }

                member _.CountTopLevel webLogId = rethink<int> {
                    withTable Table.Category
                    getAll [ webLogId ] (nameof Category.empty.WebLogId)
                    filter (nameof Category.empty.ParentId) None
                    count
                    result; withRetryDefault conn
                }
                
                member _.FindAllForView webLogId = backgroundTask {
                    let! cats = rethink<Category list> {
                        withTable Table.Category
                        getAll [ webLogId ] (nameof Category.empty.WebLogId)
                        orderByFunc (fun it -> it[nameof Category.empty.Name].Downcase () :> obj)
                        result; withRetryDefault conn
                    }
                    let  ordered = Utils.orderByHierarchy cats None None []
                    let! counts  =
                        ordered
                        |> Seq.map (fun it -> backgroundTask {
                            // Parent category post counts include posts in subcategories
                            let catIds =
                                ordered
                                |> Seq.filter (fun cat -> cat.ParentNames |> Array.contains it.Name)
                                |> Seq.map (fun cat -> cat.Id :> obj)
                                |> Seq.append (Seq.singleton it.Id)
                                |> List.ofSeq
                            let! count = rethink<int> {
                                withTable Table.Post
                                getAll catIds (nameof Post.empty.CategoryIds)
                                filter (nameof Post.empty.Status) Published
                                distinct
                                count
                                result; withRetryDefault conn
                            }
                            return it.Id, count
                            })
                        |> Task.WhenAll
                    return
                        ordered
                        |> Seq.map (fun cat ->
                            { cat with
                                PostCount = counts
                                            |> Array.tryFind (fun c -> fst c = cat.Id)
                                            |> Option.map snd
                                            |> Option.defaultValue 0
                            })
                        |> Array.ofSeq
                }
                
                member _.FindById catId webLogId =
                    rethink<Category> {
                        withTable Table.Category
                        get catId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun c -> c.WebLogId) <| conn
                
                member _.FindByWebLog webLogId = rethink<Category list> {
                    withTable Table.Category
                    getAll [ webLogId ] (nameof Category.empty.WebLogId)
                    result; withRetryDefault conn
                }
                
                member this.Delete catId webLogId = backgroundTask {
                    match! this.FindById catId webLogId with
                    | Some cat ->
                        // Reassign any children to the category's parent category
                        let! children = rethink<int> {
                            withTable Table.Category
                            filter (nameof Category.empty.ParentId) catId
                            count
                            result; withRetryDefault conn
                        }
                        if children > 0 then
                            do! rethink {
                                withTable Table.Category
                                filter (nameof Category.empty.ParentId) catId
                                update [ nameof Category.empty.ParentId, cat.ParentId :> obj ]
                                write; withRetryDefault; ignoreResult conn
                            }
                        // Delete the category off all posts where it is assigned
                        do! rethink {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.empty.WebLogId)
                            filter (fun row -> row[nameof Post.empty.CategoryIds].Contains catId :> obj)
                            update (fun row ->
                                {| CategoryIds = r.Array(row[nameof Post.empty.CategoryIds]).Remove catId |} :> obj)
                            write; withRetryDefault; ignoreResult conn 
                        }
                        // Delete the category itself
                        do! rethink {
                            withTable Table.Category
                            get catId
                            delete
                            write; withRetryDefault; ignoreResult conn
                        }
                        return if children = 0 then CategoryDeleted else ReassignedChildCategories
                    | None -> return CategoryNotFound
                }
                
                member _.Restore cats = backgroundTask {
                    for batch in cats |> List.chunkBySize restoreBatchSize do
                        do! rethink {
                            withTable Table.Category
                            insert batch
                            write; withRetryOnce; ignoreResult conn
                        }
                }
                
                member _.Update cat = rethink {
                    withTable Table.Category
                    get cat.Id
                    update [ nameof cat.Name,        cat.Name :> obj
                             nameof cat.Slug,        cat.Slug
                             nameof cat.Description, cat.Description
                             nameof cat.ParentId,    cat.ParentId
                           ]
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.Page = {
            new IPageData with
                
                member _.Add page = rethink {
                    withTable Table.Page
                    insert page
                    write; withRetryDefault; ignoreResult conn
                }

                member _.All webLogId = rethink<Page list> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.empty.WebLogId)
                    without [ nameof Page.empty.Text
                              nameof Page.empty.Metadata
                              nameof Page.empty.Revisions
                              nameof Page.empty.PriorPermalinks ]
                    orderByFunc (fun row -> row[nameof Page.empty.Title].Downcase () :> obj)
                    result; withRetryDefault conn
                }
                
                member _.CountAll webLogId = rethink<int> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.empty.WebLogId)
                    count
                    result; withRetryDefault conn
                }

                member _.CountListed webLogId = rethink<int> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.empty.WebLogId)
                    filter (nameof Page.empty.IsInPageList) true
                    count
                    result; withRetryDefault conn
                }

                member _.Delete pageId webLogId = backgroundTask {
                    let! result = rethink<Model.Result> {
                        withTable Table.Page
                        getAll [ pageId ]
                        filter (fun row -> row[nameof Page.empty.WebLogId].Eq webLogId :> obj)
                        delete
                        write; withRetryDefault conn
                    }
                    return result.Deleted > 0UL
                }
                
                member _.FindById pageId webLogId =
                    rethink<Page> {
                        withTable Table.Page
                        get pageId
                        without [ nameof Page.empty.PriorPermalinks; nameof Page.empty.Revisions ]
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun it -> it.WebLogId) <| conn

                member _.FindByPermalink permalink webLogId =
                    rethink<Page list> {
                        withTable Table.Page
                        getAll [ [| webLogId :> obj; permalink |] ] (nameof Page.empty.Permalink)
                        without [ nameof Page.empty.PriorPermalinks; nameof Page.empty.Revisions ]
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        (rethink<Page list> {
                            withTable Table.Page
                            getAll (objList permalinks) (nameof Page.empty.PriorPermalinks)
                            filter (nameof Page.empty.WebLogId) webLogId
                            without [ nameof Page.empty.Revisions; nameof Page.empty.Text ]
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst) conn
                    return result |> Option.map (fun pg -> pg.Permalink)
                }
                
                member _.FindFullById pageId webLogId =
                    rethink<Page> {
                        withTable Table.Page
                        get pageId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun it -> it.WebLogId) <| conn
                
                member _.FindFullByWebLog webLogId = rethink<Page> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.empty.WebLogId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindListed webLogId = rethink<Page list> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.empty.WebLogId)
                    filter [ nameof Page.empty.IsInPageList, true :> obj ]
                    without [ nameof Page.empty.Text; nameof Page.empty.PriorPermalinks; nameof Page.empty.Revisions ]
                    orderBy (nameof Page.empty.Title)
                    result; withRetryDefault conn
                }

                member _.FindPageOfPages webLogId pageNbr = rethink<Page list> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.empty.WebLogId)
                    without [ nameof Page.empty.Metadata
                              nameof Page.empty.PriorPermalinks
                              nameof Page.empty.Revisions ]
                    orderByFunc (fun row -> row[nameof Page.empty.Title].Downcase ())
                    skip ((pageNbr - 1) * 25)
                    limit 25
                    result; withRetryDefault conn
                }
                
                member _.Restore pages = backgroundTask {
                    for batch in pages |> List.chunkBySize restoreBatchSize do
                        do! rethink {
                            withTable Table.Page
                            insert batch
                            write; withRetryOnce; ignoreResult conn
                        }
                }
                
                member _.Update page = rethink {
                    withTable Table.Page
                    get page.Id
                    update [
                        nameof page.Title,           page.Title :> obj
                        nameof page.Permalink,       page.Permalink
                        nameof page.UpdatedOn,       page.UpdatedOn
                        nameof page.IsInPageList,    page.IsInPageList
                        nameof page.Template,        page.Template
                        nameof page.Text,            page.Text
                        nameof page.PriorPermalinks, page.PriorPermalinks
                        nameof page.Metadata,        page.Metadata
                        nameof page.Revisions,       page.Revisions
                        ]
                    write; withRetryDefault; ignoreResult conn
                }
                
                member this.UpdatePriorPermalinks pageId webLogId permalinks = backgroundTask {
                    match! this.FindById pageId webLogId with
                    | Some _ ->
                        do! rethink {
                            withTable Table.Page
                            get pageId
                            update [ nameof Page.empty.PriorPermalinks, permalinks :> obj ]
                            write; withRetryDefault; ignoreResult conn
                        }
                        return true
                    | None -> return false
                }
        }
        
        member _.Post = {
            new IPostData with
                
                member _.Add post = rethink {
                    withTable Table.Post
                    insert post
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.CountByStatus status webLogId = rethink<int> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof Post.empty.WebLogId)
                    filter (nameof Post.empty.Status) status
                    count
                    result; withRetryDefault conn
                }

                member _.Delete postId webLogId = backgroundTask {
                    let! result = rethink<Model.Result> {
                        withTable Table.Post
                        getAll [ postId ]
                        filter (fun row -> row[nameof Post.empty.WebLogId].Eq webLogId :> obj)
                        delete
                        write; withRetryDefault conn
                    }
                    return result.Deleted > 0UL
                }
                
                member _.FindById postId webLogId =
                    rethink<Post> {
                        withTable Table.Post
                        get postId
                        without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun p -> p.WebLogId) <| conn
                
                member _.FindByPermalink permalink webLogId =
                    rethink<Post list> {
                        withTable Table.Post
                        getAll [ [| webLogId :> obj; permalink |] ] (nameof Post.empty.Permalink)
                        without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindFullById postId webLogId =
                    rethink<Post> {
                        withTable Table.Post
                        get postId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun p -> p.WebLogId) <| conn

                member _.FindCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        (rethink<Post list> {
                            withTable Table.Post
                            getAll (objList permalinks) (nameof Post.empty.PriorPermalinks)
                            filter (nameof Post.empty.WebLogId) webLogId
                            without [ nameof Post.empty.Revisions; nameof Post.empty.Text ]
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst) conn
                    return result |> Option.map (fun post -> post.Permalink)
                }
                
                member _.FindFullByWebLog webLogId = rethink<Post> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof Post.empty.WebLogId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll (objList categoryIds) (nameof Post.empty.CategoryIds)
                    filter [ nameof Post.empty.WebLogId, webLogId :> obj
                             nameof Post.empty.Status,   Published ]
                    without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                    distinct
                    orderByDescending (nameof Post.empty.PublishedOn)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindPageOfPosts webLogId pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof Post.empty.WebLogId)
                    without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                    orderByFuncDescending (fun row ->
                        row[nameof Post.empty.PublishedOn].Default_ (nameof Post.empty.UpdatedOn) :> obj)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }

                member _.FindPageOfPublishedPosts webLogId pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof Post.empty.WebLogId)
                    filter (nameof Post.empty.Status) Published
                    without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                    orderByDescending (nameof Post.empty.PublishedOn)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindPageOfTaggedPosts webLogId tag pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ tag ] (nameof Post.empty.Tags)
                    filter [ nameof Post.empty.WebLogId, webLogId :> obj
                             nameof Post.empty.Status,   Published ]
                    without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                    orderByDescending (nameof Post.empty.PublishedOn)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindSurroundingPosts webLogId publishedOn = backgroundTask {
                    let! older =
                        rethink<Post list> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.empty.WebLogId)
                            filter (fun row -> row[nameof Post.empty.PublishedOn].Lt publishedOn :> obj)
                            without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                            orderByDescending (nameof Post.empty.PublishedOn)
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst <| conn
                    let! newer =
                        rethink<Post list> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.empty.WebLogId)
                            filter (fun row -> row[nameof Post.empty.PublishedOn].Gt publishedOn :> obj)
                            without [ nameof Post.empty.PriorPermalinks; nameof Post.empty.Revisions ]
                            orderBy (nameof Post.empty.PublishedOn)
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst <| conn
                    return older, newer
                }
                
                member _.Restore pages = backgroundTask {
                    for batch in pages |> List.chunkBySize restoreBatchSize do
                        do! rethink {
                            withTable Table.Post
                            insert batch
                            write; withRetryOnce; ignoreResult conn
                        }
                }
                
                member _.Update post = rethink {
                    withTable Table.Post
                    get post.Id
                    replace post
                    write; withRetryDefault; ignoreResult conn
                }

                member _.UpdatePriorPermalinks postId webLogId permalinks = backgroundTask {
                    match! (
                        rethink<Post> {
                            withTable Table.Post
                            get postId
                            without [ nameof Post.empty.Revisions; nameof Post.empty.PriorPermalinks ]
                            resultOption; withRetryOptionDefault
                        }
                        |> verifyWebLog webLogId (fun p -> p.WebLogId)) conn with
                    | Some _ ->
                        do! rethink {
                            withTable Table.Post
                            get postId
                            update [ nameof Post.empty.PriorPermalinks, permalinks :> obj ]
                            write; withRetryDefault; ignoreResult conn
                        }
                        return true
                    | None -> return false
                }
        }
        
        member _.TagMap = {
            new ITagMapData with
                
                member _.Delete tagMapId webLogId = backgroundTask {
                    let! result = rethink<Model.Result> {
                        withTable Table.TagMap
                        getAll [ tagMapId ]
                        filter (fun row -> row[nameof TagMap.empty.WebLogId].Eq webLogId :> obj)
                        delete
                        write; withRetryDefault conn
                    }
                    return result.Deleted > 0UL
                }
                
                member _.FindById tagMapId webLogId =
                    rethink<TagMap> {
                        withTable Table.TagMap
                        get tagMapId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun tm -> tm.WebLogId) <| conn
                
                member _.FindByUrlValue urlValue webLogId =
                    rethink<TagMap list> {
                        withTable Table.TagMap
                        getAll [ [| webLogId :> obj; urlValue |] ] Index.WebLogAndUrl
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindByWebLog webLogId = rethink<TagMap list> {
                    withTable Table.TagMap
                    between [| webLogId :> obj; r.Minval () |] [| webLogId :> obj; r.Maxval () |]
                            [ Index Index.WebLogAndTag ]
                    orderBy (nameof TagMap.empty.Tag)
                    result; withRetryDefault conn
                }
                
                member _.FindMappingForTags tags webLogId = rethink<TagMap list> {
                    withTable Table.TagMap
                    getAll (tags |> List.map (fun tag -> [| webLogId :> obj; tag |] :> obj)) Index.WebLogAndTag
                    result; withRetryDefault conn
                }
                
                member _.Restore tagMaps = backgroundTask {
                    for batch in tagMaps |> List.chunkBySize restoreBatchSize do
                        do! rethink {
                            withTable Table.TagMap
                            insert batch
                            write; withRetryOnce; ignoreResult conn
                        }
                }
                
                member _.Save tagMap = rethink {
                    withTable Table.TagMap
                    get tagMap.Id
                    replace tagMap
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.Theme = {
            new IThemeData with
                
                member _.All () = rethink<Theme list> {
                    withTable Table.Theme
                    filter (fun row -> row[nameof Theme.empty.Id].Ne "admin" :> obj)
                    merge withoutTemplateText
                    orderBy (nameof Theme.empty.Id)
                    result; withRetryDefault conn
                }
                
                member _.Exists themeId = backgroundTask {
                    let! count = rethink<int> {
                        withTable Table.Theme
                        filter (nameof Theme.empty.Id) themeId
                        count
                        result; withRetryDefault conn
                    }
                    return count > 0
                }
                
                member _.FindById themeId = rethink<Theme> {
                    withTable Table.Theme
                    get themeId
                    resultOption; withRetryOptionDefault conn
                }
                
                member _.FindByIdWithoutText themeId = rethink<Theme> {
                    withTable Table.Theme
                    get themeId
                    merge withoutTemplateText
                    resultOption; withRetryOptionDefault conn
                }
                
                member this.Delete themeId = backgroundTask {
                    match! this.FindByIdWithoutText themeId with
                    | Some _ ->
                        do! deleteAssetsByTheme themeId
                        do! rethink {
                            withTable Table.Theme
                            get themeId
                            delete
                            write; withRetryDefault; ignoreResult conn
                        }
                        return true
                    | None -> return false
                }
                
                member _.Save theme = rethink {
                    withTable Table.Theme
                    get theme.Id
                    replace theme
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.ThemeAsset = {
            new IThemeAssetData with
                
                member _.All () = rethink<ThemeAsset list> {
                    withTable Table.ThemeAsset
                    without [ nameof ThemeAsset.empty.Data ]
                    result; withRetryDefault conn
                }
                
                member _.DeleteByTheme themeId = deleteAssetsByTheme themeId
                
                member _.FindById assetId = rethink<ThemeAsset> {
                    withTable Table.ThemeAsset
                    get assetId
                    resultOption; withRetryOptionDefault conn
                }
                
                member _.FindByTheme themeId = rethink<ThemeAsset list> {
                    withTable Table.ThemeAsset
                    filter (matchAssetByThemeId themeId)
                    without [ nameof ThemeAsset.empty.Data ]
                    result; withRetryDefault conn
                }
                
                member _.FindByThemeWithData themeId = rethink<ThemeAsset> {
                    withTable Table.ThemeAsset
                    filter (matchAssetByThemeId themeId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.Save asset = rethink {
                    withTable Table.ThemeAsset
                    get asset.Id
                    replace asset
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.Upload = {
            new IUploadData with
                
                member _.Add upload = rethink {
                    withTable Table.Upload
                    insert upload
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.Delete uploadId webLogId = backgroundTask {
                    let! upload =
                        rethink<Upload> {
                            withTable Table.Upload
                            get uploadId
                            resultOption; withRetryOptionDefault
                        }
                        |> verifyWebLog<Upload> webLogId (fun u -> u.WebLogId) <| conn
                    match upload with
                    | Some up ->
                        do! rethink {
                            withTable Table.Upload
                            get uploadId
                            delete
                            write; withRetryDefault; ignoreResult conn
                        }
                        return Ok (Permalink.toString up.Path)
                    | None -> return Result.Error $"Upload ID {UploadId.toString uploadId} not found"
                }
                
                member _.FindByPath path webLogId =
                    rethink<Upload> {
                        withTable Table.Upload
                        getAll [ [| webLogId :> obj; path |] ] Index.WebLogAndPath
                        resultCursor; withRetryCursorDefault; toList
                    }
                    |> tryFirst <| conn
                
                member _.FindByWebLog webLogId = rethink<Upload> {
                    withTable Table.Upload
                    between [| webLogId :> obj; r.Minval () |] [| webLogId :> obj; r.Maxval () |]
                            [ Index Index.WebLogAndPath ]
                    without [ nameof Upload.empty.Data ]
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindByWebLogWithData webLogId = rethink<Upload> {
                    withTable Table.Upload
                    between [| webLogId :> obj; r.Minval () |] [| webLogId :> obj; r.Maxval () |]
                            [ Index Index.WebLogAndPath ]
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.Restore uploads = backgroundTask {
                    // Files can be large; we'll do 5 at a time
                    for batch in uploads |> List.chunkBySize 5 do
                        do! rethink {
                            withTable Table.TagMap
                            insert batch
                            write; withRetryOnce; ignoreResult conn
                        }
                }
        }
        
        member _.WebLog = {
            new IWebLogData with
                
                member _.Add webLog = rethink {
                    withTable Table.WebLog
                    insert webLog
                    write; withRetryOnce; ignoreResult conn
                }
                
                member _.All () = rethink<WebLog list> {
                    withTable Table.WebLog
                    result; withRetryDefault conn
                }
                
                member _.Delete webLogId = backgroundTask {
                     // Comments should be deleted by post IDs
                     let! thePostIds = rethink<{| Id : string |} list> {
                         withTable Table.Post
                         getAll [ webLogId ] (nameof Post.empty.WebLogId)
                         pluck [ nameof Post.empty.Id ]
                         result; withRetryOnce conn
                     }
                     if not (List.isEmpty thePostIds) then
                         let postIds = thePostIds |> List.map (fun it -> it.Id :> obj)
                         do! rethink {
                             withTable Table.Comment
                             getAll postIds (nameof Comment.empty.PostId)
                             delete
                             write; withRetryOnce; ignoreResult conn
                         }
                     // Tag mappings do not have a straightforward webLogId index
                     do! rethink {
                         withTable Table.TagMap
                         between [| webLogId :> obj; r.Minval () |] [| webLogId :> obj; r.Maxval () |]
                                 [ Index Index.WebLogAndTag ]
                         delete
                         write; withRetryOnce; ignoreResult conn
                     }
                     // Uploaded files do not have a straightforward webLogId index
                     do! rethink {
                         withTable Table.Upload
                         between [| webLogId :> obj; r.Minval () |] [| webLogId :> obj; r.Maxval () |]
                                 [ Index Index.WebLogAndPath ]
                         delete
                         write; withRetryOnce; ignoreResult conn
                     }
                     for table in [ Table.Post; Table.Category; Table.Page; Table.WebLogUser ] do
                         do! rethink {
                             withTable table
                             getAll [ webLogId ] (nameof Post.empty.WebLogId)
                             delete
                             write; withRetryOnce; ignoreResult conn
                         }
                     do! rethink {
                         withTable Table.WebLog
                         get webLogId
                         delete
                         write; withRetryOnce; ignoreResult conn
                     }
                }
                
                member _.FindByHost url =
                    rethink<WebLog list> {
                        withTable Table.WebLog
                        getAll [ url ] (nameof WebLog.empty.UrlBase)
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn

                member _.FindById webLogId = rethink<WebLog> {
                    withTable Table.WebLog
                    get webLogId
                    resultOption; withRetryOptionDefault conn
                }
                
                member _.UpdateRssOptions webLog = rethink {
                    withTable Table.WebLog
                    get webLog.Id
                    update [ nameof WebLog.empty.Rss, webLog.Rss :> obj ]
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.UpdateSettings webLog = rethink {
                    withTable Table.WebLog
                    get webLog.Id
                    update [
                        nameof webLog.Name,         webLog.Name :> obj
                        nameof webLog.Slug,         webLog.Slug
                        nameof webLog.Subtitle,     webLog.Subtitle
                        nameof webLog.DefaultPage,  webLog.DefaultPage
                        nameof webLog.PostsPerPage, webLog.PostsPerPage
                        nameof webLog.TimeZone,     webLog.TimeZone
                        nameof webLog.ThemeId,      webLog.ThemeId
                        nameof webLog.AutoHtmx,     webLog.AutoHtmx
                        nameof webLog.Uploads,      webLog.Uploads
                    ]
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.WebLogUser = {
            new IWebLogUserData with
                
                member _.Add user = rethink {
                    withTable Table.WebLogUser
                    insert user
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.FindById userId webLogId =
                    rethink<WebLogUser> {
                        withTable Table.WebLogUser
                        get userId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun u -> u.WebLogId) <| conn
                
                member this.Delete userId webLogId = backgroundTask {
                    match! this.FindById userId webLogId with
                    | Some _ ->
                        let! pageCount = rethink<int> {
                            withTable Table.Page
                            getAll [ webLogId ] (nameof Page.empty.WebLogId)
                            filter (nameof Page.empty.AuthorId) userId
                            count
                            result; withRetryDefault conn
                        }
                        let! postCount = rethink<int> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.empty.WebLogId)
                            filter (nameof Post.empty.AuthorId) userId
                            count
                            result; withRetryDefault conn
                        }
                        if pageCount + postCount > 0 then
                            return Result.Error "User has pages or posts; cannot delete"
                        else
                            do! rethink {
                                withTable Table.WebLogUser
                                get userId
                                delete
                                write; withRetryDefault; ignoreResult conn
                            }
                            return Ok true
                    | None -> return Result.Error "User does not exist"
                }
                
                member _.FindByEmail email webLogId =
                    rethink<WebLogUser list> {
                        withTable Table.WebLogUser
                        getAll [ [| webLogId :> obj; email |] ] Index.LogOn
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindByWebLog webLogId = rethink<WebLogUser list> {
                    withTable Table.WebLogUser
                    getAll [ webLogId ] (nameof WebLogUser.empty.WebLogId)
                    orderByFunc (fun row -> row[nameof WebLogUser.empty.PreferredName].Downcase ())
                    result; withRetryDefault conn
                }
                
                member _.FindNames webLogId userIds = backgroundTask {
                    let! users = rethink<WebLogUser list> {
                        withTable Table.WebLogUser
                        getAll (objList userIds)
                        filter (nameof WebLogUser.empty.WebLogId) webLogId
                        result; withRetryDefault conn
                    }
                    return
                        users
                        |> List.map (fun u -> { Name = WebLogUserId.toString u.Id; Value = WebLogUser.displayName u })
                }
                
                member _.Restore users = backgroundTask {
                    for batch in users |> List.chunkBySize restoreBatchSize do
                        do! rethink {
                            withTable Table.WebLogUser
                            insert batch
                            write; withRetryOnce; ignoreResult conn
                        }
                }
                
                member this.SetLastSeen userId webLogId = backgroundTask {
                    match! this.FindById userId webLogId with
                    | Some _ ->
                        do! rethink {
                            withTable Table.WebLogUser
                            get userId
                            update [ nameof WebLogUser.empty.LastSeenOn, Noda.now () :> obj ]
                            write; withRetryOnce; ignoreResult conn
                        }
                    | None -> ()
                }
                
                member _.Update user = rethink {
                    withTable Table.WebLogUser
                    get user.Id
                    update [
                        nameof user.Email,         user.Email :> obj
                        nameof user.FirstName,     user.FirstName
                        nameof user.LastName,      user.LastName
                        nameof user.PreferredName, user.PreferredName
                        nameof user.PasswordHash,  user.PasswordHash
                        nameof user.Salt,          user.Salt
                        nameof user.Url,           user.Url
                        nameof user.AccessLevel,   user.AccessLevel
                        ]
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.Serializer =
            Net.Converter.Serializer
        
        member _.StartUp () = backgroundTask {
            let! dbs = rethink<string list> { dbList; result; withRetryOnce conn }
            if not (dbs |> List.contains config.Database) then
                log.LogInformation $"Creating database {config.Database}..."
                do! rethink { dbCreate config.Database; write; withRetryOnce; ignoreResult conn }
            
            let! tables = rethink<string list> { tableList; result; withRetryOnce conn }
            for tbl in Table.all do
                if not (tables |> List.contains tbl) then
                    log.LogInformation $"Creating table {tbl}..."
                    do! rethink { tableCreate tbl [ PrimaryKey "Id" ]; write; withRetryOnce; ignoreResult conn }

            do! ensureIndexes Table.Category   [ nameof Category.empty.WebLogId ]
            do! ensureIndexes Table.Comment    [ nameof Comment.empty.PostId ]
            do! ensureIndexes Table.Page       [ nameof Page.empty.WebLogId; nameof Page.empty.AuthorId ]
            do! ensureIndexes Table.Post       [ nameof Post.empty.WebLogId; nameof Post.empty.AuthorId ]
            do! ensureIndexes Table.TagMap     []
            do! ensureIndexes Table.Upload     []
            do! ensureIndexes Table.WebLog     [ nameof WebLog.empty.UrlBase ]
            do! ensureIndexes Table.WebLogUser [ nameof WebLogUser.empty.WebLogId ]
            
            let! version = rethink<DbVersion list> {
                 withTable Table.DbVersion
                 result; withRetryOnce conn
            }
            match List.tryHead version with
            | Some v when v.Id = "v2-rc2" -> ()
            // Future migrations will be checked here
            | Some _
            | None ->
                log.LogWarning $"Unknown database version; assuming {Utils.currentDbVersion}"
                do! setDbVersion Utils.currentDbVersion
        }
