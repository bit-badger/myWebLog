module RethinkDbDataTests

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
let private dataCfg =
    DataConfig.FromUri (env "RETHINK_URI" "rethinkdb://172.17.0.2/mwl_test")

/// The active data instance to use for testing
let mutable private data: IData option = None

/// Dispose the existing data
let private disposeData () = task {
    if data.IsSome then
        let conn = (data.Value :?> RethinkDbData).Conn
        do! rethink { dbDrop dataCfg.Database; write; withRetryOnce; ignoreResult conn }
        conn.Dispose()
    data <- None
}

/// Create a new data implementation instance
let private newData () =
    let log  = NullLogger<RethinkDbData>()
    let conn = dataCfg.CreateConnection log
    RethinkDbData(conn, dataCfg, log)

/// Create a fresh environment from the root backup
let private freshEnvironment () = task {
    do! disposeData ()
    data <- Some (newData ())
    do! data.Value.StartUp()
    // This exercises Restore for all implementations; all tests are dependent on it working as expected
    do! Maintenance.Backup.restoreBackup "root-weblog.json" None false false data.Value
}

/// Set up the environment for the RethinkDB tests
let private environmentSetUp = testTask "creating database" {
    let _ = Json.configure Converter.Serializer
    do! freshEnvironment ()
}

/// Integration tests for the Category implementation in RethinkDB
let private categoryTests = testList "Category" [
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
let private pageTests = testList "Page" [
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
    testList "FindPageOfPages" [
        testTask "succeeds when pages are found" {
            do! PageDataTests.``FindPageOfPages succeeds when pages are found`` data.Value
        }
        testTask "succeeds when a pages are not found" {
            do! PageDataTests.``FindPageOfPages succeeds when pages are not found`` data.Value
        }
    ]
    testList "Update" [
        testTask "succeeds when the page exists" {
            do! PageDataTests.``Update succeeds when the page exists`` data.Value
        }
        testTask "succeeds when the page does not exist" {
            do! PageDataTests.``Update succeeds when the page does not exist`` data.Value
        }
    ]
    testList "UpdatePriorPermalinks" [
        testTask "succeeds when the page exists" {
            do! PageDataTests.``UpdatePriorPermalinks succeeds when the page exists`` data.Value
        }
        testTask "succeeds when the page does not exist" {
            do! PageDataTests.``UpdatePriorPermalinks succeeds when the page does not exist`` data.Value
        }
    ]
    testList "Delete" [
        testTask "succeeds when a page is deleted" {
            do! PageDataTests.``Delete succeeds when a page is deleted`` data.Value
        }
        testTask "succeeds when a page is not deleted" {
            do! PageDataTests.``Delete succeeds when a page is not deleted`` data.Value
        }
    ]
]

/// Integration tests for the Post implementation in RethinkDB
let private postTests = testList "Post" [
    testTask "Add succeeds" {
        // We'll need the root website categories restored for these tests
        do! freshEnvironment ()
        do! PostDataTests.``Add succeeds`` data.Value
    }
    testTask "CountByStatus succeeds" {
        do! PostDataTests.``CountByStatus succeeds`` data.Value
    }
    testList "FindById" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindById succeeds when a post is found`` data.Value
        }
        testTask "succeeds when a post is not found (incorrect weblog)" {
            do! PostDataTests.``FindById succeeds when a post is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when a post is not found (bad post ID)" {
            do! PostDataTests.``FindById succeeds when a post is not found (bad post ID)`` data.Value
        }
    ]
    testList "FindByPermalink" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindByPermalink succeeds when a post is found`` data.Value
        }
        testTask "succeeds when a post is not found (incorrect weblog)" {
            do! PostDataTests.``FindByPermalink succeeds when a post is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when a post is not found (no such permalink)" {
            do! PostDataTests.``FindByPermalink succeeds when a post is not found (no such permalink)`` data.Value
        }
    ]
    testList "FindCurrentPermalink" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindCurrentPermalink succeeds when a post is found`` data.Value
        }
        testTask "succeeds when a post is not found" {
            do! PostDataTests.``FindCurrentPermalink succeeds when a post is not found`` data.Value
        }
    ]
    testList "FindFullById" [
        testTask "succeeds when a post is found" {
            do! PostDataTests.``FindFullById succeeds when a post is found`` data.Value
        }
        testTask "succeeds when a post is not found" {
            do! PostDataTests.``FindFullById succeeds when a post is not found`` data.Value
        }
    ]
    testList "FindFullByWebLog" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindFullByWebLog succeeds when posts are found`` data.Value
        }
        testTask "succeeds when a posts are not found" {
            do! PostDataTests.``FindFullByWebLog succeeds when posts are not found`` data.Value
        }
    ]
    testList "FindPageOfCategorizedPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfCategorizedPosts succeeds when posts are found`` data.Value
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfCategorizedPosts succeeds when finding a too-high page number`` data.Value
        }
        testTask "succeeds when a category has no posts" {
            do! PostDataTests.``FindPageOfCategorizedPosts succeeds when a category has no posts`` data.Value
        }
    ]
    testList "FindPageOfPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfPosts succeeds when posts are found`` data.Value
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfPosts succeeds when finding a too-high page number`` data.Value
        }
        testTask "succeeds when there are no posts" {
            do! PostDataTests.``FindPageOfPosts succeeds when there are no posts`` data.Value
        }
    ]
    testList "FindPageOfPublishedPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfPublishedPosts succeeds when posts are found`` data.Value
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfPublishedPosts succeeds when finding a too-high page number`` data.Value
        }
        testTask "succeeds when there are no posts" {
            do! PostDataTests.``FindPageOfPublishedPosts succeeds when there are no posts`` data.Value
        }
    ]
    testList "FindPageOfTaggedPosts" [
        testTask "succeeds when posts are found" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when posts are found`` data.Value
        }
        testTask "succeeds when posts are found (excluding drafts)" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when posts are found (excluding drafts)`` data.Value
        }
        testTask "succeeds when finding a too-high page number" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when finding a too-high page number`` data.Value
        }
        testTask "succeeds when there are no posts" {
            do! PostDataTests.``FindPageOfTaggedPosts succeeds when there are no posts`` data.Value
        }
    ]
    testList "FindSurroundingPosts" [
        testTask "succeeds when there is no next newer post" {
            do! PostDataTests.``FindSurroundingPosts succeeds when there is no next newer post`` data.Value
        }
        testTask "succeeds when there is no next older post" {
            do! PostDataTests.``FindSurroundingPosts succeeds when there is no next older post`` data.Value
        }
        testTask "succeeds when older and newer exist" {
            do! PostDataTests.``FindSurroundingPosts succeeds when older and newer exist`` data.Value
        }
    ]
    testList "Update" [
        testTask "succeeds when the post exists" {
            do! PostDataTests.``Update succeeds when the post exists`` data.Value
        }
        testTask "succeeds when the post does not exist" {
            do! PostDataTests.``Update succeeds when the post does not exist`` data.Value
        }
    ]
    testList "UpdatePriorPermalinks" [
        testTask "succeeds when the post exists" {
            do! PostDataTests.``UpdatePriorPermalinks succeeds when the post exists`` data.Value
        }
        testTask "succeeds when the post does not exist" {
            do! PostDataTests.``UpdatePriorPermalinks succeeds when the post does not exist`` data.Value
        }
    ]
    testList "Delete" [
        testTask "succeeds when a post is deleted" {
            do! PostDataTests.``Delete succeeds when a post is deleted`` data.Value
        }
        testTask "succeeds when a post is not deleted" {
            do! PostDataTests.``Delete succeeds when a post is not deleted`` data.Value
        }
    ]
]

