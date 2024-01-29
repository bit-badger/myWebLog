module RethinkDbTests

open System
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open MyWebLog
open MyWebLog.Converters
open MyWebLog.Data
open RethinkDb.Driver.FSharp
open RethinkDb.Driver.Net

/// Get an environment variable, using the given value as the default if it is not set
let env name value =
    match Environment.GetEnvironmentVariable $"MWL_TEST_{name}" with
    | null -> value
    | it when it.Trim() = "" -> value
    | it -> it


/// The data configuration for the test database
let dataCfg =
    DataConfig.FromUri (env "RETHINK_URI" "rethinkdb://172.17.0.2/mwl_test")

/// The active data instance to use for testing
let mutable data: IData option = None

/// Dispose the existing data
let disposeData () = task {
    if data.IsSome then
        let conn = (data.Value :?> RethinkDbData).Conn
        do! rethink { dbDrop dataCfg.Database; write; withRetryOnce; ignoreResult conn }
        conn.Dispose()
    data <- None
}

/// Create a new data implementation instance
let newData () =
    let log  = NullLogger<RethinkDbData>()
    let conn = dataCfg.CreateConnection log
    RethinkDbData(conn, dataCfg, log)

/// Create a fresh environment from the root backup
let freshEnvironment () = task {
    do! disposeData ()
    data <- Some (newData ())
    do! data.Value.StartUp()
    // This exercises Restore for all implementations; all tests are dependent on it working as expected
    do! Maintenance.Backup.restoreBackup "root-weblog.json" None false false data.Value
}

/// Set up the environment for the RethinkDB tests
let environmentSetUp = testTask "creating database" {
    let _ = Json.configure Converter.Serializer
    do! freshEnvironment ()
}

/// Integration tests for the Category implementation in RethinkDB
let categoryTests = testList "Category" [
    testTask "Add succeeds" {
        do! CategoryDataTests.``Add succeeds`` data.Value
    }
    testList "CountAll" [
        testTask "succeeds when categories exist" {
            do! CategoryDataTests.``CountAll succeeds when categories exist`` data.Value
        }
        testTask "succeeds when categories do not exist" {
            do! CategoryDataTests.``CountAll succeeds when categories do not exist`` data.Value
        }
    ]
    testList "CountTopLevel" [
        testTask "succeeds when top-level categories exist" {
            do! CategoryDataTests.``CountTopLevel succeeds when top-level categories exist`` data.Value
        }
        testTask "succeeds when no top-level categories exist" {
            do! CategoryDataTests.``CountTopLevel succeeds when no top-level categories exist`` data.Value
        }
    ]
    testTask "FindAllForView succeeds" {
        do! CategoryDataTests.``FindAllForView succeeds`` data.Value
    }
    testList "FindById" [
        testTask "succeeds when a category is found" {
            do! CategoryDataTests.``FindById succeeds when a category is found`` data.Value
        }
        testTask "succeeds when a category is not found" {
            do! CategoryDataTests.``FindById succeeds when a category is not found`` data.Value
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when categories exist" {
            do! CategoryDataTests.``FindByWebLog succeeds when categories exist`` data.Value
        }
        testTask "succeeds when no categories exist" {
            do! CategoryDataTests.``FindByWebLog succeeds when no categories exist`` data.Value
        }
    ]
    testTask "Update succeeds" {
        do! CategoryDataTests.``Update succeeds`` data.Value
    }
    testList "Delete" [
        testTask "succeeds when the category is deleted (no posts)" {
            do! CategoryDataTests.``Delete succeeds when the category is deleted (no posts)`` data.Value
        }
        testTask "succeeds when the category does not exist" {
            do! CategoryDataTests.``Delete succeeds when the category does not exist`` data.Value
        }
        testTask "succeeds when reassigning parent category to None" {
            do! CategoryDataTests.``Delete succeeds when reassigning parent category to None`` data.Value
        }
        testTask "succeeds when reassigning parent category to Some" {
            do! CategoryDataTests.``Delete succeeds when reassigning parent category to Some`` data.Value
        }
        testTask "succeeds and removes category from posts" {
            do! CategoryDataTests.``Delete succeeds and removes category from posts`` data.Value
        }
    ]
]

/// Integration tests for the Page implementation in RethinkDB
let pageTests = testList "Page" [
    testTask "Add succeeds" {
        do! PageDataTests.``Add succeeds`` data.Value
    }
    testTask "All succeeds" {
        do! PageDataTests.``All succeeds`` data.Value
    }
    testTask "CountAll succeeds" {
        do! PageDataTests.``CountAll succeeds`` data.Value
    }
    testTask "CountListed succeeds" {
        do! PageDataTests.``CountListed succeeds`` data.Value
    }
    testList "FindById" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindById succeeds when a page is found`` data.Value
        }
        testTask "succeeds when a page is not found (incorrect weblog)" {
            do! PageDataTests.``FindById succeeds when a page is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when a page is not found (bad page ID)" {
            do! PageDataTests.``FindById succeeds when a page is not found (bad page ID)`` data.Value
        }
    ]
    testList "FindByPermalink" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindByPermalink succeeds when a page is found`` data.Value
        }
        testTask "succeeds when a page is not found (incorrect weblog)" {
            do! PageDataTests.``FindByPermalink succeeds when a page is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when a page is not found (no such permalink)" {
            do! PageDataTests.``FindByPermalink succeeds when a page is not found (no such permalink)`` data.Value
        }
    ]
    testList "FindCurrentPermalink" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindCurrentPermalink succeeds when a page is found`` data.Value
        }
        testTask "succeeds when a page is not found" {
            do! PageDataTests.``FindCurrentPermalink succeeds when a page is not found`` data.Value
        }
    ]
    testList "FindFullById" [
        testTask "succeeds when a page is found" {
            do! PageDataTests.``FindFullById succeeds when a page is found`` data.Value
        }
        testTask "succeeds when a page is not found" {
            do! PageDataTests.``FindFullById succeeds when a page is not found`` data.Value
        }
    ]
    testList "FindFullByWebLog" [
        testTask "succeeds when pages are found" {
            do! PageDataTests.``FindFullByWebLog succeeds when pages are found`` data.Value
        }
        testTask "succeeds when a pages are not found" {
            do! PageDataTests.``FindFullByWebLog succeeds when pages are not found`` data.Value
        }
    ]
    testList "FindListed" [
        testTask "succeeds when pages are found" {
            do! PageDataTests.``FindListed succeeds when pages are found`` data.Value
        }
        testTask "succeeds when a pages are not found" {
            do! PageDataTests.``FindListed succeeds when pages are not found`` data.Value
        }
    ]
]

/// Drop the throwaway RethinkDB database
let environmentCleanUp = testTask "Clean Up" {
    do! disposeData ()
}

/// All RethinkDB data tests
let all =
    testList "RethinkDbData"
        [ environmentSetUp
          categoryTests
          pageTests
          environmentCleanUp ]
    |> testSequenced
