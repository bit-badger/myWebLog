[<RequireQualifiedAccess>]
module MyWebLog.Data

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
    
    /// The web log table
    let WebLog = "WebLog"

    /// The web log user table
    let WebLogUser = "WebLogUser"

    /// A list of all tables
    let all = [ Category; Comment; Page; Post; TagMap; Theme; ThemeAsset; WebLog; WebLogUser ]


/// Functions to assist with retrieving data
[<AutoOpen>]
module Helpers =
    
    open RethinkDb.Driver
    open RethinkDb.Driver.Net
    open System.Threading.Tasks

    /// Shorthand for the ReQL starting point
    let r = RethinkDB.R

    /// Verify that the web log ID matches before returning an item
    let verifyWebLog<'T> webLogId (prop : 'T -> WebLogId) (f : IConnection -> Task<'T option>) =
        fun conn -> backgroundTask {
            match! f conn with Some it when (prop it) = webLogId -> return Some it | _ -> return None
        }
    
    /// Get the first item from a list, or None if the list is empty
    let tryFirst<'T> (f : IConnection -> Task<'T list>) =
        fun conn -> backgroundTask {
            let! results = f conn
            return results |> List.tryHead
        }
    
    /// Cast a strongly-typed list to an object list
    let objList<'T> (objects : 'T list) = objects |> List.map (fun it -> it :> obj)

    
open RethinkDb.Driver.FSharp
open Microsoft.Extensions.Logging

/// Start up checks to ensure the database, tables, and indexes exist
module Startup =
    
    /// Ensure field indexes exist, as well as special indexes for selected tables
    let private ensureIndexes (log : ILogger) conn table fields = backgroundTask {
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
        // Users log on with e-mail
        if Table.WebLogUser = table && not (indexes |> List.contains "logOn") then
            log.LogInformation $"Creating index {table}.logOn..."
            do! rethink {
                withTable table
                indexCreate "logOn" (fun row -> r.Array (row["webLogId"], row["userName"]) :> obj)
                write; withRetryOnce; ignoreResult conn
            }
    }

    /// Ensure all necessary tables and indexes exist
    let ensureDb (config : DataConfig) (log : ILogger) conn = backgroundTask {
        
        let! dbs = rethink<string list> { dbList; result; withRetryOnce conn }
        if not (dbs |> List.contains config.Database) then
            log.LogInformation($"Creating database {config.Database}...")
            do! rethink { dbCreate config.Database; write; withRetryOnce; ignoreResult conn }
        
        let! tables = rethink<string list> { tableList; result; withRetryOnce conn }
        for tbl in Table.all do
            if not (tables |> List.contains tbl) then
                log.LogInformation($"Creating table {tbl}...")
                do! rethink { tableCreate tbl; write; withRetryOnce; ignoreResult conn }

        let makeIdx = ensureIndexes log conn
        do! makeIdx Table.Category   [ "webLogId" ]
        do! makeIdx Table.Comment    [ "postId" ]
        do! makeIdx Table.Page       [ "webLogId"; "authorId" ]
        do! makeIdx Table.Post       [ "webLogId"; "authorId" ]
        do! makeIdx Table.TagMap     []
        do! makeIdx Table.WebLog     [ "urlBase" ]
        do! makeIdx Table.WebLogUser [ "webLogId" ]
    }

