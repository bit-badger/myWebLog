module PostgresDataTests

open BitBadger.Documents
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open MyWebLog
open MyWebLog.Converters
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open ThrowawayDb.Postgres

/// JSON serializer 
let private ser = Json.configure (JsonSerializer.CreateDefault())

/// The throwaway database (deleted when disposed)
let mutable private db: ThrowawayDatabase option = None

/// Create a PostgresData instance for testing
let private mkData () =
    PostgresData(NullLogger<PostgresData>(), ser) :> IData

/// The host for the PostgreSQL test database (defaults to localhost)
let private testHost =
    RethinkDbDataTests.env "PG_HOST" "localhost"

/// The database name for the PostgreSQL test database (defaults to postgres)
let private testDb =
    RethinkDbDataTests.env "PG_DB" "postgres"

/// The user ID for the PostgreSQL test database (defaults to postgres)
let private testUser =
    RethinkDbDataTests.env "PG_USER" "postgres"

/// The password for the PostgreSQL test database (defaults to postgres)
let private testPw =
    RethinkDbDataTests.env "PG_PW" "postgres"

/// Create a fresh environment from the root backup
let private freshEnvironment () = task {
    if Option.isSome db then db.Value.Dispose()
    db <- Some (ThrowawayDatabase.Create $"Host={testHost};Database={testDb};User ID={testUser};Password={testPw}")
    let source = NpgsqlDataSourceBuilder db.Value.ConnectionString
    let _ = source.UseNodaTime()
    Postgres.Configuration.useDataSource (source.Build())
    let env = mkData ()
    do! env.StartUp()
    // This exercises Restore for all implementations; all tests are dependent on it working as expected
    do! Maintenance.Backup.restoreBackup "root-weblog.json" None false false env
}

/// Set up the environment for the PostgreSQL tests
let private environmentSetUp = testTask "creating database" {
    do! freshEnvironment ()
}

/// Integration tests for the Category implementation in PostgreSQL
let private categoryTests = testList "Category" [
    testTask "Add succeeds" {
        do! CategoryDataTests.``Add succeeds`` (mkData ())
    }
    testList "CountAll" [
        testTask "succeeds when categories exist" {
            do! CategoryDataTests.``CountAll succeeds when categories exist`` (mkData ())
        }
        testTask "succeeds when categories do not exist" {
            do! CategoryDataTests.``CountAll succeeds when categories do not exist`` (mkData ())
        }
    ]
    testList "CountTopLevel" [
        testTask "succeeds when top-level categories exist" {
            do! CategoryDataTests.``CountTopLevel succeeds when top-level categories exist`` (mkData ())
        }
        testTask "succeeds when no top-level categories exist" {
            do! CategoryDataTests.``CountTopLevel succeeds when no top-level categories exist`` (mkData ())
        }
    ]
    testTask "FindAllForView succeeds" {
        do! CategoryDataTests.``FindAllForView succeeds`` (mkData ())
    }
    testList "FindById" [
        testTask "succeeds when a category is found" {
            do! CategoryDataTests.``FindById succeeds when a category is found`` (mkData ())
        }
        testTask "succeeds when a category is not found" {
            do! CategoryDataTests.``FindById succeeds when a category is not found`` (mkData ())
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when categories exist" {
            do! CategoryDataTests.``FindByWebLog succeeds when categories exist`` (mkData ())
        }
        testTask "succeeds when no categories exist" {
            do! CategoryDataTests.``FindByWebLog succeeds when no categories exist`` (mkData ())
        }
    ]
    testTask "Update succeeds" {
        do! CategoryDataTests.``Update succeeds`` (mkData ())
    }
    testList "Delete" [
        testTask "succeeds when the category is deleted (no posts)" {
            do! CategoryDataTests.``Delete succeeds when the category is deleted (no posts)`` (mkData ())
        }
        testTask "succeeds when the category does not exist" {
            do! CategoryDataTests.``Delete succeeds when the category does not exist`` (mkData ())
        }
        testTask "succeeds when reassigning parent category to None" {
            do! CategoryDataTests.``Delete succeeds when reassigning parent category to None`` (mkData ())
        }
        testTask "succeeds when reassigning parent category to Some" {
            do! CategoryDataTests.``Delete succeeds when reassigning parent category to Some`` (mkData ())
        }
        testTask "succeeds and removes category from posts" {
            do! CategoryDataTests.``Delete succeeds and removes category from posts`` (mkData ())
        }
    ]
]

