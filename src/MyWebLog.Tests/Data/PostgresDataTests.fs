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
let ser = Json.configure (JsonSerializer.CreateDefault())

/// The throwaway database (deleted when disposed)
let mutable db: ThrowawayDatabase option = None

/// Create a PostgresData instance for testing
let mkData () =
    PostgresData(NullLogger<PostgresData>(), ser) :> IData

/// Create a fresh environment from the root backup
let freshEnvironment () = task {
    if Option.isSome db then db.Value.Dispose()
    db <- Some (ThrowawayDatabase.Create "Host=localhost;Database=postgres;User ID=postgres;Password=postgres")
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

/// Integration tests for the Category implementation in SQLite
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

/// Drop the throwaway PostgreSQL database
let environmentCleanUp = test "Clean Up" {
    if db.IsSome then db.Value.Dispose()
} 

/// All PostgreSQL data tests
let all =
    testList "PostgresData"
        [ environmentSetUp
          categoryTests
          environmentCleanUp ]
    |> testSequenced
