namespace MyWebLog.Data

open System.Threading.Tasks
open MyWebLog
open RethinkDb.Driver

/// Functions to assist with retrieving data
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
    let verifyWebLog<'T> webLogId (prop: 'T -> WebLogId) (f: Net.IConnection -> Task<'T option>) =
        fun conn -> backgroundTask {
            match! f conn with Some it when (prop it) = webLogId -> return Some it | _ -> return None
        }
    
    /// Get the first item from a list, or None if the list is empty
    let tryFirst<'T> (f: Net.IConnection -> Task<'T list>) =
        fun conn -> backgroundTask {
            let! results = f conn
            return results |> List.tryHead
        }
    
    /// Cast a strongly-typed list to an object list
    let objList<'T> (objects: 'T list) = objects |> List.map (fun it -> it :> obj)


open System
open Microsoft.Extensions.Logging
open MyWebLog.ViewModels
open RethinkDb.Driver.FSharp
open RethinkHelpers

/// RethinkDB implementation of data functions for myWebLog
type RethinkDbData(conn: Net.IConnection, config: DataConfig, log: ILogger<RethinkDbData>) =
    
    /// Match theme asset IDs by their prefix (the theme ID)
    let matchAssetByThemeId themeId =
        let keyPrefix = $"^{themeId}/"
        fun (row: Ast.ReqlExpr) -> row[nameof ThemeAsset.Empty.Id].Match keyPrefix :> obj
    
    /// Function to exclude template text from themes
    let withoutTemplateText (row: Ast.ReqlExpr) : obj =
        {|  Templates = row[nameof Theme.Empty.Templates].Merge(r.HashMap(nameof ThemeTemplate.Empty.Text, "")) |}
        
    /// Ensure field indexes exist, as well as special indexes for selected tables
    let ensureIndexes table fields = backgroundTask {
        let! indexes = rethink<string list> { withTable table; indexList; result; withRetryOnce conn }
        for field in fields do
            if not (indexes |> List.contains field) then
                log.LogInformation $"Creating index {table}.{field}..."
                do! rethink { withTable table; indexCreate field; write; withRetryOnce; ignoreResult conn }
        // Post and page need index by web log ID and permalink
        if [ Table.Page; Table.Post ] |> List.contains table then
            let permalinkIdx = nameof Page.Empty.Permalink
            if not (indexes |> List.contains permalinkIdx) then
                log.LogInformation $"Creating index {table}.{permalinkIdx}..."
                do! rethink {
                    withTable table
                    indexCreate permalinkIdx
                        (fun row -> r.Array(row[nameof Page.Empty.WebLogId], row[permalinkIdx].Downcase()) :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
            // Prior permalinks are searched when a post or page permalink do not match the current URL
            let priorIdx = nameof Post.Empty.PriorPermalinks
            if not (indexes |> List.contains priorIdx) then
                log.LogInformation $"Creating index {table}.{priorIdx}..."
                do! rethink {
                    withTable table
                    indexCreate priorIdx [ Multi ]
                    write; withRetryOnce; ignoreResult conn
                }
        // Post needs indexes by category and tag (used for counting and retrieving posts)
        if Table.Post = table then
            for idx in [ nameof Post.Empty.CategoryIds; nameof Post.Empty.Tags ] do
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
                        [| row[nameof TagMap.Empty.WebLogId]; row[nameof TagMap.Empty.Tag] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
            if not (indexes |> List.contains Index.WebLogAndUrl) then
                log.LogInformation $"Creating index {table}.{Index.WebLogAndUrl}..."
                do! rethink {
                    withTable table
                    indexCreate Index.WebLogAndUrl (fun row ->
                        [| row[nameof TagMap.Empty.WebLogId]; row[nameof TagMap.Empty.UrlValue] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
        // Uploaded files need an index by web log ID and path, as that is how they are retrieved
        if Table.Upload = table then
            if not (indexes |> List.contains Index.WebLogAndPath) then
                log.LogInformation $"Creating index {table}.{Index.WebLogAndPath}..."
                do! rethink {
                    withTable table
                    indexCreate Index.WebLogAndPath (fun row ->
                        [| row[nameof Upload.Empty.WebLogId]; row[nameof Upload.Empty.Path] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
        // Users log on with e-mail
        if Table.WebLogUser = table then
            if not (indexes |> List.contains Index.LogOn) then
                log.LogInformation $"Creating index {table}.{Index.LogOn}..."
                do! rethink {
                    withTable table
                    indexCreate Index.LogOn (fun row ->
                        [| row[nameof WebLogUser.Empty.WebLogId]; row[nameof WebLogUser.Empty.Email] |] :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
        do! rethink { withTable table; indexWait; result; withRetryDefault; ignoreResult conn }
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
    let setDbVersion (version: string) = backgroundTask {
        do! rethink {
            withTable Table.DbVersion
            delete
            write; withRetryOnce; ignoreResult conn
        }
        do! rethink {
            withTable Table.DbVersion
            insert {| Id = version |}
            write; withRetryOnce; ignoreResult conn
        }
    }
    
    /// Migrate from v2-rc1 to v2-rc2
    let migrateV2Rc1ToV2Rc2 () = backgroundTask {
        let logStep = Utils.Migration.logStep log "v2-rc1 to v2-rc2"
        logStep "**IMPORTANT**"
        logStep "See release notes about required backup/restoration for RethinkDB."
        logStep "If there is an error immediately below this message, this is why."
        logStep "Setting database version to v2-rc2"
        do! setDbVersion "v2-rc2"
    }

    /// Migrate from v2-rc2 to v2
    let migrateV2Rc2ToV2 () = backgroundTask {
        Utils.Migration.logStep log "v2-rc2 to v2" "Setting database version; no migration required"
        do! setDbVersion "v2"
    }

    /// Migrate from v2 to v2.1
    let migrateV2ToV2point1 () = backgroundTask {
        Utils.Migration.logStep log "v2 to v2.1" "Adding empty redirect rule set to all weblogs"
        do! rethink {
            withTable Table.WebLog
            update [ nameof WebLog.Empty.RedirectRules, [] :> obj ]
            write; withRetryOnce; ignoreResult conn
        }
        
        Utils.Migration.logStep log "v2 to v2.1" "Setting database version to v2.1"
        do! setDbVersion "v2.1"
    }
    
    /// Migrate data between versions
    let migrate version = backgroundTask {
        let mutable v = defaultArg version ""
        
        if v = "v2-rc1" then
            do! migrateV2Rc1ToV2Rc2 ()
            v <- "v2-rc2"

        if v = "v2-rc2" then
            do! migrateV2Rc2ToV2 ()
            v <- "v2"
        
        if v = "v2" then
            do! migrateV2ToV2point1 ()
            v <- "v2.1"
        
        if v <> Utils.Migration.currentDbVersion then
            log.LogWarning $"Unknown database version; assuming {Utils.Migration.currentDbVersion}"
            do! setDbVersion Utils.Migration.currentDbVersion
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
                    getAll [ webLogId ] (nameof Category.Empty.WebLogId)
                    count
                    result; withRetryDefault conn
                }

                member _.CountTopLevel webLogId = rethink<int> {
                    withTable Table.Category
                    getAll [ webLogId ] (nameof Category.Empty.WebLogId)
                    filter (nameof Category.Empty.ParentId) None (Default FilterDefaultHandling.Return)
                    count
                    result; withRetryDefault conn
                }
                
                member _.FindAllForView webLogId = backgroundTask {
                    let! cats = rethink<Category list> {
                        withTable Table.Category
                        getAll [ webLogId ] (nameof Category.Empty.WebLogId)
                        orderByFunc (fun it -> it[nameof Category.Empty.Name].Downcase() :> obj)
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
                                getAll catIds (nameof Post.Empty.CategoryIds)
                                filter (nameof Post.Empty.Status) Published
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
                                            |> Option.defaultValue 0 })
                        |> Array.ofSeq
                }
                
                member _.FindById catId webLogId =
                    rethink<Category> {
                        withTable Table.Category
                        get catId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId _.WebLogId <| conn
                
                member _.FindByWebLog webLogId = rethink<Category list> {
                    withTable Table.Category
                    getAll [ webLogId ] (nameof Category.Empty.WebLogId)
                    result; withRetryDefault conn
                }
                
                member this.Delete catId webLogId = backgroundTask {
                    match! this.FindById catId webLogId with
                    | Some cat ->
                        // Reassign any children to the category's parent category
                        let! children = rethink<int> {
                            withTable Table.Category
                            filter (nameof Category.Empty.ParentId) catId
                            count
                            result; withRetryDefault conn
                        }
                        if children > 0 then
                            do! rethink {
                                withTable Table.Category
                                filter (nameof Category.Empty.ParentId) catId
                                update [ nameof Category.Empty.ParentId, cat.ParentId :> obj ]
                                write; withRetryDefault; ignoreResult conn
                            }
                        // Delete the category off all posts where it is assigned
                        do! rethink {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                            filter (fun row -> row[nameof Post.Empty.CategoryIds].Contains catId :> obj)
                            update (fun row ->
                                {| CategoryIds =
                                        row[nameof Post.Empty.CategoryIds].CoerceTo("array")
                                            .SetDifference(r.Array(catId)) |} :> obj)
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
                    getAll [ webLogId ] (nameof Page.Empty.WebLogId)
                    merge (r.HashMap(nameof Page.Empty.Text, "")
                               .With(nameof Page.Empty.Metadata, [||])
                               .With(nameof Page.Empty.Revisions, [||])
                               .With(nameof Page.Empty.PriorPermalinks, [||]))
                    orderByFunc (fun row -> row[nameof Page.Empty.Title].Downcase() :> obj)
                    result; withRetryDefault conn
                }
                
                member _.CountAll webLogId = rethink<int> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.Empty.WebLogId)
                    count
                    result; withRetryDefault conn
                }

                member _.CountListed webLogId = rethink<int> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.Empty.WebLogId)
                    filter (nameof Page.Empty.IsInPageList) true
                    count
                    result; withRetryDefault conn
                }

                member _.Delete pageId webLogId = backgroundTask {
                    let! result = rethink<Model.Result> {
                        withTable Table.Page
                        getAll [ pageId ]
                        filter (fun row -> row[nameof Page.Empty.WebLogId].Eq webLogId :> obj)
                        delete
                        write; withRetryDefault conn
                    }
                    return result.Deleted > 0UL
                }
                
                member _.FindById pageId webLogId =
                    rethink<Page list> {
                        withTable Table.Page
                        getAll [ pageId ]
                        filter (nameof Page.Empty.WebLogId) webLogId
                        merge (r.HashMap(nameof Page.Empty.PriorPermalinks, [||])
                                   .With(nameof Page.Empty.Revisions, [||]))
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn

                member _.FindByPermalink permalink webLogId =
                    rethink<Page list> {
                        withTable Table.Page
                        getAll [ [| webLogId :> obj; permalink |] ] (nameof Page.Empty.Permalink)
                        merge (r.HashMap(nameof Page.Empty.PriorPermalinks, [||])
                                   .With(nameof Page.Empty.Revisions, [||]))
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        (rethink<Page list> {
                            withTable Table.Page
                            getAll (objList permalinks) (nameof Page.Empty.PriorPermalinks)
                            filter (nameof Page.Empty.WebLogId) webLogId
                            without [ nameof Page.Empty.Revisions; nameof Page.Empty.Text ]
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst) conn
                    return result |> Option.map _.Permalink
                }
                
                member _.FindFullById pageId webLogId =
                    rethink<Page> {
                        withTable Table.Page
                        get pageId
                        resultOption; withRetryOptionDefault
                    }
                    |> verifyWebLog webLogId _.WebLogId <| conn
                
                member _.FindFullByWebLog webLogId = rethink<Page> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.Empty.WebLogId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindListed webLogId = rethink<Page list> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.Empty.WebLogId)
                    filter [ nameof Page.Empty.IsInPageList, true :> obj ]
                    merge (r.HashMap(nameof Page.Empty.Text, "")
                               .With(nameof Page.Empty.PriorPermalinks, [||])
                               .With(nameof Page.Empty.Revisions, [||]))
                    orderBy (nameof Page.Empty.Title)
                    result; withRetryDefault conn
                }

                member _.FindPageOfPages webLogId pageNbr = rethink<Page list> {
                    withTable Table.Page
                    getAll [ webLogId ] (nameof Page.Empty.WebLogId)
                    merge (r.HashMap(nameof Page.Empty.Metadata, [||])
                               .With(nameof Page.Empty.PriorPermalinks, [||])
                               .With(nameof Page.Empty.Revisions, [||]))
                    orderByFunc (fun row -> row[nameof Page.Empty.Title].Downcase())
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
                            update [ nameof Page.Empty.PriorPermalinks, permalinks :> obj ]
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
                    getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                    filter (nameof Post.Empty.Status) status
                    count
                    result; withRetryDefault conn
                }

                member _.Delete postId webLogId = backgroundTask {
                    let! result = rethink<Model.Result> {
                        withTable Table.Post
                        getAll [ postId ]
                        filter (fun row -> row[nameof Post.Empty.WebLogId].Eq webLogId :> obj)
                        delete
                        write; withRetryDefault conn
                    }
                    return result.Deleted > 0UL
                }
                
                member _.FindById postId webLogId =
                    rethink<Post list> {
                        withTable Table.Post
                        getAll [ postId ]
                        filter (nameof Post.Empty.WebLogId) webLogId
                        merge (r.HashMap(nameof Post.Empty.PriorPermalinks, [||])
                                   .With(nameof Post.Empty.Revisions, [||]))
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
                member _.FindByPermalink permalink webLogId =
                    rethink<Post list> {
                        withTable Table.Post
                        getAll [ [| webLogId :> obj; permalink |] ] (nameof Post.Empty.Permalink)
                        merge (r.HashMap(nameof Post.Empty.PriorPermalinks, [||])
                                   .With(nameof Post.Empty.Revisions, [||]))
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
                    |> verifyWebLog webLogId _.WebLogId <| conn

                member _.FindCurrentPermalink permalinks webLogId = backgroundTask {
                    let! result =
                        (rethink<Post list> {
                            withTable Table.Post
                            getAll (objList permalinks) (nameof Post.Empty.PriorPermalinks)
                            filter (nameof Post.Empty.WebLogId) webLogId
                            without [ nameof Post.Empty.Revisions; nameof Post.Empty.Text ]
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst) conn
                    return result |> Option.map _.Permalink
                }
                
                member _.FindFullByWebLog webLogId = rethink<Post> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindPageOfCategorizedPosts webLogId categoryIds pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll (objList categoryIds) (nameof Post.Empty.CategoryIds)
                    filter [ nameof Post.Empty.WebLogId, webLogId :> obj
                             nameof Post.Empty.Status,   Published ]
                    merge (r.HashMap(nameof Post.Empty.PriorPermalinks, [||])
                               .With(nameof Post.Empty.Revisions, [||]))
                    distinct
                    orderByDescending (nameof Post.Empty.PublishedOn)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindPageOfPosts webLogId pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                    merge (r.HashMap(nameof Post.Empty.Text, "")
                               .With(nameof Post.Empty.PriorPermalinks, [||])
                               .With(nameof Post.Empty.Revisions, [||]))
                    orderByFuncDescending (fun row ->
                        row[nameof Post.Empty.PublishedOn].Default_(nameof Post.Empty.UpdatedOn) :> obj)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }

                member _.FindPageOfPublishedPosts webLogId pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                    filter (nameof Post.Empty.Status) Published
                    merge (r.HashMap(nameof Post.Empty.PriorPermalinks, [||])
                               .With(nameof Post.Empty.Revisions, [||]))
                    orderByDescending (nameof Post.Empty.PublishedOn)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindPageOfTaggedPosts webLogId tag pageNbr postsPerPage = rethink<Post list> {
                    withTable Table.Post
                    getAll [ tag ] (nameof Post.Empty.Tags)
                    filter [ nameof Post.Empty.WebLogId, webLogId :> obj
                             nameof Post.Empty.Status,   Published ]
                    merge (r.HashMap(nameof Post.Empty.PriorPermalinks, [||])
                               .With(nameof Post.Empty.Revisions, [||]))
                    orderByDescending (nameof Post.Empty.PublishedOn)
                    skip ((pageNbr - 1) * postsPerPage)
                    limit (postsPerPage + 1)
                    result; withRetryDefault conn
                }
                
                member _.FindSurroundingPosts webLogId publishedOn = backgroundTask {
                    let! older =
                        rethink<Post list> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                            filter (fun row -> row[nameof Post.Empty.PublishedOn].Lt publishedOn :> obj)
                            merge (r.HashMap(nameof Post.Empty.PriorPermalinks, [||])
                                       .With(nameof Post.Empty.Revisions, [||]))
                            orderByDescending (nameof Post.Empty.PublishedOn)
                            limit 1
                            result; withRetryDefault
                        }
                        |> tryFirst <| conn
                    let! newer =
                        rethink<Post list> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                            filter (fun row -> row[nameof Post.Empty.PublishedOn].Gt publishedOn :> obj)
                            merge (r.HashMap(nameof Post.Empty.PriorPermalinks, [||])
                                       .With(nameof Post.Empty.Revisions, [||]))
                            orderBy (nameof Post.Empty.PublishedOn)
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
                
                member this.Update post = backgroundTask {
                    match! this.FindById post.Id post.WebLogId with
                    | Some _ ->
                        do! rethink {
                            withTable Table.Post
                            get post.Id
                            replace post
                            write; withRetryDefault; ignoreResult conn
                        }
                    | None -> ()
                }

                member this.UpdatePriorPermalinks postId webLogId permalinks = backgroundTask {
                    match! this.FindById postId webLogId with
                    | Some _ ->
                        do! rethink {
                            withTable Table.Post
                            get postId
                            update [ nameof Post.Empty.PriorPermalinks, permalinks :> obj ]
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
                        filter (fun row -> row[nameof TagMap.Empty.WebLogId].Eq webLogId :> obj)
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
                    |> verifyWebLog webLogId _.WebLogId <| conn
                
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
                    between [| webLogId :> obj; r.Minval() |] [| webLogId :> obj; r.Maxval() |]
                            [ Index Index.WebLogAndTag ]
                    orderBy (nameof TagMap.Empty.Tag)
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
                    filter (fun row -> row[nameof Theme.Empty.Id].Ne "admin" :> obj)
                    merge withoutTemplateText
                    orderBy (nameof Theme.Empty.Id)
                    result; withRetryDefault conn
                }
                
                member _.Exists themeId = backgroundTask {
                    let! count = rethink<int> {
                        withTable Table.Theme
                        filter (nameof Theme.Empty.Id) themeId
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
                
                member _.FindByIdWithoutText themeId =
                    rethink<Theme list> {
                        withTable Table.Theme
                        getAll [ themeId ]
                        merge withoutTemplateText
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn
                
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
                    without [ nameof ThemeAsset.Empty.Data ]
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
                    without [ nameof ThemeAsset.Empty.Data ]
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
                        |> verifyWebLog<Upload> webLogId _.WebLogId <| conn
                    match upload with
                    | Some up ->
                        do! rethink {
                            withTable Table.Upload
                            get uploadId
                            delete
                            write; withRetryDefault; ignoreResult conn
                        }
                        return Ok (string up.Path)
                    | None -> return Result.Error $"Upload ID {uploadId} not found"
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
                    between [| webLogId :> obj; r.Minval() |] [| webLogId :> obj; r.Maxval() |]
                            [ Index Index.WebLogAndPath ]
                    without [ nameof Upload.Empty.Data ]
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.FindByWebLogWithData webLogId = rethink<Upload> {
                    withTable Table.Upload
                    between [| webLogId :> obj; r.Minval() |] [| webLogId :> obj; r.Maxval() |]
                            [ Index Index.WebLogAndPath ]
                    resultCursor; withRetryCursorDefault; toList conn
                }
                
                member _.Restore uploads = backgroundTask {
                    // Files can be large; we'll do 5 at a time
                    for batch in uploads |> List.chunkBySize 5 do
                        do! rethink {
                            withTable Table.Upload
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
                     let! thePostIds = rethink<{| Id: string |} list> {
                         withTable Table.Post
                         getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                         pluck [ nameof Post.Empty.Id ]
                         result; withRetryOnce conn
                     }
                     if not (List.isEmpty thePostIds) then
                         let postIds = thePostIds |> List.map (fun it -> it.Id :> obj)
                         do! rethink {
                             withTable Table.Comment
                             getAll postIds (nameof Comment.Empty.PostId)
                             delete
                             write; withRetryOnce; ignoreResult conn
                         }
                     // Tag mappings do not have a straightforward webLogId index
                     do! rethink {
                         withTable Table.TagMap
                         between [| webLogId :> obj; r.Minval() |] [| webLogId :> obj; r.Maxval() |]
                                 [ Index Index.WebLogAndTag ]
                         delete
                         write; withRetryOnce; ignoreResult conn
                     }
                     // Uploaded files do not have a straightforward webLogId index
                     do! rethink {
                         withTable Table.Upload
                         between [| webLogId :> obj; r.Minval() |] [| webLogId :> obj; r.Maxval() |]
                                 [ Index Index.WebLogAndPath ]
                         delete
                         write; withRetryOnce; ignoreResult conn
                     }
                     for table in [ Table.Post; Table.Category; Table.Page; Table.WebLogUser ] do
                         do! rethink {
                             withTable table
                             getAll [ webLogId ] (nameof Post.Empty.WebLogId)
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
                        getAll [ url ] (nameof WebLog.Empty.UrlBase)
                        limit 1
                        result; withRetryDefault
                    }
                    |> tryFirst <| conn

                member _.FindById webLogId = rethink<WebLog> {
                    withTable Table.WebLog
                    get webLogId
                    resultOption; withRetryOptionDefault conn
                }
                
                member _.UpdateRedirectRules webLog = rethink {
                    withTable Table.WebLog
                    get webLog.Id
                    update [ nameof WebLog.Empty.RedirectRules, webLog.RedirectRules :> obj ]
                    write; withRetryDefault; ignoreResult conn
                }
                
                member _.UpdateRssOptions webLog = rethink {
                    withTable Table.WebLog
                    get webLog.Id
                    update [ nameof WebLog.Empty.Rss, webLog.Rss :> obj ]
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
                    |> verifyWebLog webLogId _.WebLogId <| conn
                
                member this.Delete userId webLogId = backgroundTask {
                    match! this.FindById userId webLogId with
                    | Some _ ->
                        let! pageCount = rethink<int> {
                            withTable Table.Page
                            getAll [ webLogId ] (nameof Page.Empty.WebLogId)
                            filter (nameof Page.Empty.AuthorId) userId
                            count
                            result; withRetryDefault conn
                        }
                        let! postCount = rethink<int> {
                            withTable Table.Post
                            getAll [ webLogId ] (nameof Post.Empty.WebLogId)
                            filter (nameof Post.Empty.AuthorId) userId
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
                    getAll [ webLogId ] (nameof WebLogUser.Empty.WebLogId)
                    orderByFunc (fun row -> row[nameof WebLogUser.Empty.PreferredName].Downcase())
                    result; withRetryDefault conn
                }
                
                member _.FindNames webLogId userIds = backgroundTask {
                    let! users = rethink<WebLogUser list> {
                        withTable Table.WebLogUser
                        getAll (objList userIds)
                        filter (nameof WebLogUser.Empty.WebLogId) webLogId
                        result; withRetryDefault conn
                    }
                    return users |> List.map (fun u -> { Name = string u.Id; Value = u.DisplayName })
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
                            update [ nameof WebLogUser.Empty.LastSeenOn, Noda.now () :> obj ]
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

            if not (List.contains Table.DbVersion tables) then
                // Version table added in v2-rc2; this will flag that migration to be run
                do! rethink {
                    withTable Table.DbVersion
                    insert {| Id = "v2-rc1" |}
                    write; withRetryOnce; ignoreResult conn
                }
            
            do! ensureIndexes Table.Category   [ nameof Category.Empty.WebLogId ]
            do! ensureIndexes Table.Comment    [ nameof Comment.Empty.PostId ]
            do! ensureIndexes Table.Page       [ nameof Page.Empty.WebLogId; nameof Page.Empty.AuthorId ]
            do! ensureIndexes Table.Post       [ nameof Post.Empty.WebLogId; nameof Post.Empty.AuthorId ]
            do! ensureIndexes Table.TagMap     []
            do! ensureIndexes Table.Upload     []
            do! ensureIndexes Table.WebLog     [ nameof WebLog.Empty.UrlBase ]
            do! ensureIndexes Table.WebLogUser [ nameof WebLogUser.Empty.WebLogId ]
            
            let! version = rethink<{| Id: string |} list> {
                 withTable Table.DbVersion
                 limit 1
                 result; withRetryOnce conn
            }
            do! migrate (List.tryHead version |> Option.map _.Id)
        }