/// Integration tests for the Page implementation in PostgreSQL
let private pageTests = testList "Page" [
    testTask "Add succeeds" {
        do! PageDataTests.``Add succeeds`` (mkData ())
    }
    testTask "All succeeds" {
        do! PageDataTests.``All succeeds`` (mkData ())
    }
    testTask "CountAll succeeds" {
        do! PageDataTests.``CountAll succeeds`` (mkData ())
    }
    testTask "CountListed succeeds" {
        do! PageDataTests.``CountListed succeeds`` (mkData ())
    }
    testList "FindById" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindById succeeds when a page is found`` (mkData ())
        }
        testTask "succeeds when a page is not found (incorrect weblog)" {
            do! PageDataTests.``FindById succeeds when a page is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when a page is not found (bad page ID)" {
            do! PageDataTests.``FindById succeeds when a page is not found (bad page ID)`` (mkData ())
        }
    ]
    testList "FindByPermalink" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindByPermalink succeeds when a page is found`` (mkData ())
        }
        testTask "succeeds when a page is not found (incorrect weblog)" {
            do! PageDataTests.``FindByPermalink succeeds when a page is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when a page is not found (no such permalink)" {
            do! PageDataTests.``FindByPermalink succeeds when a page is not found (no such permalink)`` (mkData ())
        }
    ]
    testList "FindCurrentPermalink" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindCurrentPermalink succeeds when a page is found`` (mkData ())
        }
        testTask "succeeds when a page is not found" {
            do! PageDataTests.``FindCurrentPermalink succeeds when a page is not found`` (mkData ())
        }
    ]
    testList "FindFullById" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindFullById succeeds when a page is found`` (mkData ())
        }
        testTask "succeeds when a page is not found" {
            do! PageDataTests.``FindFullById succeeds when a page is not found`` (mkData ())
        }
    ]
    testList "FindFullByWebLog" [
        testTask "succeeds when pages are found" {
            do! PageDataTests.``FindFullByWebLog succeeds when pages are found`` (mkData ())
        }
        testTask "succeeds when a pages are not found" {
            do! PageDataTests.``FindFullByWebLog succeeds when pages are not found`` (mkData ())
        }
    ]
    testList "FindListed" [
        testTask "succeeds when pages are found" {
            do! PageDataTests.``FindListed succeeds when pages are found`` (mkData ())
        }
        testTask "succeeds when a pages are not found" {
            do! PageDataTests.``FindListed succeeds when pages are not found`` (mkData ())
        }
    ]
    testList "FindPageOfPages" [
        testTask "succeeds when pages are found" {
            do! PageDataTests.``FindPageOfPages succeeds when pages are found`` (mkData ())
        }
        testTask "succeeds when a pages are not found" {
            do! PageDataTests.``FindPageOfPages succeeds when pages are not found`` (mkData ())
        }
    ]
    testList "Update" [
        testTask "succeeds when the page exists" {
            do! PageDataTests.``Update succeeds when the page exists`` (mkData ())
        }
        testTask "succeeds when the page does not exist" {
            do! PageDataTests.``Update succeeds when the page does not exist`` (mkData ())
        }
    ]
    testList "UpdatePriorPermalinks" [
        testTask "succeeds when the page exists" {
            do! PageDataTests.``UpdatePriorPermalinks succeeds when the page exists`` (mkData ())
        }
        testTask "succeeds when the page does not exist" {
            do! PageDataTests.``UpdatePriorPermalinks succeeds when the page does not exist`` (mkData ())
        }
    ]
    testList "Delete" [
        testTask "succeeds when a page is deleted" {
            do! PageDataTests.``Delete succeeds when a page is deleted`` (mkData ())
        }
        testTask "succeeds when a page is not deleted" {
            do! PageDataTests.``Delete succeeds when a page is not deleted`` (mkData ())
        }
    ]
]

