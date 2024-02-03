module SQLiteDataTests

open System.IO
open BitBadger.Documents.Sqlite
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open MyWebLog
open MyWebLog.Converters
open MyWebLog.Data
open Newtonsoft.Json

/// JSON serializer 
let ser = Json.configure (JsonSerializer.CreateDefault())

/// The test database name
let dbName =
    RethinkDbDataTests.env "SQLITE_DB" "test-db.db"

/// Create a SQLiteData instance for testing
let mkData () =
    Configuration.useConnectionString $"Data Source=./{dbName}"
    let conn = Configuration.dbConn ()
    SQLiteData(conn, NullLogger<SQLiteData>(), ser) :> IData

// /// Create a SQLiteData instance for testing
// let mkTraceData () =
//     Sqlite.Configuration.useConnectionString $"Data Source=./{dbName}"
//     let conn = Sqlite.Configuration.dbConn ()
//     let myLogger = 
//         LoggerFactory
//             .Create(fun builder -> 
//                 builder
//                     .AddSimpleConsole()
//                     .SetMinimumLevel(LogLevel.Trace) 
//                     |> ignore)
//             .CreateLogger<SQLiteData>()
//     SQLiteData(conn, myLogger, ser) :> IData

/// Dispose the connection associated with the SQLiteData instance
let dispose (data: IData) =
    (data :?> SQLiteData).Conn.Dispose()

/// Create a fresh environment from the root backup
let freshEnvironment (data: IData option) = task {
    let! env = task {
        match data with
        | Some d ->
            return d
        | None ->
            let d = mkData ()
            // Thank you, kind Internet stranger... https://stackoverflow.com/a/548297
            do! (d :?> SQLiteData).Conn.customNonQuery
                    "PRAGMA writable_schema = 1;
                     DELETE FROM sqlite_master WHERE type IN ('table', 'index');
                     PRAGMA writable_schema = 0;
                     VACUUM" []
            return d
        }
    do! env.StartUp()
    // This exercises Restore for all implementations; all tests are dependent on it working as expected
    do! Maintenance.Backup.restoreBackup "root-weblog.json" None false false env
    return env
}

/// Set up the environment for the SQLite tests
let environmentSetUp = testList "Environment" [
    testTask "creating database" {
        let data = mkData ()
        try do! freshEnvironment (Some data)
        finally dispose data
    }
]

/// Integration tests for the Category implementation in SQLite
let categoryTests = testList "Category" [
    testTask "Add succeeds" {
        let data = mkData ()
        try do! CategoryDataTests.``Add succeeds`` data
        finally dispose data
    }
    testList "CountAll" [
        testTask "succeeds when categories exist" {
            let data = mkData ()
            try do! CategoryDataTests.``CountAll succeeds when categories exist`` data
            finally dispose data
        }
        testTask "succeeds when categories do not exist" {
            let data = mkData ()
            try do! CategoryDataTests.``CountAll succeeds when categories do not exist`` data
            finally dispose data
        }
    ]
    testList "CountTopLevel" [
        testTask "succeeds when top-level categories exist" {
            let data = mkData ()
            try do! CategoryDataTests.``CountTopLevel succeeds when top-level categories exist`` data
            finally dispose data
        }
        testTask "succeeds when no top-level categories exist" {
            let data = mkData ()
            try do! CategoryDataTests.``CountTopLevel succeeds when no top-level categories exist`` data
            finally dispose data
        }
    ]
    testTask "FindAllForView succeeds" {
        let data = mkData ()
        try do! CategoryDataTests.``FindAllForView succeeds`` data
        finally dispose data
    }
    testList "FindById" [
        testTask "succeeds when a category is found" {
            let data = mkData ()
            try do! CategoryDataTests.``FindById succeeds when a category is found`` data
            finally dispose data
        }
        testTask "succeeds when a category is not found" {
            let data = mkData ()
            try do! CategoryDataTests.``FindById succeeds when a category is not found`` data
            finally dispose data
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when categories exist" {
            let data = mkData ()
            try do! CategoryDataTests.``FindByWebLog succeeds when categories exist`` data
            finally dispose data
        }
        testTask "succeeds when no categories exist" {
            let data = mkData ()
            try do! CategoryDataTests.``FindByWebLog succeeds when no categories exist`` data
            finally dispose data
        }
    ]
    testTask "Update succeeds" {
        let data = mkData ()
        try do! CategoryDataTests.``Update succeeds`` data
        finally dispose data
    }
    testList "Delete" [
        testTask "succeeds when the category is deleted (no posts)" {
            let data = mkData ()
            try do! CategoryDataTests.``Delete succeeds when the category is deleted (no posts)`` data
            finally dispose data
        }
        testTask "succeeds when the category does not exist" {
            let data = mkData ()
            try do! CategoryDataTests.``Delete succeeds when the category does not exist`` data
            finally dispose data
        }
        testTask "succeeds when reassigning parent category to None" {
            let data = mkData ()
            try do! CategoryDataTests.``Delete succeeds when reassigning parent category to None`` data
            finally dispose data
        }
        testTask "succeeds when reassigning parent category to Some" {
            let data = mkData ()
            try do! CategoryDataTests.``Delete succeeds when reassigning parent category to Some`` data
            finally dispose data
        }
        testTask "succeeds and removes category from posts" {
            let data = mkData ()
            try do! CategoryDataTests.``Delete succeeds and removes category from posts`` data
            finally dispose data
        }
    ]
]