let private tagMapTests = testList "TagMap" [
    testList "FindById" [
        testTask "succeeds when a tag mapping is found" {
            do! TagMapDataTests.``FindById succeeds when a tag mapping is found`` data.Value
        }
        testTask "succeeds when a tag mapping is not found (incorrect weblog)" {
            do! TagMapDataTests.``FindById succeeds when a tag mapping is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when a tag mapping is not found (bad tag map ID)" {
            do! TagMapDataTests.``FindById succeeds when a tag mapping is not found (bad tag map ID)`` data.Value
        }
    ]
    testList "FindByUrlValue" [
        testTask "succeeds when a tag mapping is found" {
            do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is found`` data.Value
        }
        testTask "succeeds when a tag mapping is not found (incorrect weblog)" {
            do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is not found (incorrect weblog)``
                    data.Value
        }
        testTask "succeeds when a tag mapping is not found (no such value)" {
            do! TagMapDataTests.``FindByUrlValue succeeds when a tag mapping is not found (no such value)`` data.Value
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when tag mappings are found" {
            do! TagMapDataTests.``FindByWebLog succeeds when tag mappings are found`` data.Value
        }
        testTask "succeeds when no tag mappings are found" {
            do! TagMapDataTests.``FindByWebLog succeeds when no tag mappings are found`` data.Value
        }
    ]
    testList "FindMappingForTags" [
        testTask "succeeds when mappings exist" {
            do! TagMapDataTests.``FindMappingForTags succeeds when mappings exist`` data.Value
        }
        testTask "succeeds when no mappings exist" {
            do! TagMapDataTests.``FindMappingForTags succeeds when no mappings exist`` data.Value
        }
    ]
    testList "Save" [
        testTask "succeeds when adding a tag mapping" {
            do! TagMapDataTests.``Save succeeds when adding a tag mapping`` data.Value
        }
        testTask "succeeds when updating a tag mapping" {
            do! TagMapDataTests.``Save succeeds when updating a tag mapping`` data.Value
        }
    ]
    testList "Delete" [
        testTask "succeeds when a tag mapping is deleted" {
            do! TagMapDataTests.``Delete succeeds when a tag mapping is deleted`` data.Value
        }
        testTask "succeeds when a tag mapping is not deleted" {
            do! TagMapDataTests.``Delete succeeds when a tag mapping is not deleted`` data.Value
        }
    ]
]

