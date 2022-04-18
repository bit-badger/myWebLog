[<RequireQualifiedAccess>]
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
        fun conn -> task {
            match! f conn with Some it when (prop it) = webLogId -> return Some it | _ -> return None
        }
    
    /// Get the first item from a list, or None if the list is empty
    let tryFirst<'T> (f : IConnection -> Task<'T list>) =
        fun conn -> task {
            let! results = f conn
            return results |> List.tryHead
        }

    
open RethinkDb.Driver.FSharp
open Microsoft.Extensions.Logging

/// Start up checks to ensure the database, tables, and indexes exist
module Startup =
    
    /// Ensure field indexes exist, as well as special indexes for selected tables
    let private ensureIndexes (log : ILogger) conn table fields = task {
        let! indexes = rethink<string list> { withTable table; indexList; result; withRetryOnce conn }
        for field in fields do
            match indexes |> List.contains field with
            | true -> ()
            | false ->
                log.LogInformation($"Creating index {table}.{field}...")
                let! _ = rethink { withTable table; indexCreate field; write; withRetryOnce conn }
                ()
        // Post and page need index by web log ID and permalink
        match [ Table.Page; Table.Post ] |> List.contains table with
        | true ->
            match indexes |> List.contains "permalink" with
            | true -> ()
            | false ->
                log.LogInformation($"Creating index {table}.permalink...")
                let! _ =
                    rethink {
                        withTable table
                        indexCreate "permalink" (fun row -> r.Array(row.G "webLogId", row.G "permalink"))
                        write
                        withRetryOnce conn
                    }
                ()
            // Prior permalinks are searched when a post or page permalink do not match the current URL
            match indexes |> List.contains "priorPermalinks" with
            | true -> ()
            | false ->
                log.LogInformation($"Creating index {table}.priorPermalinks...")
                let! _ =
                    rethink {
                        withTable table
                        indexCreate "priorPermalinks"
                        indexOption Multi
                        write
                        withRetryOnce conn
                    }
                ()
        | false -> ()
        // Users log on with e-mail
        match Table.WebLogUser = table with
        | true ->
            match indexes |> List.contains "logOn" with
            | true -> ()
            | false ->
                log.LogInformation($"Creating index {table}.logOn...")
                let! _ =
                    rethink {
                        withTable table
                        indexCreate "logOn" (fun row -> r.Array(row.G "webLogId", row.G "userName"))
                        write
                        withRetryOnce conn
                    }
                ()
        | false -> ()
    }

    /// Ensure all necessary tables and indexes exist
    let ensureDb (config : DataConfig) (log : ILogger) conn = task {
        
        let! dbs = rethink<string list> { dbList; result; withRetryOnce conn }
        match dbs |> List.contains config.Database with
        | true -> ()
        | false ->
            log.LogInformation($"Creating database {config.Database}...")
            let! _ = rethink { dbCreate config.Database; write; withRetryOnce conn }
            ()
        
        let! tables = rethink<string list> { tableList; result; withRetryOnce conn }
        for tbl in Table.all do
            match tables |> List.contains tbl with
            | true -> ()
            | false ->
                log.LogInformation($"Creating table {tbl}...")
                let! _ = rethink { tableCreate tbl; write; withRetryOnce conn }
                ()

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
    
    /// Count all categories for a web log
    let countAll (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Category
            getAll [ webLogId ] (nameof webLogId)
            count
            result
            withRetryDefault
        }

    /// Count top-level categories for a web log
    let countTopLevel (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Category
            getAll [ webLogId ] (nameof webLogId)
            filter "parentId" None
            count
            result
            withRetryDefault
        }


/// Functions to manipulate pages
module Page =
    
    /// Add a new page
    let add (page : Page) =
        rethink {
            withTable Table.Page
            insert page
            write
            withRetryDefault
            ignoreResult
        }

    /// Count all pages for a web log
    let countAll (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            count
            result
            withRetryDefault
        }

    /// Count listed pages for a web log
    let countListed (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            filter "showInPageList" true
            count
            result
            withRetryDefault
        }

    /// Retrieve all pages for a web log (excludes text, prior permalinks, and revisions)
    let findAll (webLogId : WebLogId) =
        rethink<Page list> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            without [ "text", "priorPermalinks", "revisions" ]
            result
            withRetryDefault
        }

    /// Find a page by its ID (including prior permalinks and revisions)
    let findByFullId (pageId : PageId) webLogId =
        rethink<Page> {
            withTable Table.Page
            get pageId
            resultOption
            withRetryDefault
        }
        |> verifyWebLog webLogId (fun it -> it.webLogId)

    /// Find a page by its ID (excludes prior permalinks and revisions)
    let findById (pageId : PageId) webLogId =
        rethink<Page> {
            withTable Table.Page
            get pageId
            without [ "priorPermalinks", "revisions" ]
            resultOption
            withRetryDefault
        }
        |> verifyWebLog webLogId (fun it -> it.webLogId)

    /// Find a page by its permalink
    let findByPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Page list> {
            withTable Table.Page
            getAll [ r.Array (webLogId, permalink) ] (nameof permalink)
            without [ "priorPermalinks", "revisions" ]
            limit 1
            result
            withRetryDefault
        }
        |> tryFirst
    
    /// Find the current permalink for a page by a prior permalink
    let findCurrentPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Permalink list> {
            withTable Table.Page
            getAll [ permalink ] "priorPermalinks"
            filter [ "webLogId", webLogId :> obj ]
            pluck [ "permalink" ]
            limit 1
            result
            withRetryDefault
        }
        |> tryFirst

    /// Find all pages in the page list for the given web log
    let findListed (webLogId : WebLogId) =
        rethink<Page list> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            filter [ "showInPageList", true :> obj ]
            without [ "text", "priorPermalinks", "revisions" ]
            orderBy "title"
            result
            withRetryDefault
        }

    /// Find a list of pages (displayed in admin area)
    let findPageOfPages (webLogId : WebLogId) pageNbr =
        rethink<Page list> {
            withTable Table.Page
            getAll [ webLogId ] (nameof webLogId)
            without [ "priorPermalinks", "revisions" ]
            orderBy "title"
            skip ((pageNbr - 1) * 25)
            limit 25
            result
            withRetryDefault
        }
    
    /// Update a page
    let update (page : Page) =
        rethink {
            withTable Table.Page
            get page.id
            update [
                "title",           page.title
                "permalink",       page.permalink
                "updatedOn",       page.updatedOn
                "showInPageList",  page.showInPageList
                "text",            page.text
                "priorPermalinks", page.priorPermalinks
                "revisions",       page.revisions
                ]
            write
            withRetryDefault
            ignoreResult
        }