/// Integration tests for the Page implementation in SQLite
let pageTests = testList "Page" [
    testTask "Add succeeds" {
        let data = mkData ()
        try do! PageDataTests.``Add succeeds`` data
        finally dispose data
    }
    testTask "All succeeds" {
        let data = mkData ()
        try do! PageDataTests.``All succeeds`` data
        finally dispose data
    }
    testTask "CountAll succeeds" {
        let data = mkData ()
        try do! PageDataTests.``CountAll succeeds`` data
        finally dispose data
    }
    testTask "CountListed succeeds" {
        let data = mkData ()
        try do! PageDataTests.``CountListed succeeds`` data
        finally dispose data
    }
    testList "FindById" [
        testTask "succeeds when a page is found" {
            let data = mkData ()
            try do! PageDataTests.``FindById succeeds when a page is found`` data
            finally dispose data
        }
        testTask "succeeds when a page is not found (incorrect weblog)" {
            let data = mkData ()
            try do! PageDataTests.``FindById succeeds when a page is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a page is not found (bad page ID)" {
            let data = mkData ()
            try do! PageDataTests.``FindById succeeds when a page is not found (bad page ID)`` data
            finally dispose data
        }
    ]
    testList "FindByPermalink" [
        testTask "succeeds when a page is found" {
            let data = mkData ()
            try do! PageDataTests.``FindByPermalink succeeds when a page is found`` data
            finally dispose data
        }
        testTask "succeeds when a page is not found (incorrect weblog)" {
            let data = mkData ()
            try do! PageDataTests.``FindByPermalink succeeds when a page is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a page is not found (no such permalink)" {
            let data = mkData ()
            try do! PageDataTests.``FindByPermalink succeeds when a page is not found (no such permalink)`` data
            finally dispose data
        }
    ]
    testList "FindCurrentPermalink" [
        testTask "succeeds when a page is found" {
            let data = mkData ()
            try do! PageDataTests.``FindCurrentPermalink succeeds when a page is found`` data
            finally dispose data
        }
        testTask "succeeds when a page is not found" {
            let data = mkData ()
            try do! PageDataTests.``FindCurrentPermalink succeeds when a page is not found`` data
            finally dispose data
        }
    ]
    testList "FindFullById" [
        testTask "succeeds when a page is found" {
            let data = mkData ()
            try do! PageDataTests.``FindFullById succeeds when a page is found`` data
            finally dispose data
        }
        testTask "succeeds when a page is not found" {
            let data = mkData ()
            try do! PageDataTests.``FindFullById succeeds when a page is not found`` data
            finally dispose data
        }
    ]
    testList "FindFullByWebLog" [
        testTask "succeeds when pages are found" {
            let data = mkData ()
            try do! PageDataTests.``FindFullByWebLog succeeds when pages are found`` data
            finally dispose data
        }
        testTask "succeeds when a pages are not found" {
            let data = mkData ()
            try do! PageDataTests.``FindFullByWebLog succeeds when pages are not found`` data
            finally dispose data
        }
    ]
    testList "FindListed" [
        testTask "succeeds when pages are found" {
            let data = mkData ()
            try do! PageDataTests.``FindListed succeeds when pages are found`` data
            finally dispose data
        }
        testTask "succeeds when a pages are not found" {
            let data = mkData ()
            try do! PageDataTests.``FindListed succeeds when pages are not found`` data
            finally dispose data
        }
    ]
    testList "FindPageOfPages" [
        testTask "succeeds when pages are found" {
            let data = mkData ()
            try do! PageDataTests.``FindPageOfPages succeeds when pages are found`` data
            finally dispose data
        }
        testTask "succeeds when a pages are not found" {
            let data = mkData ()
            try do! PageDataTests.``FindPageOfPages succeeds when pages are not found`` data
            finally dispose data
        }
    ]
    testList "Update" [
        testTask "succeeds when the page exists" {
            let data = mkData ()
            try do! PageDataTests.``Update succeeds when the page exists`` data
            finally dispose data
        }
        testTask "succeeds when the page does not exist" {
            let data = mkData ()
            try do! PageDataTests.``Update succeeds when the page does not exist`` data
            finally dispose data
        }
    ]
    testList "UpdatePriorPermalinks" [
        testTask "succeeds when the page exists" {
            let data = mkData ()
            try do! PageDataTests.``UpdatePriorPermalinks succeeds when the page exists`` data
            finally dispose data
        }
        testTask "succeeds when the page does not exist" {
            let data = mkData ()
            try do! PageDataTests.``UpdatePriorPermalinks succeeds when the page does not exist`` data
            finally dispose data
        }
    ]
    testList "Delete" [
        testTask "succeeds when a page is deleted" {
            let data = mkData ()
            try do! PageDataTests.``Delete succeeds when a page is deleted`` data
            finally dispose data
        }
        testTask "succeeds when a page is not deleted" {
            let data = mkData ()
            try do! PageDataTests.``Delete succeeds when a page is not deleted`` data
            finally dispose data
        }
    ]
]

