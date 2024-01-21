module UtilsTests

open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// Unit tests for the orderByHierarchy function
let orderByHierarchyTests = test "orderByHierarchy succeeds" {
    let rawCats =
        [ { Category.Empty with Id = CategoryId "a"; Name = "Audio"; Slug = "audio"; ParentId = Some (CategoryId "p") }
          { Category.Empty with
              Id          = CategoryId "b"
              Name        = "Breaking"
              Description = Some "Breaking News" 
              Slug        = "breaking"
              ParentId    = Some (CategoryId "n") }
          { Category.Empty with Id = CategoryId "l"; Name = "Local"; Slug = "local"; ParentId = Some (CategoryId "b") }
          { Category.Empty with Id = CategoryId "n"; Name = "News"; Slug = "news" }
          { Category.Empty with Id = CategoryId "p"; Name = "Podcast"; Slug = "podcast" }
          { Category.Empty with Id = CategoryId "v"; Name = "Video"; Slug = "vid"; ParentId = Some (CategoryId "p") } ]
    let cats = Utils.orderByHierarchy rawCats None None [] |> List.ofSeq
    Expect.equal cats.Length 6 "There should have been 6 categories"
    Expect.equal cats[0].Id "n" "The first top-level category should have been News"
    Expect.equal cats[0].Slug "news" "Slug for News not filled properly"
    Expect.isEmpty cats[0].ParentNames "Parent names for News not filled properly"
    Expect.equal cats[1].Id "b" "Breaking should have been just below News"
    Expect.equal cats[1].Slug "news/breaking" "Slug for Breaking not filled properly"
    Expect.equal cats[1].Name "Breaking" "Name not filled properly"
    Expect.equal cats[1].Description (Some "Breaking News") "Description not filled properly"
    Expect.equal cats[1].ParentNames [| "News" |] "Parent names for Breaking not filled properly"
    Expect.equal cats[2].Id "l" "Local should have been just below Breaking"
    Expect.equal cats[2].Slug "news/breaking/local" "Slug for Local not filled properly"
    Expect.equal cats[2].ParentNames [| "News"; "Breaking" |] "Parent names for Local not filled properly"
    Expect.equal cats[3].Id "p" "Podcast should have been the next top-level category"
    Expect.equal cats[3].Slug "podcast" "Slug for Podcast not filled properly"
    Expect.isEmpty cats[3].ParentNames "Parent names for Podcast not filled properly"
    Expect.equal cats[4].Id "a" "Audio should have been just below Podcast"
    Expect.equal cats[4].Slug "podcast/audio" "Slug for Audio not filled properly"
    Expect.equal cats[4].ParentNames [| "Podcast" |] "Parent names for Audio not filled properly"
    Expect.equal cats[5].Id "v" "Video should have been below Audio"
    Expect.equal cats[5].Slug "podcast/vid" "Slug for Video not filled properly"
    Expect.equal cats[5].ParentNames [| "Podcast" |] "Parent names for Video not filled properly"
    Expect.hasCountOf cats 6u (fun it -> it.PostCount = 0) "All post counts should have been 0"
}

/// Unit tests for the diffLists function
let diffListsTests = testList "diffLists" [
    test "succeeds with identical lists" {
        let removed, added = Utils.diffLists [ 1; 2; 3 ] [ 1; 2; 3 ] id
        Expect.isEmpty removed "There should have been no removed items returned"
        Expect.isEmpty added "There should have been no added items returned"
    }
    test "succeeds with differing lists" {
        let removed, added = Utils.diffLists [ 1; 2; 3 ] [ 3; 4; 5 ] string
        Expect.equal removed [ 1; 2 ] "Removed items incorrect"
        Expect.equal added [ 4; 5 ] "Added items incorrect"
    }
]

/// Unit tests for the diffRevisions function
let diffRevisionsTests = testList "diffRevisions" [
    test "succeeds with identical lists" {
        let oldItems =
            [ { AsOf = Noda.epoch + Duration.FromDays 3; Text = Html "<p>test" }
              { AsOf = Noda.epoch; Text = Html "<p>test test" } ]
        let newItems =
            [ { AsOf = Noda.epoch; Text = Html "<p>test test" }
              { AsOf = Noda.epoch + Duration.FromDays 3; Text = Html "<p>test" } ]
        let removed, added = Utils.diffRevisions oldItems newItems
        Expect.isEmpty removed "There should have been no removed items returned"
        Expect.isEmpty added "There should have been no added items returned"
    }
    test "succeeds with differing lists" {
        let oldItems =
            [ { AsOf = Noda.epoch + Duration.FromDays 3; Text = Html "<p>test" }
              { AsOf = Noda.epoch + Duration.FromDays 2; Text = Html "<p>tests" }
              { AsOf = Noda.epoch; Text = Html "<p>test test" } ]
        let newItems =
            [ { AsOf = Noda.epoch + Duration.FromDays 4; Text = Html "<p>tests" }
              { AsOf = Noda.epoch + Duration.FromDays 3; Text = Html "<p>test" }
              { AsOf = Noda.epoch; Text = Html "<p>test test" } ]
        let removed, added = Utils.diffRevisions oldItems newItems
        Expect.equal removed.Length 1 "There should be 1 removed item"
        Expect.equal removed[0].AsOf (Noda.epoch + Duration.FromDays 2) "Expected removed item incorrect"
        Expect.equal added.Length 1 "There should be 1 added item"
        Expect.equal added[0].AsOf (Noda.epoch + Duration.FromDays 4) "Expected added item incorrect"
    }
]

/// All tests for the Utils file
let all = testList "Utils" [
    orderByHierarchyTests
    diffListsTests
    diffRevisionsTests
]
