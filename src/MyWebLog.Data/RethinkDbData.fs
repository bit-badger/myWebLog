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
        let all = [ Category; Comment; Page; Post; TagMap; Theme; ThemeAsset; Upload; WebLog; WebLogUser ]


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


open System
open Microsoft.Extensions.Logging
open MyWebLog.ViewModels
open RethinkDb.Driver.FSharp

/// RethinkDB implementation of data functions for myWebLog
type RethinkDbData (conn : Net.IConnection, config : DataConfig, log : ILogger<RethinkDbData>) =
    
    /// Match theme asset IDs by their prefix (the theme ID)
    let matchAssetByThemeId themeId =
        let keyPrefix = $"^{ThemeId.toString themeId}/"
        fun (row : Ast.ReqlExpr) -> row["id"].Match keyPrefix :> obj
    
    /// Ensure field indexes exist, as well as special indexes for selected tables
    let ensureIndexes table fields = backgroundTask {
        let! indexes = rethink<string list> { withTable table; indexList; result; withRetryOnce conn }
        for field in fields do
            if not (indexes |> List.contains field) then
                log.LogInformation $"Creating index {table}.{field}..."
                do! rethink { withTable table; indexCreate field; write; withRetryOnce; ignoreResult conn }
        // Post and page need index by web log ID and permalink
        if [ Table.Page; Table.Post ] |> List.contains table then
            if not (indexes |> List.contains "permalink") then
                log.LogInformation $"Creating index {table}.permalink..."
                do! rethink {
                    withTable table
                    indexCreate "permalink" (fun row -> r.Array (row["webLogId"], row["permalink"].Downcase ()) :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
            // Prior permalinks are searched when a post or page permalink do not match the current URL
            if not (indexes |> List.contains "priorPermalinks") then
                log.LogInformation $"Creating index {table}.priorPermalinks..."
                do! rethink {
                    withTable table
                    indexCreate "priorPermalinks" (fun row -> row["priorPermalinks"].Downcase () :> obj) [ Multi ]
                    write; withRetryOnce; ignoreResult conn
                }
        // Post needs indexes by category and tag (used for counting and retrieving posts)
        if Table.Post = table then
            for idx in [ "categoryIds"; "tags" ] do
                if not (List.contains idx indexes) then
                    log.LogInformation $"Creating index {table}.{idx}..."
                    do! rethink {
                        withTable table
                        indexCreate idx [ Multi ]
                        write; withRetryOnce; ignoreResult conn
                    }
        // Tag mapping needs an index by web log ID and both tag and URL values
        if Table.TagMap = table then
            if not (indexes |> List.contains "webLogAndTag") then
                log.LogInformation $"Creating index {table}.webLogAndTag..."
                do! rethink {
                    withTable table
                    indexCreate "webLogAndTag" (fun row -> r.Array (row["webLogId"], row["tag"]) :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
            if not (indexes |> List.contains "webLogAndUrl") then
                log.LogInformation $"Creating index {table}.webLogAndUrl..."
                do! rethink {
                    withTable table
                    indexCreate "webLogAndUrl" (fun row -> r.Array (row["webLogId"], row["urlValue"]) :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
        // Uploaded files need an index by web log ID and path, as that is how they are retrieved
        if Table.Upload = table then
            if not (indexes |> List.contains "webLogAndPath") then
                log.LogInformation $"Creating index {table}.webLogAndPath..."
                do! rethink {
                    withTable table
                    indexCreate "webLogAndPath" (fun row -> r.Array (row["webLogId"], row["path"]) :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
        // Users log on with e-mail
        if Table.WebLogUser = table && not (indexes |> List.contains "logOn") then
            log.LogInformation $"Creating index {table}.logOn..."
            do! rethink {
                withTable table
                indexCreate "logOn" (fun row -> r.Array (row["webLogId"], row["userName"]) :> obj)
                write; withRetryOnce; ignoreResult conn
            }
    }
    
    /// The batch size for restoration methods
    let restoreBatchSize = 100
    
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
                    getAll [ webLogId ] (nameof webLogId)
                    count
                    result; withRetryDefault conn
                }

                member _.CountTopLevel webLogId = rethink<int> {
                    withTable Table.Category
                    getAll [ webLogId ] (nameof webLogId)
                    filter "parentId" None
                    count
                    result; withRetryDefault conn
                }
                
                member _.FindAllForView webLogId = backgroundTask {
                    let! cats = rethink<Category list> {
                        withTable Table.Category
                        getAll [ webLogId ] (nameof webLogId)
                        orderByFunc (fun it -> it["name"].Downcase () :> obj)
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
                                getAll catIds "categoryIds"
                                filter "status" Published
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
                    |> verifyWebLog webLogId (fun c -> c.webLogId) <| conn
                
                member _.FindByWebLog webLogId = rethink<Category list> {
                    withTable Table.Category
                    getAll [ webLogId ] (nameof webLogId)
                    result; withRetryDefault conn
                }
                
                member this.Delete catId webLogId = backgroundTask {
                    match! this.FindById catId webLogId with
                    | Some _ ->
                        // Delete the category off all posts where it is assigned
                        do! rethink {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof webLogId)
                            filter (fun row -> row["categoryIds"].Contains catId :> obj)
                            update (fun row -> r.HashMap ("categoryIds", r.Array(row["categoryIds"]).Remove catId) :> obj)
                            write; withRetryDefault; ignoreResult conn 
                        }
                        // Delete the category itself
                        do! rethink {
                            withTable Table.Category
                            get catId
                            delete
                            write; withRetryDefault; ignoreResult conn
                        }
                        return true
                    | None -> return false
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
                    get cat.id
                    update [ "name",        cat.name :> obj
                             "slug",        cat.slug
                             "description", cat.description
                             "parentId",    cat.parentId
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
                    getAll [ webLogId ] (nameof webLogId)
                    without [ "text"; "metadata"; "revisions"; "priorPermalinks" ]
                    orderByFunc (fun row -> row["title"].Downcase () :> obj)
                    result; withRetryDefault conn
                }
                
                member _.CountAll webLogId = rethink<int> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof webLogId)
                    count
                    result; withRetryDefault conn
                }

                member _.CountListed webLogId = rethink<int> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof webLogId)
                    filter "showInPageList" true
                    count
                    result; withRetryDefault conn
                }

                member _.Delete pageId webLogId = backgroundTask {
                    let! result = rethink<Model.Result> {
                        withTable Table.Page
                        getAll [ pageId ]
                        filter (fun row -> row["webLogId"].Eq webLogId :> obj)
                        delete
                        write; withRetryDefault conn
                    }
                    return result.Deleted > 0UL
                }
                
                member _.FindById pageId webLogId =
                    rethink<Page> {
                        withTable Table.Page
                        get pageId
                        without [ "priorPermalinks"; "revisions" ]
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun it -> it.webLogId) <| conn

                member _.FindByPermalink permalink webLogId =
                    rethink<Page list> {
                        withTable Table.Page
                        getAll [ r.Array (webLogId, permalink) ] (nameof permalink)
                        without [ "priorPermalinks"; "revisions" ]
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        (rethink<Page list> {
                            withTable Table.Page
                            getAll (objList permalinks) "priorPermalinks"
                            filter "webLogId" webLogId
                            without [ "revisions"; "text" ]
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst) conn
                    return result |> Option.map (fun pg -> pg.permalink)
                }
                
                member _.FindFullById pageId webLogId =
                    rethink<Page> {
                        withTable Table.Page
                        get pageId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun it -> it.webLogId) <| conn
                
                member _.FindFullByWebLog webLogId = rethink<Page> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof webLogId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindListed webLogId = rethink<Page list> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof webLogId)
                    filter [ "showInPageList", true :> obj ]
                    without [ "text"; "priorPermalinks"; "revisions" ]
                    orderBy "title"
                    result; withRetryDefault conn
                }

                member _.FindPageOfPages webLogId pageNbr = rethink<Page list> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof webLogId)
                    without [ "metadata"; "priorPermalinks"; "revisions" ]
                    orderByFunc (fun row -> row["title"].Downcase ())
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
                    get page.id
                    update [
                        "title",           page.title :> obj
                        "permalink",       page.permalink
                        "updatedOn",       page.updatedOn
                        "showInPageList",  page.showInPageList
                        "template",        page.template
                        "text",            page.text
                        "priorPermalinks", page.priorPermalinks
                        "metadata",        page.metadata
                        "revisions",       page.revisions
                        ]
                    write; withRetryDefault; ignoreResult conn
                }
                
                member this.UpdatePriorPermalinks pageId webLogId permalinks = backgroundTask {
                    match! this.FindById pageId webLogId with
                    | Some _ ->
                        do! rethink {
                            withTable Table.Page
                            get pageId
                            update [ "priorPermalinks", permalinks :> obj ]
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
                    getAll [ webLogId ] (nameof webLogId)
                    filter "status" status
                    count
                    result; withRetryDefault conn
                }

                member _.Delete postId webLogId = backgroundTask {
                    let! result = rethink<Model.Result> {
                        withTable Table.Post
                        getAll [ postId ]
                        filter (fun row -> row["webLogId"].Eq webLogId :> obj)
                        delete
                        write; withRetryDefault conn
                    }
                    return result.Deleted > 0UL
                }
                
                member _.FindById postId webLogId =
                    rethink<Post> {
                        withTable Table.Post
                        get postId
                        without [ "priorPermalinks"; "revisions" ]
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun p -> p.webLogId) <| conn
                
                member _.FindByPermalink permalink webLogId =
                    rethink<Post list> {
                        withTable Table.Post
                        getAll [ r.Array (webLogId, permalink) ] (nameof permalink)
                        without [ "priorPermalinks"; "revisions" ]
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
                    |> verifyWebLog webLogId (fun p -> p.webLogId) <| conn

                member _.FindCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        (rethink<Post list> {
                            withTable Table.Post
                            getAll (objList permalinks) "priorPermalinks"
                            filter "webLogId" webLogId
                            without [ "revisions"; "text" ]
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst) conn
                    return result |> Option.map (fun post -> post.permalink)
                }
                
                member _.FindFullByWebLog webLogId = rethink<Post> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof webLogId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll (objList categoryIds) "categoryIds"
                    filter "webLogId" webLogId
                    filter "status" Published
                    without [ "priorPermalinks"; "revisions" ]
                    distinct
                    orderByDescending "publishedOn"
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindPageOfPosts webLogId pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof webLogId)
                    without [ "priorPermalinks"; "revisions" ]
                    orderByFuncDescending (fun row -> row["publishedOn"].Default_ "updatedOn" :> obj)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }

                member _.FindPageOfPublishedPosts webLogId pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof webLogId)
                    filter "status" Published
                    without [ "priorPermalinks"; "revisions" ]
                    orderByDescending "publishedOn"
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindPageOfTaggedPosts webLogId tag pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ tag ] "tags"
                    filter "webLogId" webLogId
                    filter "status" Published
                    without [ "priorPermalinks"; "revisions" ]
                    orderByDescending "publishedOn"
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindSurroundingPosts webLogId publishedOn = backgroundTask {
                    let! older =
                        rethink<Post list> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof webLogId)
                            filter (fun row -> row["publishedOn"].Lt publishedOn :> obj)
                            without [ "priorPermalinks"; "revisions" ]
                            orderByDescending "publishedOn"
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst <| conn
                    let! newer =
                        rethink<Post list> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof webLogId)
                            filter (fun row -> row["publishedOn"].Gt publishedOn :> obj)
                            without [ "priorPermalinks"; "revisions" ]
                            orderBy "publishedOn"
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
                    get post.id
                    replace post
                    write; withRetryDefault; ignoreResult conn
                }

                member _.UpdatePriorPermalinks postId webLogId permalinks = backgroundTask {
                    match! (
                        rethink<Post> {
                            withTable Table.Post
                            get postId
                            without [ "revisions"; "priorPermalinks" ]
                            resultOption; withRetryOptionDefault
                        }
                        |> verifyWebLog webLogId (fun p -> p.webLogId)) conn with
                    | Some _ ->
                        do! rethink {
                            withTable Table.Post
                            get postId
                            update [ "priorPermalinks", permalinks :> obj ]
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
                        filter (fun row -> row["webLogId"].Eq webLogId :> obj)
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
                    |> verifyWebLog webLogId (fun tm -> tm.webLogId) <| conn
                
                member _.FindByUrlValue urlValue webLogId =
                    rethink<TagMap list> {
                        withTable Table.TagMap
                        getAll [ r.Array (webLogId, urlValue) ] "webLogAndUrl"
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindByWebLog webLogId = rethink<TagMap list> {
                    withTable Table.TagMap
                    between (r.Array (webLogId, r.Minval ())) (r.Array (webLogId, r.Maxval ())) [ Index "webLogAndTag" ]
                    orderBy "tag"
                    result; withRetryDefault conn
                }
                
                member _.FindMappingForTags tags webLogId = rethink<TagMap list> {
                    withTable Table.TagMap
                    getAll (tags |> List.map (fun tag -> r.Array (webLogId, tag) :> obj)) "webLogAndTag"
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
                    get tagMap.id
                    replace tagMap
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.Theme = {
            new IThemeData with
                
                member _.All () = rethink<Theme list> {
                    withTable Table.Theme
                    filter (fun row -> row["id"].Ne "admin" :> obj)
                    without [ "templates" ]
                    orderBy "id"
                    result; withRetryDefault conn
                }
                
                member _.FindById themeId = rethink<Theme> {
                    withTable Table.Theme
                    get themeId
                    resultOption; withRetryOptionDefault conn
                }
                
                member _.FindByIdWithoutText themeId = rethink<Theme> {
                    withTable Table.Theme
                    get themeId
                    merge (fun row -> r.HashMap ("templates", row["templates"].Without [| "text" |]))
                    resultOption; withRetryOptionDefault conn
                }
                
                member _.Save theme = rethink {
                    withTable Table.Theme
                    get theme.id
                    replace theme
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.ThemeAsset = {
            new IThemeAssetData with
                
                member _.All () = rethink<ThemeAsset list> {
                    withTable Table.ThemeAsset
                    without [ "data" ]
                    result; withRetryDefault conn
                }
                
                member _.DeleteByTheme themeId = rethink {
                    withTable Table.ThemeAsset
                    filter (matchAssetByThemeId themeId)
                    delete
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.FindById assetId = rethink<ThemeAsset> {
                    withTable Table.ThemeAsset
                    get assetId
                    resultOption; withRetryOptionDefault conn
                }
                
                member _.FindByTheme themeId = rethink<ThemeAsset list> {
                    withTable Table.ThemeAsset
                    filter (matchAssetByThemeId themeId)
                    without [ "data" ]
                    result; withRetryDefault conn
                }
                
                member _.FindByThemeWithData themeId = rethink<ThemeAsset> {
                    withTable Table.ThemeAsset
                    filter (matchAssetByThemeId themeId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.Save asset = rethink {
                    withTable Table.ThemeAsset
                    get asset.id
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
                        |> verifyWebLog<Upload> webLogId (fun u -> u.webLogId) <| conn
                    match upload with
                    | Some up ->
                        do! rethink {
                            withTable Table.Upload
                            get uploadId
                            delete
                            write; withRetryDefault; ignoreResult conn
                        }
                        return Ok (Permalink.toString up.path)
                    | None -> return Result.Error $"Upload ID {UploadId.toString uploadId} not found"
                }
                
                member _.FindByPath path webLogId =
                    rethink<Upload> {
                        withTable Table.Upload
                        getAll [ r.Array (webLogId, path) ] "webLogAndPath"
                        resultCursor; withRetryCursorDefault; toList
                    }
                    |> tryFirst <| conn
                
                member _.FindByWebLog webLogId = rethink<Upload> {
                    withTable Table.Upload
                    between (r.Array (webLogId, r.Minval ())) (r.Array (webLogId, r.Maxval ()))
                        [ Index "webLogAndPath" ]
                    without [ "data" ]
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindByWebLogWithData webLogId = rethink<Upload> {
                    withTable Table.Upload
                    between (r.Array (webLogId, r.Minval ())) (r.Array (webLogId, r.Maxval ()))
                        [ Index "webLogAndPath" ]
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
                     let! thePostIds = rethink<{| id : string |} list> {
                         withTable Table.Post
                         getAll [ webLogId ] (nameof webLogId)
                         pluck [ "id" ]
                         result; withRetryOnce conn
                     }
                     if not (List.isEmpty thePostIds) then
                         let postIds = thePostIds |> List.map (fun it -> it.id :> obj)
                         do! rethink {
                             withTable Table.Comment
                             getAll postIds "postId"
                             delete
                             write; withRetryOnce; ignoreResult conn
                         }
                     // Tag mappings do not have a straightforward webLogId index
                     do! rethink {
                         withTable Table.TagMap
                         between (r.Array (webLogId, r.Minval ())) (r.Array (webLogId, r.Maxval ()))
                                     [ Index "webLogAndTag" ]
                         delete
                         write; withRetryOnce; ignoreResult conn
                     }
                     // Uploaded files do not have a straightforward webLogId index
                     do! rethink {
                         withTable Table.Upload
                         between (r.Array (webLogId, r.Minval ())) (r.Array (webLogId, r.Maxval ()))
                                     [ Index "webLogAndPath" ]
                         delete
                         write; withRetryOnce; ignoreResult conn
                     }
                     for table in [ Table.Post; Table.Category; Table.Page; Table.WebLogUser ] do
                         do! rethink {
                             withTable table
                             getAll [ webLogId ] (nameof webLogId)
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
                        getAll [ url ] "urlBase"
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
                    get webLog.id
                    update [ "rss", webLog.rss :> obj ]
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.UpdateSettings webLog = rethink {
                    withTable Table.WebLog
                    get webLog.id
                    update [
                        "name",         webLog.name :> obj
                        "slug",         webLog.slug
                        "subtitle",     webLog.subtitle
                        "defaultPage",  webLog.defaultPage
                        "postsPerPage", webLog.postsPerPage
                        "timeZone",     webLog.timeZone
                        "themePath",    webLog.themePath
                        "autoHtmx",     webLog.autoHtmx
                        "uploads",      webLog.uploads
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
                
                member _.FindByEmail email webLogId =
                    rethink<WebLogUser list> {
                        withTable Table.WebLogUser
                        getAll [ r.Array (webLogId, email) ] "logOn"
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindById userId webLogId =
                    rethink<WebLogUser> {
                        withTable Table.WebLogUser
                        get userId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId (fun u -> u.webLogId) <| conn
                
                member _.FindByWebLog webLogId = rethink<WebLogUser list> {
                    withTable Table.WebLogUser
                    getAll [ webLogId ] (nameof webLogId)
                    result; withRetryDefault conn
                }
                
                member _.FindNames webLogId userIds = backgroundTask {
                    let! users = rethink<WebLogUser list> {
                        withTable Table.WebLogUser
                        getAll (objList userIds)
                        filter "webLogId" webLogId
                        result; withRetryDefault conn
                    }
                    return
                        users
                        |> List.map (fun u -> { name = WebLogUserId.toString u.id; value = WebLogUser.displayName u })
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
                            update [ "lastSeenOn", DateTime.UtcNow :> obj ]
                            write; withRetryOnce; ignoreResult conn
                        }
                    | None -> ()
                }
                
                member _.Update user = rethink {
                    withTable Table.WebLogUser
                    get user.id
                    update [
                        "firstName",     user.firstName :> obj
                        "lastName",      user.lastName
                        "preferredName", user.preferredName
                        "passwordHash",  user.passwordHash
                        "salt",          user.salt
                        "accessLevel",   user.accessLevel
                        ]
                    write; withRetryDefault; ignoreResult conn
                }
        }
        
        member _.StartUp () = backgroundTask {
            let! dbs = rethink<string list> { dbList; result; withRetryOnce conn }
            if not (dbs |> List.contains config.Database) then
                log.LogInformation $"Creating database {config.Database}..."
                do! rethink { dbCreate config.Database; write; withRetryOnce; ignoreResult conn }
            
            let! tables = rethink<string list> { tableList; result; withRetryOnce conn }
            for tbl in Table.all do
                if not (tables |> List.contains tbl) then
                    log.LogInformation $"Creating table {tbl}..."
                    do! rethink { tableCreate tbl; write; withRetryOnce; ignoreResult conn }

            do! ensureIndexes Table.Category   [ "webLogId" ]
            do! ensureIndexes Table.Comment    [ "postId" ]
            do! ensureIndexes Table.Page       [ "webLogId"; "authorId" ]
            do! ensureIndexes Table.Post       [ "webLogId"; "authorId" ]
            do! ensureIndexes Table.TagMap     []
            do! ensureIndexes Table.Upload     []
            do! ensureIndexes Table.WebLog     [ "urlBase" ]
            do! ensureIndexes Table.WebLogUser [ "webLogId" ]
        }