/// Integration tests for the Post implementation in PostgreSQL
let private postTests = testList "Post" [
    testTask "Add succeeds" {
        // We'll need the root website categories restored for these tests
        do! freshEnvironment ()
        do! PostDataTests.``Add succeeds`` (mkData ())
    }
    testTask "CountByStatus succeeds" {
        do! PostDataTests.``CountByStatus succeeds`` (mkData ())
    }
    testList "FindById" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindById succeeds when a post is found`` (mkData ())
        }
        testTask "succeeds when a post is not found (incorrect weblog)" {
            do! PostDataTests.``FindById succeeds when a post is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when a post is not found (bad post ID)" {
            do! PostDataTests.``FindById succeeds when a post is not found (bad post ID)`` (mkData ())
        }
    ]
    testList "FindByPermalink" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindByPermalink succeeds when a post is found`` (mkData ())
        }
        testTask "succeeds when a post is not found (incorrect weblog)" {
            do! PostDataTests.``FindByPermalink succeeds when a post is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when a post is not found (no such permalink)" {
            do! PostDataTests.``FindByPermalink succeeds when a post is not found (no such permalink)`` (mkData ())
        }
    ]
    testList "FindCurrentPermalink" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindCurrentPermalink succeeds when a post is found`` (mkData ())
        }
        testTask "succeeds when a post is not found" {
            do! PostDataTests.``FindCurrentPermalink succeeds when a post is not found`` (mkData ())
        }
    ]
    testList "FindFullById" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindFullById succeeds when a post is found`` (mkData ())
        }
        testTask "succeeds when a post is not found" {
            do! PostDataTests.``FindFullById succeeds when a post is not found`` (mkData ())
        }
    ]
    testList "FindFullByWebLog" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindFullByWebLog succeeds when posts are found`` (mkData ())
        }
        testTask "succeeds when a posts are not found" {
            do! PostDataTests.``FindFullByWebLog succeeds when posts are not found`` (mkData ())
        }
    ]
    testList "FindPageOfCategorizedPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfCategorizedPosts succeeds when posts are found`` (mkData ())
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfCategorizedPosts succeeds when finding a too-high page number`` (mkData ())
        }
        testTask "succeeds when a category has no posts" {
            do! PostDataTests.``FindPageOfCategorizedPosts succeeds when a category has no posts`` (mkData ())
        }
    ]
    testList "FindPageOfPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfPosts succeeds when posts are found`` (mkData ())
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfPosts succeeds when finding a too-high page number`` (mkData ())
        }
        testTask "succeeds when there are no posts" {
            do! PostDataTests.``FindPageOfPosts succeeds when there are no posts`` (mkData ())
        }
    ]
    testList "FindPageOfPublishedPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfPublishedPosts succeeds when posts are found`` (mkData ())
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfPublishedPosts succeeds when finding a too-high page number`` (mkData ())
        }
        testTask "succeeds when there are no posts" {
            do! PostDataTests.``FindPageOfPublishedPosts succeeds when there are no posts`` (mkData ())
        }
    ]
    testList "FindPageOfTaggedPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when posts are found`` (mkData ())
        }
        testTask "succeeds when posts are found (excluding drafts)" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when posts are found (excluding drafts)`` (mkData ())
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when finding a too-high page number`` (mkData ())
        }
        testTask "succeeds when there are no posts" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when there are no posts`` (mkData ())
        }
    ]
    testList "FindSurroundingPosts" [
        testTask "succeeds when there is no next newer post" {
            do! PostDataTests.``FindSurroundingPosts succeeds when there is no next newer post`` (mkData ())
        }
        testTask "succeeds when there is no next older post" {
            do! PostDataTests.``FindSurroundingPosts succeeds when there is no next older post`` (mkData ())
        }
        testTask "succeeds when older and newer exist" {
            do! PostDataTests.``FindSurroundingPosts succeeds when older and newer exist`` (mkData ())
        }
    ]
    testList "Update" [
        testTask "succeeds when the post exists" {
            do! PostDataTests.``Update succeeds when the post exists`` (mkData ())
        }
        testTask "succeeds when the post does not exist" {
            do! PostDataTests.``Update succeeds when the post does not exist`` (mkData ())
        }
    ]
    testList "UpdatePriorPermalinks" [
        testTask "succeeds when the post exists" {
            do! PostDataTests.``UpdatePriorPermalinks succeeds when the post exists`` (mkData ())
        }
        testTask "succeeds when the post does not exist" {
            do! PostDataTests.``UpdatePriorPermalinks succeeds when the post does not exist`` (mkData ())
        }
    ]
    testList "Delete" [
        testTask "succeeds when a post is deleted" {
            do! PostDataTests.``Delete succeeds when a post is deleted`` (mkData ())
        }
        testTask "succeeds when a post is not deleted" {
            do! PostDataTests.``Delete succeeds when a post is not deleted`` (mkData ())
        }
    ]
]