let private themeTests = testList "Theme" [
    testTask "All succeeds" {
        do! ThemeDataTests.``All succeeds`` data.Value
    }
    testList "Exists" [
        testTask "succeeds when the theme exists" {
            do! ThemeDataTests.``Exists succeeds when the theme exists`` data.Value
        }
        testTask "succeeds when the theme does not exist" {
            do! ThemeDataTests.``Exists succeeds when the theme does not exist`` data.Value
        }
    ]
    testList "FindById" [
        testTask "succeeds when the theme exists" {
            do! ThemeDataTests.``FindById succeeds when the theme exists`` data.Value
        }
        testTask "succeeds when the theme does not exist" {
            do! ThemeDataTests.``FindById succeeds when the theme does not exist`` data.Value
        }
    ]
    testList "FindByIdWithoutText" [
        testTask "succeeds when the theme exists" {
            do! ThemeDataTests.``FindByIdWithoutText succeeds when the theme exists`` data.Value
        }
        testTask "succeeds when the theme does not exist" {
            do! ThemeDataTests.``FindByIdWithoutText succeeds when the theme does not exist`` data.Value
        }
    ]
    testList "Save" [
        testTask "succeeds when adding a theme" {
            do! ThemeDataTests.``Save succeeds when adding a theme`` data.Value
        }
        testTask "succeeds when updating a theme" {
            do! ThemeDataTests.``Save succeeds when updating a theme`` data.Value
        }
    ]
    testList "Delete" [
        testTask "succeeds when a theme is deleted" {
            do! ThemeDataTests.``Delete succeeds when a theme is deleted`` data.Value
        }
        testTask "succeeds when a theme is not deleted" {
            do! ThemeDataTests.``Delete succeeds when a theme is not deleted`` data.Value
        }
    ]
]

let private themeAssetTests = testList "ThemeAsset" [
    testList "Save" [
        testTask "succeeds when adding an asset" {
            do! ThemeDataTests.Asset.``Save succeeds when adding an asset`` data.Value
        }
        testTask "succeeds when updating an asset" {
            do! ThemeDataTests.Asset.``Save succeeds when updating an asset`` data.Value
        }
    ]
    testTask "All succeeds" {
        do! ThemeDataTests.Asset.``All succeeds`` data.Value
    }
    testList "FindById" [
        testTask "succeeds when an asset is found" {
            do! ThemeDataTests.Asset.``FindById succeeds when an asset is found`` data.Value
        }
        testTask "succeeds when an asset is not found" {
            do! ThemeDataTests.Asset.``FindById succeeds when an asset is not found`` data.Value
        }
    ]
    testList "FindByTheme" [
        testTask "succeeds when assets exist" {
            do! ThemeDataTests.Asset.``FindByTheme succeeds when assets exist`` data.Value
        }
        testTask "succeeds when assets do not exist" {
            do! ThemeDataTests.Asset.``FindByTheme succeeds when assets do not exist`` data.Value
        }
    ]
    testList "FindByThemeWithData" [
        testTask "succeeds when assets exist" {
            do! ThemeDataTests.Asset.``FindByThemeWithData succeeds when assets exist`` data.Value
        }
        testTask "succeeds when assets do not exist" {
            do! ThemeDataTests.Asset.``FindByThemeWithData succeeds when assets do not exist`` data.Value
        }
    ]
    testList "DeleteByTheme" [
        testTask "succeeds when assets are deleted" {
            do! ThemeDataTests.Asset.``DeleteByTheme succeeds when assets are deleted`` data.Value
        }
        testTask "succeeds when no assets are deleted" {
            do! ThemeDataTests.Asset.``DeleteByTheme succeeds when no assets are deleted`` data.Value
        }
    ]
]

