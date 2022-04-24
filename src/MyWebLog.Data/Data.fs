﻿[<RequireQualifiedAccess>]
module MyWebLog.Data

/// Table names
[<RequireQualifiedAccess>]
module private Table =
    
    /// The category table
    let Category = "Category"

    /// The comment table
    let Comment = "Comment"

    /// The page table
    let Page = "Page"
    
    /// The post table
    let Post = "Post"
    
    /// The web log table
    let WebLog = "WebLog"

    /// The web log user table
    let WebLogUser = "WebLogUser"

    /// A list of all tables
    let all = [ Category; Comment; Page; Post; WebLog; WebLogUser ]


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

    
open RethinkDb.Driver.FSharp
open Microsoft.Extensions.Logging

/// Start up checks to ensure the database, tables, and indexes exist
module Startup =
    
    /// Ensure field indexes exist, as well as special indexes for selected tables
    let private ensureIndexes (log : ILogger) conn table fields = backgroundTask {
        let! indexes = rethink<string list> { withTable table; indexList; result; withRetryOnce conn }
        for field in fields do
            if not (indexes |> List.contains field) then
                log.LogInformation($"Creating index {table}.{field}...")
                do! rethink { withTable table; indexCreate field; write; withRetryOnce; ignoreResult conn }
        // Post and page need index by web log ID and permalink
        if [ Table.Page; Table.Post ] |> List.contains table then
            if not (indexes |> List.contains "permalink") then
                log.LogInformation($"Creating index {table}.permalink...")
                do! rethink {
                    withTable table
                    indexCreate "permalink" (fun row -> r.Array(row.G "webLogId", row.G "permalink") :> obj)
                    write; withRetryOnce; ignoreResult conn
                }
            // Prior permalinks are searched when a post or page permalink do not match the current URL
            if not (indexes |> List.contains "priorPermalinks") then
                log.LogInformation($"Creating index {table}.priorPermalinks...")
                do! rethink {
                    withTable table
                    indexCreate "priorPermalinks" [ Multi ]
                    write; withRetryOnce; ignoreResult conn
                }
        // Users log on with e-mail
        if Table.WebLogUser = table && not (indexes |> List.contains "logOn") then
            log.LogInformation($"Creating index {table}.logOn...")
            do! rethink {
                withTable table
                indexCreate "logOn" (fun row -> r.Array(row.G "webLogId", row.G "userName") :> obj)
                write; withRetryOnce; ignoreResult conn
            }
    }

    /// Ensure all necessary tables and indexes exist
    let ensureDb (config : DataConfig) (log : ILogger) conn = task {
        
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
        do! makeIdx Table.WebLog     [ "urlBase" ]
        do! makeIdx Table.WebLogUser [ "webLogId" ]
    }

/// Functions to manipulate categories
module Category =
    
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
            }
            yield! orderByHierarchy cats (Some cat.id) (Some fullSlug) ([ cat.name ] |> List.append parentNames)
    }
    
    /// Find all categories for a web log, sorted alphabetically, arranged in groups, in view model format
    let findAllForView (webLogId : WebLogId) conn = backgroundTask {
        let! cats = rethink<Category list> {
            withTable Table.Category
            getAll [ webLogId ] (nameof webLogId)
            orderBy "name"
            result; withRetryDefault conn
        }
        return orderByHierarchy cats None None [] |> Array.ofSeq
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
                filter (fun row -> row.G("categoryIds").Contains catId :> obj)
                update (fun row -> r.HashMap("categoryIds", r.Array(row.G("categoryIds")).Remove catId) :> obj)
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
    let findNames (catIds : CategoryId list) (webLogId : WebLogId) conn = backgroundTask {
        let! cats = rethink<Category list> {
            withTable Table.Category
            getAll (catIds |> List.map (fun it -> it :> obj))
            filter "webLogId" webLogId
            result; withRetryDefault conn
        }
        return cats |> List.map (fun c -> CategoryId.toString c.id, c.name) |> dict
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
    let findCurrentPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Permalink list> {
            withTable Table.Page
            getAll [ permalink ] "priorPermalinks"
            filter "webLogId" webLogId
            pluck [ "permalink" ]
            limit 1
            result; withRetryDefault
        }
        |> tryFirst

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
            orderBy "title"
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
                "text",            page.text
                "priorPermalinks", page.priorPermalinks
                "revisions",       page.revisions
                ]
            write; withRetryDefault; ignoreResult
        }

/// Functions to manipulate posts
module Post =
    
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

    /// Find a post by its permalink
    let findByPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Post list> {
            withTable Table.Post
            getAll [ r.Array(permalink, webLogId) ] (nameof permalink)
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
    let findCurrentPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Permalink list> {
            withTable Table.Post
            getAll [ permalink ] "priorPermalinks"
            filter "webLogId" webLogId
            pluck [ "permalink" ]
            limit 1
            result; withRetryDefault
        }
        |> tryFirst

    /// Find posts to be displayed on an admin page
    let findPageOfPosts (webLogId : WebLogId) pageNbr postsPerPage =
        rethink<Post list> {
            withTable Table.Post
            getAll [ webLogId ] (nameof webLogId)
            without [ "priorPermalinks"; "revisions" ]
            orderByFuncDescending (fun row -> row.G("publishedOn").Default_("updatedOn") :> obj)
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
    
    /// Update a post (all fields are updated)
    let update (post : Post) =
        rethink {
            withTable Table.Post
            get post.id
            replace post
            write; withRetryDefault; ignoreResult
        }


/// Functions to manipulate web logs
module WebLog =
    
    /// Add a web log
    let add (webLog : WebLog) =
        rethink {
            withTable Table.WebLog
            insert webLog
            write; withRetryOnce; ignoreResult
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
    
    /// Update web log settings
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
    
    /// Get a user ID -> name dictionary for the given user IDs
    let findNames (userIds : WebLogUserId list) (webLogId : WebLogId) conn = backgroundTask {
        let! users = rethink<WebLogUser list> {
            withTable Table.WebLogUser
            getAll (userIds |> List.map (fun it -> it :> obj))
            filter "webLogId" webLogId
            result; withRetryDefault conn
        }
        return users |> List.map (fun u -> WebLogUserId.toString u.id, WebLogUser.displayName u) |> dict
    }