open Expecto

/// Whether to only run RethinkDB data tests
let rethinkOnly = (RethinkDbDataTests.env "RETHINK_ONLY" "0") = "1"

/// Whether to only run SQLite data tests
let sqliteOnly = (RethinkDbDataTests.env "SQLITE_ONLY" "0") = "1"

/// Whether to only run PostgreSQL data tests
let postgresOnly = (RethinkDbDataTests.env "PG_ONLY" "0") = "1"

/// Whether any of the data tests are being isolated
let dbOnly = rethinkOnly || sqliteOnly || postgresOnly

/// Whether to only run the unit tests (skip database/integration tests)
let unitOnly = (RethinkDbDataTests.env "UNIT_ONLY" "0") = "1"

let allTests = testList "MyWebLog" [
    if not dbOnly then testList "Domain" [ SupportTypesTests.all; DataTypesTests.all; ViewModelsTests.all ]
    if not unitOnly then
        testList "Data" [
            if not dbOnly then ConvertersTests.all
            if not dbOnly then UtilsTests.all
            if not dbOnly || (dbOnly && rethinkOnly)  then RethinkDbDataTests.all
            if not dbOnly || (dbOnly && sqliteOnly)   then SQLiteDataTests.all
            if not dbOnly || (dbOnly && postgresOnly) then PostgresDataTests.all
        ]
]

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args allTests