let private uploadTests = testList "Upload" [
    testTask "Add succeeds" {
        do! UploadDataTests.``Add succeeds`` data.Value
    }
    testList "FindByPath" [
        testTask "succeeds when an upload is found" {
            do! UploadDataTests.``FindByPath succeeds when an upload is found`` data.Value
        }
        testTask "succeeds when an upload is not found (incorrect weblog)" {
            do! UploadDataTests.``FindByPath succeeds when an upload is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when an upload is not found (bad path)" {
            do! UploadDataTests.``FindByPath succeeds when an upload is not found (bad path)`` data.Value
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when uploads exist" {
            do! UploadDataTests.``FindByWebLog succeeds when uploads exist`` data.Value
        }
        testTask "succeeds when no uploads exist" {
            do! UploadDataTests.``FindByWebLog succeeds when no uploads exist`` data.Value
        }
    ]
    testList "FindByWebLogWithData" [
        testTask "succeeds when uploads exist" {
            do! UploadDataTests.``FindByWebLogWithData succeeds when uploads exist`` data.Value
        }
        testTask "succeeds when no uploads exist" {
            do! UploadDataTests.``FindByWebLogWithData succeeds when no uploads exist`` data.Value
        }
    ]
    testList "Delete" [
        testTask "succeeds when an upload is deleted" {
            do! UploadDataTests.``Delete succeeds when an upload is deleted`` data.Value
        }
        testTask "succeeds when an upload is not deleted" {
            do! UploadDataTests.``Delete succeeds when an upload is not deleted`` data.Value
        }
    ]
]

let private webLogUserTests = testList "WebLogUser" [
    testTask "Add succeeds" {
        // This restore ensures all the posts and pages exist
        do! freshEnvironment ()
        do! WebLogUserDataTests.``Add succeeds`` data.Value
    }
    testList "FindByEmail" [
        testTask "succeeds when a user is found" {
            do! WebLogUserDataTests.``FindByEmail succeeds when a user is found`` data.Value
        }
        testTask "succeeds when a user is not found (incorrect weblog)" {
            do! WebLogUserDataTests.``FindByEmail succeeds when a user is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when a user is not found (bad email)" {
            do! WebLogUserDataTests.``FindByEmail succeeds when a user is not found (bad email)`` data.Value
        }
    ]
    testList "FindById" [
        testTask "succeeds when a user is found" {
            do! WebLogUserDataTests.``FindById succeeds when a user is found`` data.Value
        }
        testTask "succeeds when a user is not found (incorrect weblog)" {
            do! WebLogUserDataTests.``FindById succeeds when a user is not found (incorrect weblog)`` data.Value
        }
        testTask "succeeds when a user is not found (bad ID)" {
            do! WebLogUserDataTests.``FindById succeeds when a user is not found (bad ID)`` data.Value
        }
    ]
    testList "FindByWebLog" [
        testTask "succeeds when users exist" {
            do! WebLogUserDataTests.``FindByWebLog succeeds when users exist`` data.Value
        }
        testTask "succeeds when no users exist" {
            do! WebLogUserDataTests.``FindByWebLog succeeds when no users exist`` data.Value
        }
    ]
    testList "FindNames" [
        testTask "succeeds when users exist" {
            do! WebLogUserDataTests.``FindNames succeeds when users exist`` data.Value
        }
        testTask "succeeds when users do not exist" {
            do! WebLogUserDataTests.``FindNames succeeds when users do not exist`` data.Value
        }
    ]
    testList "SetLastSeen" [
        testTask "succeeds when the user exists" {
            do! WebLogUserDataTests.``SetLastSeen succeeds when the user exists`` data.Value
        }
        testTask "succeeds when the user does not exist" {
            do! WebLogUserDataTests.``SetLastSeen succeeds when the user does not exist`` data.Value
        }
    ]
    testList "Update" [
        testTask "succeeds when the user exists" {
            do! WebLogUserDataTests.``Update succeeds when the user exists`` data.Value
        }
        testTask "succeeds when the user does not exist" {
            do! WebLogUserDataTests.``Update succeeds when the user does not exist`` data.Value
        }
    ]
    testList "Delete" [
        testTask "fails when the user is the author of a page" {
            do! WebLogUserDataTests.``Delete fails when the user is the author of a page`` data.Value
        }
        testTask "fails when the user is the author of a post" {
            do! WebLogUserDataTests.``Delete fails when the user is the author of a post`` data.Value
        }
        testTask "succeeds when the user is not an author" {
            do! WebLogUserDataTests.``Delete succeeds when the user is not an author`` data.Value
        }
        testTask "succeeds when the user does not exist" {
            do! WebLogUserDataTests.``Delete succeeds when the user does not exist`` data.Value
        }
    ]
]

/// Drop the throwaway RethinkDB database
let private environmentCleanUp = testTask "Clean Up" {
    do! disposeData ()
}

/// All RethinkDB data tests
let all =
    testList "RethinkDbData"
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
