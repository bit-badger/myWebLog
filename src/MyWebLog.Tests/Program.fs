open Expecto

let allTests =
    testList
        "MyWebLog"
        [ Domain.all ]

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args allTests