/// Integration tests for the Post implementation in SQLite
let postTests = testList "Post" [
    testTask "Add succeeds" {
        // We'll need the root website categories restored for these tests
        let! data = freshEnvironment None
        try do! PostDataTests.``Add succeeds`` data
        finally dispose data
    }
    testTask "CountPostsByStatus succeeds" {
        let data = mkData ()
        try do! PostDataTests.``CountByStatus succeeds`` data
        finally dispose data
    }
    testList "FindById" [
        testTask "succeeds when a post is found" {
            let data = mkData ()
            try do! PostDataTests.``FindById succeeds when a post is found`` data
            finally dispose data
        }
        testTask "succeeds when a post is not found (incorrect weblog)" {
            let data = mkData ()
            try do! PostDataTests.``FindById succeeds when a post is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a post is not found (bad post ID)" {
            let data = mkData ()
            try do! PostDataTests.``FindById succeeds when a post is not found (bad post ID)`` data
            finally dispose data
        }
    ]
    testList "FindByPermalink" [
        testTask "succeeds when a post is found" {
            let data = mkData ()
            try do! PostDataTests.``FindByPermalink succeeds when a post is found`` data
            finally dispose data
        }
        testTask "succeeds when a post is not found (incorrect weblog)" {
            let data = mkData ()
            try do! PostDataTests.``FindByPermalink succeeds when a post is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a post is not found (no such permalink)" {
            let data = mkData ()
            try do! PostDataTests.``FindByPermalink succeeds when a post is not found (no such permalink)`` data
            finally dispose data
        }
    ]
    testList "FindCurrentPermalink" [
        testTask "succeeds when a post is found" {
            let data = mkData ()
            try do! PostDataTests.``FindCurrentPermalink succeeds when a post is found`` data
            finally dispose data
        }
        testTask "succeeds when a post is not found" {
            let data = mkData ()
            try do! PostDataTests.``FindCurrentPermalink succeeds when a post is not found`` data
            finally dispose data
        }
    ]
    testList "FindFullById" [
        testTask "succeeds when a post is found" {
            let data = mkData ()
            try do! PostDataTests.``FindFullById succeeds when a post is found`` data
            finally dispose data
        }
        testTask "succeeds when a post is not found" {
            let data = mkData ()
            try do! PostDataTests.``FindFullById succeeds when a post is not found`` data
            finally dispose data
        }
    ]
    testList "FindFullByWebLog" [
        testTask "succeeds when posts are found" {
            let data = mkData ()
            try do! PostDataTests.``FindFullByWebLog succeeds when posts are found`` data
            finally dispose data
        }
        testTask "succeeds when a posts are not found" {
            let data = mkData ()
            try do! PostDataTests.``FindFullByWebLog succeeds when posts are not found`` data
            finally dispose data
        }
    ]
    testList "FindPageOfCategorizedPosts" [
        testTask "succeeds when posts are found" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfCategorizedPosts succeeds when posts are found`` data
            finally dispose data
        }
        testTask "succeeds when finding a too-high page number" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfCategorizedPosts succeeds when finding a too-high page number`` data
            finally dispose data
        }
        testTask "succeeds when a category has no posts" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfCategorizedPosts succeeds when a category has no posts`` data
            finally dispose data
        }
    ]
    testList "FindPageOfPosts" [
        testTask "succeeds when posts are found" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfPosts succeeds when posts are found`` data
            finally dispose data
        }
        testTask "succeeds when finding a too-high page number" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfPosts succeeds when finding a too-high page number`` data
            finally dispose data
        }
        testTask "succeeds when there are no posts" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfPosts succeeds when there are no posts`` data
            finally dispose data
        }
    ]
    testList "FindPageOfPublishedPosts" [
        testTask "succeeds when posts are found" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfPublishedPosts succeeds when posts are found`` data
            finally dispose data
        }
        testTask "succeeds when finding a too-high page number" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfPublishedPosts succeeds when finding a too-high page number`` data
            finally dispose data
        }
        testTask "succeeds when there are no posts" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfPublishedPosts succeeds when there are no posts`` data
            finally dispose data
        }
    ]
    testList "FindPageOfTaggedPosts" [
        testTask "succeeds when posts are found" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfTaggedPosts succeeds when posts are found`` data
            finally dispose data
        }
        testTask "succeeds when posts are found (excluding drafts)" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfTaggedPosts succeeds when posts are found (excluding drafts)`` data
            finally dispose data
        }
        testTask "succeeds when finding a too-high page number" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfTaggedPosts succeeds when finding a too-high page number`` data
            finally dispose data
        }
        testTask "succeeds when there are no posts" {
            let data = mkData ()
            try do! PostDataTests.``FindPageOfTaggedPosts succeeds when there are no posts`` data
            finally dispose data
        }
    ]
    testList "FindSurroundingPosts" [
        testTask "succeeds when there is no next newer post" {
            let data = mkData ()
            try do! PostDataTests.``FindSurroundingPosts succeeds when there is no next newer post`` data
            finally dispose data
        }
        testTask "succeeds when there is no next older post" {
            let data = mkData ()
            try do! PostDataTests.``FindSurroundingPosts succeeds when there is no next older post`` data
            finally dispose data
        }
        testTask "succeeds when older and newer exist" {
            let data = mkData ()
            try do! PostDataTests.``FindSurroundingPosts succeeds when older and newer exist`` data
            finally dispose data
        }
    ]
    testList "Update" [
        testTask "succeeds when the post exists" {
            let data = mkData ()
            try do! PostDataTests.``Update succeeds when the post exists`` data
            finally dispose data
        }
        testTask "succeeds when the post does not exist" {
            let data = mkData ()
            try do! PostDataTests.``Update succeeds when the post does not exist`` data
            finally dispose data
        }
    ]
    testList "UpdatePriorPermalinks" [
        testTask "succeeds when the post exists" {
            let data = mkData ()
            try do! PostDataTests.``UpdatePriorPermalinks succeeds when the post exists`` data
            finally dispose data
        }
        testTask "succeeds when the post does not exist" {
            let data = mkData ()
            try do! PostDataTests.``UpdatePriorPermalinks succeeds when the post does not exist`` data
            finally dispose data
        }
    ]
    testList "Delete" [
        testTask "succeeds when a post is deleted" {
            let data = mkData ()
            try do! PostDataTests.``Delete succeeds when a post is deleted`` data
            finally dispose data
        }
        testTask "succeeds when a post is not deleted" {
            let data = mkData ()
            try do! PostDataTests.``Delete succeeds when a post is not deleted`` data
            finally dispose data
        }
    ]
]

