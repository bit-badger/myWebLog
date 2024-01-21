open Expecto

let allTests = testList "MyWebLog" [
    testList "Domain" [ SupportTypesTests.all; DataTypesTests.all; ViewModelsTests.all ]
    testList "Data" [ ConvertersTests.all; UtilsTests.all; SQLiteDataTests.all ]
]

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args allTests