let private tagMapTests = testList "TagMap" [
    testList "FindById" [
        testTask "succeeds when a tag mapping is found" {
            do! TagMapDataTests.``FindById succeeds when a tag mapping is found`` (mkData ())
        }
        testTask "succeeds when a tag mapping is not found (incorrect weblog)" {
            do! TagMapDataTests.``FindById succeeds when a tag mapping is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when a tag mapping is not found (bad tag map ID)" {
            do! TagMapDataTests.``FindById succeeds when a tag mapping is not found (bad tag map ID)`` (mkData ())
        }
    ]
    testList "FindByUrlValue" [
        testTask "succeeds when a tag mapping is found" {
            do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is found`` (mkData ())
        }
        testTask "succeeds when a tag mapping is not found (incorrect weblog)" {
            do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is not found (incorrect weblog)``
                    (mkData ())
        }
        testTask "succeeds when a tag mapping is not found (no such value)" {
            do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is not found (no such value)`` (mkData ())
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when tag mappings are found" {
            do! TagMapDataTests.``FindByWebLog succeeds when tag mappings are found`` (mkData ())
        }
        testTask "succeeds when no tag mappings are found" {
            do! TagMapDataTests.``FindByWebLog succeeds when no tag mappings are found`` (mkData ())
        }
    ]
    testList "FindMappingForTags" [
        testTask "succeeds when mappings exist" {
            do! TagMapDataTests.``FindMappingForTags succeeds when mappings exist`` (mkData ())
        }
        testTask "succeeds when no mappings exist" {
            do! TagMapDataTests.``FindMappingForTags succeeds when no mappings exist`` (mkData ())
        }
    ]
    testList "Save" [
        testTask "succeeds when adding a tag mapping" {
            do! TagMapDataTests.``Save succeeds when adding a tag mapping`` (mkData ())
        }
        testTask "succeeds when updating a tag mapping" {
            do! TagMapDataTests.``Save succeeds when updating a tag mapping`` (mkData ())
        }
    ]
    testList "Delete" [
        testTask "succeeds when a tag mapping is deleted" {
            do! TagMapDataTests.``Delete succeeds when a tag mapping is deleted`` (mkData ())
        }
        testTask "succeeds when a tag mapping is not deleted" {
            do! TagMapDataTests.``Delete succeeds when a tag mapping is not deleted`` (mkData ())
        }
    ]
]

let private themeTests = testList "Theme" [
    testTask "All succeeds" {
        do! ThemeDataTests.``All succeeds`` (mkData ())
    }
    testList "Exists" [
        testTask "succeeds when the theme exists" {
            do! ThemeDataTests.``Exists succeeds when the theme exists`` (mkData ())
        }
        testTask "succeeds when the theme does not exist" {
            do! ThemeDataTests.``Exists succeeds when the theme does not exist`` (mkData ())
        }
    ]
    testList "FindById" [
        testTask "succeeds when the theme exists" {
            do! ThemeDataTests.``FindById succeeds when the theme exists`` (mkData ())
        }
        testTask "succeeds when the theme does not exist" {
            do! ThemeDataTests.``FindById succeeds when the theme does not exist`` (mkData ())
        }
    ]
    testList "FindByIdWithoutText" [
        testTask "succeeds when the theme exists" {
            do! ThemeDataTests.``FindByIdWithoutText succeeds when the theme exists`` (mkData ())
        }
        testTask "succeeds when the theme does not exist" {
            do! ThemeDataTests.``FindByIdWithoutText succeeds when the theme does not exist`` (mkData ())
        }
    ]
    testList "Save" [
        testTask "succeeds when adding a theme" {
            do! ThemeDataTests.``Save succeeds when adding a theme`` (mkData ())
        }
        testTask "succeeds when updating a theme" {
            do! ThemeDataTests.``Save succeeds when updating a theme`` (mkData ())
        }
    ]
    testList "Delete" [
        testTask "succeeds when a theme is deleted" {
            do! ThemeDataTests.``Delete succeeds when a theme is deleted`` (mkData ())
        }
        testTask "succeeds when a theme is not deleted" {
            do! ThemeDataTests.``Delete succeeds when a theme is not deleted`` (mkData ())
        }
    ]
]

