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
let private ser = Json.configure (JsonSerializer.CreateDefault())

/// The test database name
let private dbName =
    RethinkDbDataTests.env "SQLITE_DB" "test-db.db"

/// Create a SQLiteData instance for testing
let private mkData () =
    Configuration.useConnectionString $"Data Source=./{dbName}"
    let conn = Configuration.dbConn ()
    SQLiteData(conn, NullLogger<SQLiteData>(), ser) :> IData

// /// Create a SQLiteData instance for testing
// let private mkTraceData () =
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
let private dispose (data: IData) =
    (data :?> SQLiteData).Conn.Dispose()

/// Create a fresh environment from the root backup
let private freshEnvironment (data: IData option) = task {
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
let private environmentSetUp = testList "Environment" [
    testTask "creating database" {
        let data = mkData ()
        try do! freshEnvironment (Some data)
        finally dispose data
    }
]

/// Integration tests for the Category implementation in SQLite
let private categoryTests = testList "Category" [
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
let private pageTests = testList "Page" [
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
            try
                do! PageDataTests.``Delete succeeds when a page is deleted`` data
                let! revisions =
                    (data :?> SQLiteData).Conn.customScalar
                        "SELECT COUNT(*) AS it FROM page_revision WHERE page_id = @id"
                        [ idParam PageDataTests.coolPageId ]
                        toCount
                Expect.equal revisions 0L "All revisions for the page should have been deleted"
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
let private postTests = testList "Post" [
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
            try
                do! PostDataTests.``Delete succeeds when a post is deleted`` data
                let! revisions =
                    (data :?> SQLiteData).Conn.customScalar
                        "SELECT COUNT(*) AS it FROM post_revision WHERE post_id = @id"
                        [ idParam PostDataTests.episode2 ]
                        toCount
                Expect.equal revisions 0L "All revisions for the post should have been deleted"
            finally dispose data
        }
        testTask "succeeds when a post is not deleted" {
            let data = mkData ()
            try do! PostDataTests.``Delete succeeds when a post is not deleted`` data
            finally dispose data
        }
    ]
]

let private tagMapTests = testList "TagMap" [
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

let private themeTests = testList "Theme" [
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

let private themeAssetTests = testList "ThemeAsset" [
    testList "Save" [
        testTask "succeeds when adding an asset" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``Save succeeds when adding an asset`` data
            finally dispose data
        }
        testTask "succeeds when updating an asset" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``Save succeeds when updating an asset`` data
            finally dispose data
        }
    ]
    testTask "All succeeds" {
        let data = mkData ()
        try do! ThemeDataTests.Asset.``All succeeds`` data
        finally dispose data
    }
    testList "FindById" [
        testTask "succeeds when an asset is found" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``FindById succeeds when an asset is found`` data
            finally dispose data
        }
        testTask "succeeds when an asset is not found" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``FindById succeeds when an asset is not found`` data
            finally dispose data
        }
    ]
    testList "FindByTheme" [
        testTask "succeeds when assets exist" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``FindByTheme succeeds when assets exist`` data
            finally dispose data
        }
        testTask "succeeds when assets do not exist" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``FindByTheme succeeds when assets do not exist`` data
            finally dispose data
        }
    ]
    testList "FindByThemeWithData" [
        testTask "succeeds when assets exist" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``FindByThemeWithData succeeds when assets exist`` data
            finally dispose data
        }
        testTask "succeeds when assets do not exist" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``FindByThemeWithData succeeds when assets do not exist`` data
            finally dispose data
        }
    ]
    testList "DeleteByTheme" [
        testTask "succeeds when assets are deleted" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``DeleteByTheme succeeds when assets are deleted`` data
            finally dispose data
        }
        testTask "succeeds when no assets are deleted" {
            let data = mkData ()
            try do! ThemeDataTests.Asset.``DeleteByTheme succeeds when no assets are deleted`` data
            finally dispose data
        }
    ]
]

