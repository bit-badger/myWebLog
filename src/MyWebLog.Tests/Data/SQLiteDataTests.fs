module SQLiteDataTests

open System.IO
open BitBadger.Documents
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open MyWebLog
open MyWebLog.Converters
open MyWebLog.Data
open Newtonsoft.Json

/// JSON serializer 
let ser = Json.configure (JsonSerializer.CreateDefault())

/// The test database name
let dbName = "test-db.db"

/// Create a SQLiteData instance for testing
let mkData () =
    Sqlite.Configuration.useConnectionString $"Data Source=./{dbName}"
    let conn = Sqlite.Configuration.dbConn ()
    SQLiteData(conn, NullLogger<SQLiteData>(), ser) :> IData

/// Dispose the connection associated with the SQLiteData instance
let dispose (data: IData) =
    (data :?> SQLiteData).Conn.Dispose()

/// Create a fresh environment from the root backup
let freshEnvironment (data: IData option) = task {
    let env =
        match data with
        | Some d -> d
        | None ->
            File.Delete dbName
            mkData ()
    do! env.StartUp()
    // This exercises Restore for all implementations; all tests are dependent on it working as expected
    do! Maintenance.Backup.restoreBackup "root-weblog.json" None false false env
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
          environmentCleanUp ]
    |> testSequenced
