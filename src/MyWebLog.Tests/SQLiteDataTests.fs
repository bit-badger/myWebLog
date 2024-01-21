module SQLiteDataTests

open BitBadger.Documents
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open MyWebLog
open MyWebLog.Converters
open MyWebLog.Data
open Newtonsoft.Json

/// JSON serializer 
let ser = Json.configure (JsonSerializer.CreateDefault())

/// Create a SQLiteData instance for testing
let mkData () =
    Sqlite.Configuration.useConnectionString "Data Source=./test-db.db"
    let conn = Sqlite.Configuration.dbConn ()
    SQLiteData(conn, NullLogger<SQLiteData>(), ser) :> IData

/// Dispose the connection associated with the SQLiteData instance
let dispose (data: IData) =
    (data :?> SQLiteData).Conn.Dispose()

/// Set up the environment for the SQLite tests
let environmentSetUp = testList "Environment" [
    testTask "creating database" {
        let data = mkData ()
        try
            do! data.StartUp()
            do! Maintenance.Backup.restoreBackup "root-weblog.json" None false data
        finally dispose data
    }
]

/// Integration tests for the Category implementation in SQLite
let categoryTests = testList "Category" [
    testTask "Add succeeds" {
        let data = mkData ()
        try do! CategoryDataTests.addTests data
        finally dispose data
    }
]


open System.IO

/// Delete the SQLite database
let environmentCleanUp = test "Clean Up" {
    File.Delete "test-db.db"
    Expect.isFalse (File.Exists "test-db.db") "The test SQLite database should have been deleted"
} 

/// All SQLite data tests
let all =
    testList "SQLiteData"
        [ environmentSetUp
          categoryTests
          environmentCleanUp ]
    |> testSequenced