/// Functions to manipulate categories
module Category =
    
    open System.Threading.Tasks
    open MyWebLog.ViewModels
    
    /// Add a category
    let add (cat : Category) =
        rethink {
            withTable Table.Category
            insert cat
            write; withRetryDefault; ignoreResult
        }
    
    /// Count all categories for a web log
    let countAll (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Category
            getAll [ webLogId ] (nameof webLogId)
            count
            result; withRetryDefault
        }

    /// Count top-level categories for a web log
    let countTopLevel (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Category
            getAll [ webLogId ] (nameof webLogId)
            filter "parentId" None
            count
            result; withRetryDefault
        }
    
    /// Create a category hierarchy from the given list of categories
    let rec private orderByHierarchy (cats : Category list) parentId slugBase parentNames = seq {
        for cat in cats |> List.filter (fun c -> c.parentId = parentId) do
            let fullSlug = (match slugBase with Some it -> $"{it}/" | None -> "") + cat.slug
            { id          = CategoryId.toString cat.id
              slug        = fullSlug
              name        = cat.name
              description = cat.description
              parentNames = Array.ofList parentNames
              // Post counts are filled on a second pass
              postCount   = 0
            }
            yield! orderByHierarchy cats (Some cat.id) (Some fullSlug) ([ cat.name ] |> List.append parentNames)
    }
    
    /// Find all categories for a web log, sorted alphabetically, arranged in groups, in view model format
    let findAllForView (webLogId : WebLogId) conn = backgroundTask {
        let! cats = rethink<Category list> {
            withTable Table.Category
            getAll [ webLogId ] (nameof webLogId)
            orderByFunc (fun it -> it["name"].Downcase () :> obj)
            result; withRetryDefault conn
        }
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
                let! count = rethink<int> {
                    withTable Table.Post
                    getAll catIds "categoryIds"
                    filter "status" Published
                    distinct
                    count
                    result; withRetryDefault conn
                }
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
    
    /// Find a category by its ID
    let findById (catId : CategoryId) webLogId =
        rethink<Category> {
            withTable Table.Category
            get catId
            resultOption; withRetryOptionDefault
        }
        |> verifyWebLog webLogId (fun c -> c.webLogId)
    
    /// Delete a category, also removing it from any posts to which it is assigned
    let delete catId webLogId conn = backgroundTask {
        match! findById catId webLogId conn with
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
    
    /// Get a category ID -> name dictionary for the given category IDs
    let findNames (webLogId : WebLogId) conn (catIds : CategoryId list) = backgroundTask {
        let! cats = rethink<Category list> {
            withTable Table.Category
            getAll (objList catIds)
            filter "webLogId" webLogId
            result; withRetryDefault conn
        }
        return cats |> List.map (fun c -> { name = CategoryId.toString c.id; value = c.name})
    }
    
    /// Update a category
    let update (cat : Category) =
        rethink {
            withTable Table.Category
            get cat.id
            update [ "name",        cat.name :> obj
                     "slug",        cat.slug
                     "description", cat.description
                     "parentId",    cat.parentId
                   ]
            write; withRetryDefault; ignoreResult
        }


/// Functions to manipulate pages
module Page =
    
    open RethinkDb.Driver.Model
    
    /// Add a new page
    let add (page : Page) =
        rethink {
            withTable Table.Page
            insert page
            write; withRetryDefault; ignoreResult
        }

    /// Count all pages for a web log
    let countAll (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            count
            result; withRetryDefault
        }

    /// Count listed pages for a web log
    let countListed (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            filter "showInPageList" true
            count
            result; withRetryDefault
        }

    /// Delete a page
    let delete (pageId : PageId) (webLogId : WebLogId) conn = backgroundTask {
        let! result =
            rethink<Result> {
                withTable Table.Page
                getAll [ pageId ]
                filter (fun row -> row["webLogId"].Eq webLogId :> obj)
                delete
                write; withRetryDefault conn
            }
        return result.Deleted > 0UL
    }
    
    /// Retrieve all pages for a web log (excludes text, prior permalinks, and revisions)
    let findAll (webLogId : WebLogId) =
        rethink<Page list> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            without [ "text"; "priorPermalinks"; "revisions" ]
            result; withRetryDefault
        }

    /// Find a page by its ID (including prior permalinks and revisions)
    let findByFullId (pageId : PageId) webLogId =
        rethink<Page> {
            withTable Table.Page
            get pageId
            resultOption; withRetryOptionDefault
        }
        |> verifyWebLog webLogId (fun it -> it.webLogId)

    /// Find a page by its ID (excludes prior permalinks and revisions)
    let findById (pageId : PageId) webLogId =
        rethink<Page> {
            withTable Table.Page
            get pageId
            without [ "priorPermalinks"; "revisions" ]
            resultOption; withRetryOptionDefault
        }
        |> verifyWebLog webLogId (fun it -> it.webLogId)

    /// Find a page by its permalink
    let findByPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Page list> {
            withTable Table.Page
            getAll [ r.Array (webLogId, permalink) ] (nameof permalink)
            without [ "priorPermalinks"; "revisions" ]
            limit 1
            result; withRetryDefault
        }
        |> tryFirst
    
    /// Find the current permalink for a page by a prior permalink
    let findCurrentPermalink (permalinks : Permalink list) (webLogId : WebLogId) conn = backgroundTask {
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
    
    /// Find all pages in the page list for the given web log
    let findListed (webLogId : WebLogId) =
        rethink<Page list> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            filter [ "showInPageList", true :> obj ]
            without [ "text"; "priorPermalinks"; "revisions" ]
            orderBy "title"
            result; withRetryDefault
        }

    /// Find a list of pages (displayed in admin area)
    let findPageOfPages (webLogId : WebLogId) pageNbr =
        rethink<Page list> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            without [ "priorPermalinks"; "revisions" ]
            orderByFunc (fun row -> row["title"].Downcase ())
            skip ((pageNbr - 1) * 25)
            limit 25
            result; withRetryDefault
        }
    
    /// Update a page
    let update (page : Page) =
        rethink {
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
            write; withRetryDefault; ignoreResult
        }
    
    /// Update prior permalinks for a page
    let updatePriorPermalinks pageId webLogId (permalinks : Permalink list) conn = backgroundTask {
        match! findById pageId webLogId conn with
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


/// Functions to manipulate posts
module Post =
    
    open System
    open RethinkDb.Driver.Model
    
    /// Add a post
    let add (post : Post) =
        rethink {
            withTable Table.Post
            insert post
            write; withRetryDefault; ignoreResult
        }
    
    /// Count posts for a web log by their status
    let countByStatus (status : PostStatus) (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Post
            getAll [ webLogId ] (nameof webLogId)
            filter "status" status
            count
            result; withRetryDefault
        }

    /// Delete a post
    let delete (postId : PostId) (webLogId : WebLogId) conn = backgroundTask {
        let! result =
            rethink<Result> {
                withTable Table.Post
                getAll [ postId ]
                filter (fun row -> row["webLogId"].Eq webLogId :> obj)
                delete
                write; withRetryDefault conn
            }
        return result.Deleted > 0UL
    }
    
    /// Find a post by its permalink
    let findByPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Post list> {
            withTable Table.Post
            getAll [ r.Array (webLogId, permalink) ] (nameof permalink)
            without [ "priorPermalinks"; "revisions" ]
            limit 1
            result; withRetryDefault
        }
        |> tryFirst
    
    /// Find a post by its ID, including all revisions and prior permalinks
    let findByFullId (postId : PostId) webLogId =
        rethink<Post> {
            withTable Table.Post
            get postId
            resultOption; withRetryOptionDefault
        }
        |> verifyWebLog webLogId (fun p -> p.webLogId)

    /// Find the current permalink for a post by a prior permalink
    let findCurrentPermalink (permalinks : Permalink list) (webLogId : WebLogId) conn = backgroundTask {
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

    /// Find posts to be displayed on a category list page
    let findPageOfCategorizedPosts (webLogId : WebLogId) (catIds : CategoryId list) pageNbr postsPerPage =
        rethink<Post list> {
            withTable Table.Post
            getAll (objList catIds) "categoryIds"
            filter "webLogId" webLogId
            filter "status" Published
            without [ "priorPermalinks"; "revisions" ]
            distinct
            orderByDescending "publishedOn"
            skip ((pageNbr - 1) * postsPerPage)
            limit (postsPerPage + 1)
            result; withRetryDefault
        }
    
    /// Find posts to be displayed on an admin page
    let findPageOfPosts (webLogId : WebLogId) (pageNbr : int) postsPerPage =
        rethink<Post list> {
            withTable Table.Post
            getAll [ webLogId ] (nameof webLogId)
            without [ "priorPermalinks"; "revisions" ]
            orderByFuncDescending (fun row -> row["publishedOn"].Default_ "updatedOn" :> obj)
            skip ((pageNbr - 1) * postsPerPage)
            limit (postsPerPage + 1)
            result; withRetryDefault
        }

    /// Find posts to be displayed on a page
    let findPageOfPublishedPosts (webLogId : WebLogId) pageNbr postsPerPage =
        rethink<Post list> {
            withTable Table.Post
            getAll [ webLogId ] (nameof webLogId)
            filter "status" Published
            without [ "priorPermalinks"; "revisions" ]
            orderByDescending "publishedOn"
            skip ((pageNbr - 1) * postsPerPage)
            limit (postsPerPage + 1)
            result; withRetryDefault
        }
    
    /// Find posts to be displayed on a tag list page
    let findPageOfTaggedPosts (webLogId : WebLogId) (tag : string) pageNbr postsPerPage =
        rethink<Post list> {
            withTable Table.Post
            getAll [ tag ] "tags"
            filter "webLogId" webLogId
            filter "status" Published
            without [ "priorPermalinks"; "revisions" ]
            orderByDescending "publishedOn"
            skip ((pageNbr - 1) * postsPerPage)
            limit (postsPerPage + 1)
            result; withRetryDefault
        }
    
    /// Find the next older and newer post for the given post
    let findSurroundingPosts (webLogId : WebLogId) (publishedOn : DateTime) conn = backgroundTask {
        let! older =
            rethink<Post list> {
                withTable Table.Post
                getAll [ webLogId ] (nameof webLogId)
                filter (fun row -> row["publishedOn"].Lt publishedOn :> obj)
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
                orderBy "publishedOn"
                limit 1
                result; withRetryDefault
            }
            |> tryFirst <| conn
        return older, newer
    }
    
    /// Update a post (all fields are updated)
    let update (post : Post) =
        rethink {
            withTable Table.Post
            get post.id
            replace post
            write; withRetryDefault; ignoreResult
        }

    /// Update prior permalinks for a post
    let updatePriorPermalinks (postId : PostId) webLogId (permalinks : Permalink list) conn = backgroundTask {
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


/// Functions to manipulate tag mappings
module TagMap =
    
    open RethinkDb.Driver.Model
    
    /// Delete a tag mapping
    let delete (tagMapId : TagMapId) (webLogId : WebLogId) conn = backgroundTask {
        let! result =
            rethink<Result> {
                withTable Table.TagMap
                getAll [ tagMapId ]
                filter (fun row -> row["webLogId"].Eq webLogId :> obj)
                delete
                write; withRetryDefault conn
            }
        return result.Deleted > 0UL
    }
    
    /// Find a tag map by its ID
    let findById (tagMapId : TagMapId) webLogId =
        rethink<TagMap> {
            withTable Table.TagMap
            get tagMapId
            resultOption; withRetryOptionDefault
        }
        |> verifyWebLog webLogId (fun tm -> tm.webLogId)
    
    /// Find a tag mapping via URL value for a given web log
    let findByUrlValue (urlValue : string) (webLogId : WebLogId) =
        rethink<TagMap list> {
            withTable Table.TagMap
            getAll [ r.Array (webLogId, urlValue) ] "webLogAndUrl"
            limit 1
            result; withRetryDefault
        }
        |> tryFirst
    
    /// Find all tag mappings for a web log
    let findByWebLogId (webLogId : WebLogId) =
        rethink<TagMap list> {
            withTable Table.TagMap
            between (r.Array (webLogId, r.Minval ())) (r.Array (webLogId, r.Maxval ())) [ Index "webLogAndTag" ]
            orderBy "tag"
            result; withRetryDefault
        }
    
    /// Retrieve mappings for the specified tags
    let findMappingForTags (tags : string list) (webLogId : WebLogId) =
        rethink<TagMap list> {
            withTable Table.TagMap
            getAll (tags |> List.map (fun tag -> r.Array (webLogId, tag) :> obj)) "webLogAndTag"
            result; withRetryDefault
        }
    
    /// Save a tag mapping
    let save (tagMap : TagMap) =
        rethink {
            withTable Table.TagMap
            get tagMap.id
            replace tagMap
            write; withRetryDefault; ignoreResult
        }


/// Functions to manipulate themes
module Theme =
    
    /// Get all themes
    let list =
        rethink<Theme list> {
            withTable Table.Theme
            filter (fun row -> row["id"].Ne "admin" :> obj)
            without [ "templates" ]
            orderBy "id"
            result; withRetryDefault
        }
    
    /// Retrieve a theme by its ID
    let findById (themeId : ThemeId) =
        rethink<Theme> {
            withTable Table.Theme
            get themeId
            resultOption; withRetryOptionDefault
        }
    
    /// Save a theme
    let save (theme : Theme) =
        rethink {
            withTable Table.Theme
            get theme.id
            replace theme
            write; withRetryDefault; ignoreResult
        }


/// Functions to manipulate theme assets
module ThemeAsset =
    
    /// Delete all assets for a theme
    let deleteByTheme themeId =
        let keyPrefix = $"^{ThemeId.toString themeId}/"
        rethink {
            withTable Table.ThemeAsset
            filter (fun row -> row["id"].Match keyPrefix :> obj)
            delete
            write; withRetryDefault; ignoreResult
        }
    
    /// Find a theme asset by its ID
    let findById (assetId : ThemeAssetId) =
        rethink<ThemeAsset> {
            withTable Table.ThemeAsset
            get assetId
            resultOption; withRetryOptionDefault
        }
    
    /// Save a theme assed
    let save (asset : ThemeAsset) =
        rethink {
            withTable Table.ThemeAsset
            get asset.id
            replace asset
            write; withRetryDefault; ignoreResult
        }


/// Functions to manipulate web logs
module WebLog =
    
    /// Add a web log
    let add (webLog : WebLog) = rethink {
        withTable Table.WebLog
        insert webLog
        write; withRetryOnce; ignoreResult
    }
    
    /// Get all web logs
    let all = rethink<WebLog list> {
        withTable Table.WebLog
        result; withRetryDefault
    }
    
    /// Retrieve a web log by the URL base
    let findByHost (url : string) =
        rethink<WebLog list> {
            withTable Table.WebLog
            getAll [ url ] "urlBase"
            limit 1
            result; withRetryDefault
        }
        |> tryFirst

    /// Retrieve a web log by its ID
    let findById (webLogId : WebLogId) =
        rethink<WebLog> {
            withTable Table.WebLog
            get webLogId
            resultOption; withRetryOptionDefault
        }
    
    /// Update RSS options for a web log
    let updateRssOptions (webLog : WebLog) =
        rethink {
            withTable Table.WebLog
            get webLog.id
            update [ "rss", webLog.rss :> obj ]
            write; withRetryDefault; ignoreResult
        }
    
    /// Update web log settings (from settings page)
    let updateSettings (webLog : WebLog) =
        rethink {
            withTable Table.WebLog
            get webLog.id
            update [
                "name",         webLog.name :> obj
                "subtitle",     webLog.subtitle
                "defaultPage",  webLog.defaultPage
                "postsPerPage", webLog.postsPerPage
                "timeZone",     webLog.timeZone
                "themePath",    webLog.themePath
                "autoHtmx",     webLog.autoHtmx
            ]
            write; withRetryDefault; ignoreResult
        }


/// Functions to manipulate web log users
module WebLogUser =
    
    /// Add a web log user
    let add (user : WebLogUser) =
        rethink {
            withTable Table.WebLogUser
            insert user
            write; withRetryDefault; ignoreResult
        }
    
    /// Find a user by their e-mail address
    let findByEmail (email : string) (webLogId : WebLogId) =
        rethink<WebLogUser list> {
            withTable Table.WebLogUser
            getAll [ r.Array (webLogId, email) ] "logOn"
            limit 1
            result; withRetryDefault
        }
        |> tryFirst
    
    /// Find a user by their ID
    let findById (userId : WebLogUserId) =
        rethink<WebLogUser> {
            withTable Table.WebLogUser
            get userId
            resultOption; withRetryOptionDefault
        }
    
    /// Get a user ID -> name dictionary for the given user IDs
    let findNames (webLogId : WebLogId) conn (userIds : WebLogUserId list) = backgroundTask {
        let! users = rethink<WebLogUser list> {
            withTable Table.WebLogUser
            getAll (objList userIds)
            filter "webLogId" webLogId
            result; withRetryDefault conn
        }
        return users |> List.map (fun u -> { name = WebLogUserId.toString u.id; value = WebLogUser.displayName u })
    }
    
    /// Update a user
    let update (user : WebLogUser) =
        rethink {
            withTable Table.WebLogUser
            get user.id
            update [
                "firstName",     user.firstName :> obj
                "lastName",      user.lastName
                "preferredName", user.preferredName
                "passwordHash",  user.passwordHash
                "salt",          user.salt
                ]
            write; withRetryDefault; ignoreResult
        }
    