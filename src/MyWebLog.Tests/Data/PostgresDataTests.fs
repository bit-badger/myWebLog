module PostgresDataTests

open System
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
let ser = Json.configure (JsonSerializer.CreateDefault())

/// The throwaway database (deleted when disposed)
let mutable db: ThrowawayDatabase option = None

/// Create a PostgresData instance for testing
let mkData () =
    PostgresData(NullLogger<PostgresData>(), ser) :> IData

/// The host for the PostgreSQL test database (defaults to localhost)
let testHost =
    RethinkDbDataTests.env "PG_HOST" "localhost"

/// The database name for the PostgreSQL test database (defaults to postgres)
let testDb =
    RethinkDbDataTests.env "PG_DB" "postgres"

/// The user ID for the PostgreSQL test database (defaults to postgres)
let testUser =
    RethinkDbDataTests.env "PG_USER" "postgres"

/// The password for the PostgreSQL test database (defaults to postgres)
let testPw =
    RethinkDbDataTests.env "PG_PW" "postgres"

/// Create a fresh environment from the root backup
let freshEnvironment () = task {
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
let environmentSetUp = testTask "creating database" {
    do! freshEnvironment ()
}

/// Integration tests for the Category implementation in PostgreSQL
let categoryTests = testList "Category" [
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
let pageTests = testList "Page" [
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
let postTests = testList "Post" [
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
]

/// Drop the throwaway PostgreSQL database
let environmentCleanUp = test "Clean Up" {
    if db.IsSome then db.Value.Dispose()
} 

/// All PostgreSQL data tests
let all =
    testList "PostgresData"
        [ environmentSetUp
          categoryTests
          pageTests
          postTests
          environmentCleanUp ]
    |> testSequenced