let tagMapTests = testList "TagMap" [
    testList "FindById" [
        testTask "succeeds when a tag mapping is found" {
            let data = mkData ()
            try do! TagMapDataTests.``FindById succeeds when a tag mapping is found`` data
            finally dispose data
        }
        testTask "succeeds when a tag mapping is not found (incorrect weblog)" {
            let data = mkData ()
            try do! TagMapDataTests.``FindById succeeds when a tag mapping is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a tag mapping is not found (bad tag map ID)" {
            let data = mkData ()
            try do! TagMapDataTests.``FindById succeeds when a tag mapping is not found (bad tag map ID)`` data
            finally dispose data
        }
    ]
    testList "FindByUrlValue" [
        testTask "succeeds when a tag mapping is found" {
            let data = mkData ()
            try do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is found`` data
            finally dispose data
        }
        testTask "succeeds when a tag mapping is not found (incorrect weblog)" {
            let data = mkData ()
            try do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a tag mapping is not found (no such value)" {
            let data = mkData ()
            try do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is not found (no such value)`` data
            finally dispose data
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when tag mappings are found" {
            let data = mkData ()
            try do! TagMapDataTests.``FindByWebLog succeeds when tag mappings are found`` data
            finally dispose data
        }
        testTask "succeeds when no tag mappings are found" {
            let data = mkData ()
            try do! TagMapDataTests.``FindByWebLog succeeds when no tag mappings are found`` data
            finally dispose data
        }
    ]
    testList "FindMappingForTags" [
        testTask "succeeds when mappings exist" {
            let data = mkData ()
            try do! TagMapDataTests.``FindMappingForTags succeeds when mappings exist`` data
            finally dispose data
        }
        testTask "succeeds when no mappings exist" {
            let data = mkData ()
            try do! TagMapDataTests.``FindMappingForTags succeeds when no mappings exist`` data
            finally dispose data
        }
    ]
    testList "Save" [
        testTask "succeeds when adding a tag mapping" {
            let data = mkData ()
            try do! TagMapDataTests.``Save succeeds when adding a tag mapping`` data
            finally dispose data
        }
        testTask "succeeds when updating a tag mapping" {
            let data = mkData ()
            try do! TagMapDataTests.``Save succeeds when updating a tag mapping`` data
            finally dispose data
        }
    ]
    testList "Delete" [
        testTask "succeeds when a tag mapping is deleted" {
            let data = mkData ()
            try do! TagMapDataTests.``Delete succeeds when a tag mapping is deleted`` data
            finally dispose data
        }
        testTask "succeeds when a tag mapping is not deleted" {
            let data = mkData ()
            try do! TagMapDataTests.``Delete succeeds when a tag mapping is not deleted`` data
            finally dispose data
        }
    ]
]