let private uploadTests = testList "Upload" [
    testTask "Add succeeds" {
        let data = mkData ()
        try do! UploadDataTests.``Add succeeds`` data
        finally dispose data
    }
    testList "FindByPath" [
        testTask "succeeds when an upload is found" {
            let data = mkData ()
            try do! UploadDataTests.``FindByPath succeeds when an upload is found`` data
            finally dispose data
        }
        testTask "succeeds when an upload is not found (incorrect weblog)" {
            let data = mkData ()
            try do! UploadDataTests.``FindByPath succeeds when an upload is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when an upload is not found (bad path)" {
            let data = mkData ()
            try do! UploadDataTests.``FindByPath succeeds when an upload is not found (bad path)`` data
            finally dispose data
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when uploads exist" {
            let data = mkData ()
            try do! UploadDataTests.``FindByWebLog succeeds when uploads exist`` data
            finally dispose data
        }
        testTask "succeeds when no uploads exist" {
            let data = mkData ()
            try do! UploadDataTests.``FindByWebLog succeeds when no uploads exist`` data
            finally dispose data
        }
    ]
    testList "FindByWebLogWithData" [
        testTask "succeeds when uploads exist" {
            let data = mkData ()
            try do! UploadDataTests.``FindByWebLogWithData succeeds when uploads exist`` data
            finally dispose data
        }
        testTask "succeeds when no uploads exist" {
            let data = mkData ()
            try do! UploadDataTests.``FindByWebLogWithData succeeds when no uploads exist`` data
            finally dispose data
        }
    ]
    testList "Delete" [
        testTask "succeeds when an upload is deleted" {
            let data = mkData ()
            try do! UploadDataTests.``Delete succeeds when an upload is deleted`` data
            finally dispose data
        }
        testTask "succeeds when an upload is not deleted" {
            let data = mkData ()
            try do! UploadDataTests.``Delete succeeds when an upload is not deleted`` data
            finally dispose data
        }
    ]
]

let private webLogUserTests = testList "WebLogUser" [
    testTask "Add succeeds" {
        // This restore ensures all the posts and pages exist
        let! data = freshEnvironment None
        try do! WebLogUserDataTests.``Add succeeds`` data
        finally dispose data
    }
    testList "FindByEmail" [
        testTask "succeeds when a user is found" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindByEmail succeeds when a user is found`` data
            finally dispose data
        }
        testTask "succeeds when a user is not found (incorrect weblog)" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindByEmail succeeds when a user is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a user is not found (bad email)" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindByEmail succeeds when a user is not found (bad email)`` data
            finally dispose data
        }
    ]
    testList "FindById" [
        testTask "succeeds when a user is found" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindById succeeds when a user is found`` data
            finally dispose data
        }
        testTask "succeeds when a user is not found (incorrect weblog)" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindById succeeds when a user is not found (incorrect weblog)`` data
            finally dispose data
        }
        testTask "succeeds when a user is not found (bad ID)" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindById succeeds when a user is not found (bad ID)`` data
            finally dispose data
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when users exist" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindByWebLog succeeds when users exist`` data
            finally dispose data
        }
        testTask "succeeds when no users exist" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindByWebLog succeeds when no users exist`` data
            finally dispose data
        }
    ]
    testList "FindNames" [
        testTask "succeeds when users exist" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindNames succeeds when users exist`` data
            finally dispose data
        }
        testTask "succeeds when users do not exist" {
            let data = mkData ()
            try do! WebLogUserDataTests.``FindNames succeeds when users do not exist`` data
            finally dispose data
        }
    ]
    testList "SetLastSeen" [
        testTask "succeeds when the user exists" {
            let data = mkData ()
            try do! WebLogUserDataTests.``SetLastSeen succeeds when the user exists`` data
            finally dispose data
        }
        testTask "succeeds when the user does not exist" {
            let data = mkData ()
            try do! WebLogUserDataTests.``SetLastSeen succeeds when the user does not exist`` data
            finally dispose data
        }
    ]
    testList "Update" [
        testTask "succeeds when the user exists" {
            let data = mkData ()
            try do! WebLogUserDataTests.``Update succeeds when the user exists`` data
            finally dispose data
        }
        testTask "succeeds when the user does not exist" {
            let data = mkData ()
            try do! WebLogUserDataTests.``Update succeeds when the user does not exist`` data
            finally dispose data
        }
    ]
    testList "Delete" [
        testTask "fails when the user is the author of a page" {
            let data = mkData ()
            try do! WebLogUserDataTests.``Delete fails when the user is the author of a page`` data
            finally dispose data
        }
        testTask "fails when the user is the author of a post" {
            let data = mkData ()
            try do! WebLogUserDataTests.``Delete fails when the user is the author of a post`` data
            finally dispose data
        }
        testTask "succeeds when the user is not an author" {
            let data = mkData ()
            try do! WebLogUserDataTests.``Delete succeeds when the user is not an author`` data
            finally dispose data
        }
        testTask "succeeds when the user does not exist" {
            let data = mkData ()
            try do! WebLogUserDataTests.``Delete succeeds when the user does not exist`` data
            finally dispose data
        }
    ]
]