/// Functions to manipulate posts
module Post =
    
    /// Count posts for a web log by their status
    let countByStatus (status : PostStatus) (webLogId : WebLogId) =
        rethink<int> {
            withTable Table.Post
            getAll [ webLogId ] (nameof webLogId)
            filter "status" status
            count
            result
            withRetryDefault
        }

    /// Find a post by its permalink
    let findByPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Post list> {
            withTable Table.Post
            getAll [ r.Array(permalink, webLogId) ] (nameof permalink)
            without [ "priorPermalinks", "revisions" ]
            limit 1
            result
            withRetryDefault
        }
        |> tryFirst

    /// Find the current permalink for a post by a prior permalink
    let findCurrentPermalink (permalink : Permalink) (webLogId : WebLogId) =
        rethink<Permalink list> {
            withTable Table.Post
            getAll [ permalink ] "priorPermalinks"
            filter [ "webLogId", webLogId :> obj ]
            pluck [ "permalink" ]
            limit 1
            result
            withRetryDefault
        }
        |> tryFirst

    /// Find posts to be displayed on a page
    let findPageOfPublishedPosts (webLogId : WebLogId) pageNbr postsPerPage =
        rethink<Post list> {
            withTable Table.Post
            getAll [ webLogId ] (nameof webLogId)
            filter "status" Published
            without [ "priorPermalinks", "revisions" ]
            orderBy "publishedOn"
            skip ((pageNbr - 1) * postsPerPage)
            limit postsPerPage
            result
            withRetryDefault
        }


/// Functions to manipulate web logs
module WebLog =
    
    /// Add a web log
    let add (webLog : WebLog) =
        rethink {
            withTable Table.WebLog
            insert webLog
            write
            withRetryOnce
            ignoreResult
        }
    
    /// Retrieve a web log by the URL base
    let findByHost (url : string) =
        rethink<WebLog list> {
            withTable Table.WebLog
            getAll [ url ] "urlBase"
            limit 1
            result
            withRetryDefault
        }
        |> tryFirst

    /// Retrieve a web log by its ID
    let findById (webLogId : WebLogId) =
        rethink<WebLog> {
            withTable Table.WebLog
            get webLogId
            resultOption
            withRetryDefault
        }
    
    /// Update web log settings
    let updateSettings (webLog : WebLog) =
        rethink {
            withTable Table.WebLog
            get webLog.id
            update [ 
                "name",         webLog.name
                "subtitle",     webLog.subtitle
                "defaultPage",  webLog.defaultPage
                "postsPerPage", webLog.postsPerPage
                "timeZone",     webLog.timeZone
                ]
            write
            withRetryDefault
            ignoreResult
        }


/// Functions to manipulate web log users
module WebLogUser =
    
    /// Add a web log user
    let add (user : WebLogUser) =
        rethink {
            withTable Table.WebLogUser
            insert user
            write
            withRetryDefault
            ignoreResult
        }
    
    /// Find a user by their e-mail address
    let findByEmail (email : string) (webLogId : WebLogId) =
        rethink<WebLogUser list> {
            withTable Table.WebLogUser
            getAll [ r.Array (webLogId, email) ] "logOn"
            limit 1
            result
            withRetryDefault
        }
        |> tryFirst
        