let private themeAssetTests = testList "ThemeAsset" [
    testList "Save" [
        testTask "succeeds when adding an asset" {
            do! ThemeDataTests.Asset.``Save succeeds when adding an asset`` (mkData ())
        }
        testTask "succeeds when updating an asset" {
            do! ThemeDataTests.Asset.``Save succeeds when updating an asset`` (mkData ())
        }
    ]
    testTask "All succeeds" {
        do! ThemeDataTests.Asset.``All succeeds`` (mkData ())
    }
    testList "FindById" [
        testTask "succeeds when an asset is found" {
            do! ThemeDataTests.Asset.``FindById succeeds when an asset is found`` (mkData ())
        }
        testTask "succeeds when an asset is not found" {
            do! ThemeDataTests.Asset.``FindById succeeds when an asset is not found`` (mkData ())
        }
    ]
    testList "FindByTheme" [
        testTask "succeeds when assets exist" {
            do! ThemeDataTests.Asset.``FindByTheme succeeds when assets exist`` (mkData ())
        }
        testTask "succeeds when assets do not exist" {
            do! ThemeDataTests.Asset.``FindByTheme succeeds when assets do not exist`` (mkData ())
        }
    ]
    testList "FindByThemeWithData" [
        testTask "succeeds when assets exist" {
            do! ThemeDataTests.Asset.``FindByThemeWithData succeeds when assets exist`` (mkData ())
        }
        testTask "succeeds when assets do not exist" {
            do! ThemeDataTests.Asset.``FindByThemeWithData succeeds when assets do not exist`` (mkData ())
        }
    ]
    testList "DeleteByTheme" [
        testTask "succeeds when assets are deleted" {
            do! ThemeDataTests.Asset.``DeleteByTheme succeeds when assets are deleted`` (mkData ())
        }
        testTask "succeeds when no assets are deleted" {
            do! ThemeDataTests.Asset.``DeleteByTheme succeeds when no assets are deleted`` (mkData ())
        }
    ]
]

let private uploadTests = testList "Upload" [
    testTask "Add succeeds" {
        do! UploadDataTests.``Add succeeds`` (mkData ())
    }
    testList "FindByPath" [
        testTask "succeeds when an upload is found" {
            do! UploadDataTests.``FindByPath succeeds when an upload is found`` (mkData ())
        }
        testTask "succeeds when an upload is not found (incorrect weblog)" {
            do! UploadDataTests.``FindByPath succeeds when an upload is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when an upload is not found (bad path)" {
            do! UploadDataTests.``FindByPath succeeds when an upload is not found (bad path)`` (mkData ())
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when uploads exist" {
            do! UploadDataTests.``FindByWebLog succeeds when uploads exist`` (mkData ())
        }
        testTask "succeeds when no uploads exist" {
            do! UploadDataTests.``FindByWebLog succeeds when no uploads exist`` (mkData ())
        }
    ]
    testList "FindByWebLogWithData" [
        testTask "succeeds when uploads exist" {
            do! UploadDataTests.``FindByWebLogWithData succeeds when uploads exist`` (mkData ())
        }
        testTask "succeeds when no uploads exist" {
            do! UploadDataTests.``FindByWebLogWithData succeeds when no uploads exist`` (mkData ())
        }
    ]
    testList "Delete" [
        testTask "succeeds when an upload is deleted" {
            do! UploadDataTests.``Delete succeeds when an upload is deleted`` (mkData ())
        }
        testTask "succeeds when an upload is not deleted" {
            do! UploadDataTests.``Delete succeeds when an upload is not deleted`` (mkData ())
        }
    ]
]

let private webLogUserTests = testList "WebLogUser" [
    testTask "Add succeeds" {
        do! WebLogUserDataTests.``Add succeeds`` (mkData ())
    }
    testList "FindByEmail" [
        testTask "succeeds when a user is found" {
            do! WebLogUserDataTests.``FindByEmail succeeds when a user is found`` (mkData ())
        }
        testTask "succeeds when a user is not found (incorrect weblog)" {
            do! WebLogUserDataTests.``FindByEmail succeeds when a user is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when a user is not found (bad email)" {
            do! WebLogUserDataTests.``FindByEmail succeeds when a user is not found (bad email)`` (mkData ())
        }
    ]
    testList "FindById" [
        testTask "succeeds when a user is found" {
            do! WebLogUserDataTests.``FindById succeeds when a user is found`` (mkData ())
        }
        testTask "succeeds when a user is not found (incorrect weblog)" {
            do! WebLogUserDataTests.``FindById succeeds when a user is not found (incorrect weblog)`` (mkData ())
        }
        testTask "succeeds when a user is not found (bad ID)" {
            do! WebLogUserDataTests.``FindById succeeds when a user is not found (bad ID)`` (mkData ())
        }
    ]
]

/// Drop the throwaway PostgreSQL database
let private environmentCleanUp = test "Clean Up" {
    if db.IsSome then db.Value.Dispose()
} 

/// All PostgreSQL data tests
let all =
    testList "PostgresData"
        [ environmentSetUp
          categoryTests
          pageTests
          postTests
          tagMapTests
          themeTests
          themeAssetTests
          uploadTests
          webLogUserTests
          environmentCleanUp ]
    |> testSequenced