let private webLogTests = testList "WebLog" [
    testTask "Add succeeds" {
        let data = mkData ()
        try do! WebLogDataTests.``Add succeeds`` data
        finally dispose data
    }
    testTask "All succeeds" {
        let data = mkData ()
        try do! WebLogDataTests.``All succeeds`` data
        finally dispose data
    }
    testList "FindByHost" [
        testTask "succeeds when a web log is found" {
            let data = mkData ()
            try do! WebLogDataTests.``FindByHost succeeds when a web log is found`` data
            finally dispose data
        }
        testTask "succeeds when a web log is not found" {
            let data = mkData ()
            try do! WebLogDataTests.``FindByHost succeeds when a web log is not found`` data
            finally dispose data
        }
    ]
    testList "FindById" [
        testTask "succeeds when a web log is found" {
            let data = mkData ()
            try do! WebLogDataTests.``FindById succeeds when a web log is found`` data
            finally dispose data
        }
        testTask "succeeds when a web log is not found" {
            let data = mkData ()
            try do! WebLogDataTests.``FindById succeeds when a web log is not found`` data
            finally dispose data
        }
    ]
    testList "UpdateRedirectRules" [
        testTask "succeeds when the web log exists" {
            let data = mkData ()
            try do! WebLogDataTests.``UpdateRedirectRules succeeds when the web log exists`` data
            finally dispose data
        }
        testTask "succeeds when the web log does not exist" {
            let data = mkData ()
            try do! WebLogDataTests.``UpdateRedirectRules succeeds when the web log does not exist`` data
            finally dispose data
        }
    ]
    testList "UpdateRssOptions" [
        testTask "succeeds when the web log exists" {
            let data = mkData ()
            try do! WebLogDataTests.``UpdateRssOptions succeeds when the web log exists`` data
            finally dispose data
        }
        testTask "succeeds when the web log does not exist" {
            let data = mkData ()
            try do! WebLogDataTests.``UpdateRssOptions succeeds when the web log does not exist`` data
            finally dispose data
        }
    ]
    testList "UpdateSettings" [
        testTask "succeeds when the web log exists" {
            let data = mkData ()
            try do! WebLogDataTests.``UpdateSettings succeeds when the web log exists`` data
            finally dispose data
        }
        testTask "succeeds when the web log does not exist" {
            let data = mkData ()
            try do! WebLogDataTests.``UpdateSettings succeeds when the web log does not exist`` data
            finally dispose data
        }
    ]
    testList "Delete" [
        testTask "succeeds when the web log exists" {
            let data = mkData ()
            try
                do! WebLogDataTests.``Delete succeeds when the web log exists`` data
                let! revisions =
                    (data :?> SQLiteData).Conn.customScalar
                        "SELECT (SELECT COUNT(*) FROM page_revision) + (SELECT COUNT(*) FROM post_revision) AS it"
                        []
                        toCount
                Expect.equal revisions 0L "All revisions should be deleted"
            finally dispose data
        }
        testTask "succeeds when the web log does not exist" {
            let data = mkData ()
            try do! WebLogDataTests.``Delete succeeds when the web log does not exist`` data
            finally dispose data
        }
    ]
]

/// Delete the SQLite database
let private environmentCleanUp = test "Clean Up" {
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
          themeAssetTests
          uploadTests
          webLogUserTests
          webLogTests
          environmentCleanUp ]
    |> testSequenced