let themeTests = testList "Theme" [
    testTask "All succeeds" {
        let data = mkData ()
        try do! ThemeDataTests.``All succeeds`` data
        finally dispose data
    }
    testList "Exists" [
        testTask "succeeds when the theme exists" {
            let data = mkData ()
            try do! ThemeDataTests.``Exists succeeds when the theme exists`` data
            finally dispose data
        }
        testTask "succeeds when the theme does not exist" {
            let data = mkData ()
            try do! ThemeDataTests.``Exists succeeds when the theme does not exist`` data
            finally dispose data
        }
    ]
    testList "FindById" [
        testTask "succeeds when the theme exists" {
            let data = mkData ()
            try do! ThemeDataTests.``FindById succeeds when the theme exists`` data
            finally dispose data
        }
        testTask "succeeds when the theme does not exist" {
            let data = mkData ()
            try do! ThemeDataTests.``FindById succeeds when the theme does not exist`` data
            finally dispose data
        }
    ]
    testList "FindByIdWithoutText" [
        testTask "succeeds when the theme exists" {
            let data = mkData ()
            try do! ThemeDataTests.``FindByIdWithoutText succeeds when the theme exists`` data
            finally dispose data
        }
        testTask "succeeds when the theme does not exist" {
            let data = mkData ()
            try do! ThemeDataTests.``FindByIdWithoutText succeeds when the theme does not exist`` data
            finally dispose data
        }
    ]
    testList "Save" [
        testTask "succeeds when adding a theme" {
            let data = mkData ()
            try do! ThemeDataTests.``Save succeeds when adding a theme`` data
            finally dispose data
        }
        testTask "succeeds when updating a theme" {
            let data = mkData ()
            try do! ThemeDataTests.``Save succeeds when updating a theme`` data
            finally dispose data
        }
    ]
    testList "Delete" [
        testTask "succeeds when a theme is deleted" {
            let data = mkData ()
            try do! ThemeDataTests.``Delete succeeds when a theme is deleted`` data
            finally dispose data
        }
        testTask "succeeds when a theme is not deleted" {
            let data = mkData ()
            try do! ThemeDataTests.``Delete succeeds when a theme is not deleted`` data
            finally dispose data
        }
    ]
]

/// Delete the SQLite database
let environmentCleanUp = test "Clean Up" {
    File.Delete dbName
    Expect.isFalse (File.Exists dbName) "The test SQLite database should have been deleted"
} 

/// All SQLite data tests
let all =
    testList "SQLiteData"
        [ environmentSetUp
          categoryTests
          pageTests
          postTests
          tagMapTests
          themeTests
          environmentCleanUp ]
    |> testSequenced
