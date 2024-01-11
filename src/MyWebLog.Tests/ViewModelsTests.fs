module ViewModelsTests

open System
open Expecto
open MyWebLog
open MyWebLog.ViewModels
open NodaTime

/// Unit tests for the addBaseToRelativeUrls helper function
let addBaseToRelativeUrlsTests =
    testList "PublicHelpers.addBaseToRelativeUrls" [
        test "succeeds when there is no extra URL path" {
            let testText = """<a href="/somewhere-else.html">Howdy></a>"""
            let modified = addBaseToRelativeUrls "" testText
            Expect.equal modified testText "The text should not have been modified"
        }
        test "succeeds with an extra URL path" {
            let testText =
                """<a href="/my-link.htm"><img src="/pretty-picture.jpg"></a><a href="https://example.com>link</a>"""
            let expected =
                """<a href="/a/b/my-link.htm"><img src="/a/b/pretty-picture.jpg"></a><a href="https://example.com>link</a>"""
            Expect.equal (addBaseToRelativeUrls "/a/b" testText) expected "Relative URLs not modified correctly"
        }
    ]

/// Unit tests for the DisplayCustomFeed type
let displayCustomFeedTests =
    testList "DisplayCustomFeed.FromFeed" [
        test "succeeds for a feed for an existing category" {
            let cats =
                [| { DisplayCategory.Id = "abc"
                     Slug        = "a-b-c"
                     Name        = "My Lovely Category"
                     Description = None
                     ParentNames = [||]
                     PostCount   = 3 } |]
            let feed =
                { CustomFeed.Empty with
                    Id     = CustomFeedId "test-feed"
                    Source = Category (CategoryId "abc")
                    Path   = Permalink "test-feed.xml" }
            let model = DisplayCustomFeed.FromFeed cats feed
            Expect.equal model.Id "test-feed" "Id not filled properly"
            Expect.equal model.Source "Category: My Lovely Category" "Source not filled properly"
            Expect.equal model.Path "test-feed.xml" "Path not filled properly"
            Expect.isFalse model.IsPodcast "IsPodcast not filled properly"
        }
        test "succeeds for a feed for a non-existing category" {
            let feed =
                { CustomFeed.Empty with
                    Id     = CustomFeedId "bad-feed"
                    Source = Category (CategoryId "xyz")
                    Path   = Permalink "trouble.xml" }
            let model = DisplayCustomFeed.FromFeed [||] feed
            Expect.equal model.Id "bad-feed" "Id not filled properly"
            Expect.equal model.Source "Category: --INVALID; DELETE THIS FEED--" "Source not filled properly"
            Expect.equal model.Path "trouble.xml" "Path not filled properly"
            Expect.isFalse model.IsPodcast "IsPodcast not filled properly"
        }
        test "succeeds for a feed for a tag" {
            let feed =
                { Id      = CustomFeedId "tag-feed"
                  Source  = Tag "testing"
                  Path    = Permalink "testing-posts.xml"
                  Podcast = Some PodcastOptions.Empty }
            let model = DisplayCustomFeed.FromFeed [||] feed
            Expect.equal model.Id "tag-feed" "Id not filled properly"
            Expect.equal model.Source "Tag: testing" "Source not filled properly"
            Expect.equal model.Path "testing-posts.xml" "Path not filled properly"
            Expect.isTrue model.IsPodcast "IsPodcast not filled properly"
        }
    ]

/// Unit tests for the DisplayPage type
let displayPageTests =
    testList "DisplayPage" [
        let page =
            { Page.Empty with
                Id          = PageId "my-page"
                AuthorId    = WebLogUserId "jim"
                Title       = "A Fine Example"
                Permalink   = Permalink "about/a-fine-example.html"
                PublishedOn = Noda.epoch
                UpdatedOn   = Noda.epoch + Duration.FromHours 1
                Text        = """<a href="/link.html">Click Me!</a>"""
                Metadata    = [ { Name = "unit"; Value = "test" } ] }
        testList "FromPageMinimal" [
            test "succeeds when page is default page" {
                let webLog = { WebLog.Empty with TimeZone = "Etc/GMT-1"; DefaultPage = "my-page" }
                let model = DisplayPage.FromPageMinimal webLog page
                Expect.equal model.Id "my-page" "Id not filled properly"
                Expect.equal model.AuthorId "jim" "AuthorId not filled properly"
                Expect.equal model.Title "A Fine Example" "Title not filled properly"
                Expect.equal model.Permalink "about/a-fine-example.html" "Permalink not filled properly"
                Expect.equal
                    model.PublishedOn
                    ((Noda.epoch + Duration.FromHours 1).ToDateTimeUtc())
                    "PublishedOn not filled properly"
                Expect.equal
                    model.UpdatedOn
                    ((Noda.epoch + Duration.FromHours 2).ToDateTimeUtc())
                    "UpdatedOn not filled properly"
                Expect.isFalse model.IsInPageList "IsInPageList should not have been set"
                Expect.isTrue model.IsDefault "IsDefault should have been set"
                Expect.equal model.Text "" "Text should have been blank"
                Expect.isEmpty model.Metadata "Metadata should have been empty"
            }
            test "succeeds when page is not the default page" {
                let model = DisplayPage.FromPageMinimal { WebLog.Empty with DefaultPage = "posts" } page
                Expect.isFalse model.IsDefault "IsDefault should not have been set"
            }
        ]
        testList "FromPage" [
            test "succeeds when the web log is on the domain root" {
                let webLog = { WebLog.Empty with TimeZone = "Etc/GMT-4"; UrlBase = "https://example.com" }
                let model = DisplayPage.FromPage webLog page
                Expect.equal model.Id "my-page" "Id not filled properly"
                Expect.equal model.AuthorId "jim" "AuthorId not filled properly"
                Expect.equal model.Title "A Fine Example" "Title not filled properly"
                Expect.equal model.Permalink "about/a-fine-example.html" "Permalink not filled properly"
                Expect.equal
                    model.PublishedOn
                    ((Noda.epoch + Duration.FromHours 4).ToDateTimeUtc())
                    "PublishedOn not filled properly"
                Expect.equal
                    model.UpdatedOn
                    ((Noda.epoch + Duration.FromHours 5).ToDateTimeUtc())
                    "UpdatedOn not filled properly"
                Expect.isFalse model.IsInPageList "IsInPageList should not have been set"
                Expect.isFalse model.IsDefault "IsDefault should not have been set"
                Expect.equal model.Text """<a href="/link.html">Click Me!</a>""" "Text not filled properly"
                Expect.equal model.Metadata.Length 1 "Metadata not filled properly"
            }
            test "succeeds when the web log is not on the domain root" {
                let model = DisplayPage.FromPage { WebLog.Empty with UrlBase = "https://example.com/a/b/c" } page
                Expect.equal model.Text """<a href="/a/b/c/link.html">Click Me!</a>""" "Text not filled properly"
            }
        ]
    ]

/// Unit tests for the DisplayRevision type
let displayRevisionTests =
    test "DisplayRevision.FromRevision succeeds" {
        let model =
            DisplayRevision.FromRevision
                { WebLog.Empty with TimeZone = "Etc/GMT+1" }
                { Text = Html "howdy"; AsOf = Noda.epoch }
        Expect.equal model.AsOf (Noda.epoch.ToDateTimeUtc()) "AsOf not filled properly"
        Expect.equal
            model.AsOfLocal ((Noda.epoch - Duration.FromHours 1).ToDateTimeUtc()) "AsOfLocal not filled properly"
        Expect.equal model.Format "HTML" "Format not filled properly"
    }

open System.IO

/// Unit tests for the DisplayTheme type
let displayThemeTests =
    testList "DisplayTheme.FromTheme" [
        let theme =
            { Id        = ThemeId "the-theme"
              Name      = "Test Theme"
              Version   = "v0.1.2"
              Templates = [ ThemeTemplate.Empty; ThemeTemplate.Empty ] }
        test "succeeds when theme is in use and not on disk" {
            let model =
                DisplayTheme.FromTheme
                    (fun it -> Expect.equal it (ThemeId "the-theme") "The theme ID not passed correctly"; true) theme
            Expect.equal model.Id "the-theme" "Id not filled properly"
            Expect.equal model.Name "Test Theme" "Name not filled properly"
            Expect.equal model.Version "v0.1.2" "Version not filled properly"
            Expect.equal model.TemplateCount 2 "TemplateCount not filled properly"
            Expect.isTrue model.IsInUse "IsInUse should have been set"
            Expect.isFalse model.IsOnDisk "IsOnDisk should not have been set"
        }
        test "succeeds when the theme is not in use as is on disk" {
            let file = File.Create "another-theme.zip"
            try
                let model = DisplayTheme.FromTheme (fun _ -> false) { theme with Id = ThemeId "another" }
                Expect.isFalse model.IsInUse "IsInUse should not have been set"
                Expect.isTrue model.IsOnDisk "IsOnDisk should have been set"
            finally
               file.Close()
               file.Dispose()
               File.Delete "another-theme.zip"
        }
    ]

/// Unit tests for the DisplayUpload type
let displayUploadTests =
    test "DisplayUpload.FromUpload succeeds" {
        let upload =
            { Upload.Empty with
                Id        = UploadId "test-up"
                Path      = Permalink "2022/04/my-pic.jpg"
                UpdatedOn = Noda.epoch }
        let model = DisplayUpload.FromUpload { WebLog.Empty with TimeZone = "Etc/GMT-1" } Database upload
        Expect.equal model.Id "test-up" "Id not filled properly"
        Expect.equal model.Name "my-pic.jpg" "Name not filled properly"
        Expect.equal model.Path "2022/04/" "Path not filled properly"
        Expect.equal model.Source "Database" "Source not filled properly"
        Expect.isSome model.UpdatedOn "There should have been an UpdatedOn value"
        Expect.equal
            model.UpdatedOn.Value ((Noda.epoch + Duration.FromHours 1).ToDateTimeUtc()) "UpdatedOn not filled properly"
    }

/// Unit tests for the DisplayUser type
let displayUserTests =
    testList "DisplayUser.FromUser" [
        let minimalUser =
            { WebLogUser.Empty with
                Id            = WebLogUserId "test-user"
                Email         = "jim.james@example.com"
                FirstName     = "Jim"
                LastName      = "James"
                PreferredName = "John"
                AccessLevel   = Editor
                CreatedOn     = Noda.epoch }
        test "succeeds when the user has minimal information" {
            let model = DisplayUser.FromUser WebLog.Empty minimalUser
            Expect.equal model.Id "test-user" "Id not filled properly"
            Expect.equal model.Email "jim.james@example.com" "Email not filled properly"
            Expect.equal model.FirstName "Jim" "FirstName not filled properly"
            Expect.equal model.LastName "James" "LastName not filled properly"
            Expect.equal model.PreferredName "John" "PreferredName not filled properly"
            Expect.equal model.Url "" "Url not filled properly"
            Expect.equal model.AccessLevel "Editor" "AccessLevel not filled properly"
            Expect.equal model.CreatedOn (Noda.epoch.ToDateTimeUtc()) "CreatedOn not filled properly"
            Expect.isFalse model.LastSeenOn.HasValue "LastSeenOn should have been null"
        }
        test "succeeds when the user has all information" {
            let model =
                DisplayUser.FromUser
                    { WebLog.Empty with TimeZone = "Etc/GMT-1" }
                    { minimalUser with
                        Url = Some "https://my.site"
                        LastSeenOn = Some (Noda.epoch + Duration.FromDays 4) } 
            Expect.equal model.Url "https://my.site" "Url not filled properly"
            Expect.equal
                model.CreatedOn ((Noda.epoch + Duration.FromHours 1).ToDateTimeUtc()) "CreatedOn not filled properly"
            Expect.isTrue model.LastSeenOn.HasValue "LastSeenOn should not have been null"
            Expect.equal
                model.LastSeenOn.Value
                ((Noda.epoch + Duration.FromDays 4 + Duration.FromHours 1).ToDateTimeUtc())
                "LastSeenOn not filled properly"
        }
    ]

/// Unit tests for the EditCategoryModel type
let editCategoryModelTests =
    testList "EditCategoryModel" [
        testList "FromCategory" [
            let minimalCat = { Category.Empty with Id = CategoryId "test-cat"; Name = "test"; Slug = "test-slug" }
            test "succeeds with minimal information" {
                let model = EditCategoryModel.FromCategory minimalCat
                Expect.equal model.CategoryId "test-cat" "CategoryId not filled properly"
                Expect.equal model.Name "test" "Name not filled properly"
                Expect.equal model.Slug "test-slug" "Slug not filled properly"
                Expect.equal model.Description "" "Description not filled properly"
                Expect.equal model.ParentId "" "ParentId not filled properly"
            }
            test "succeeds with complete information" {
                let model =
                    EditCategoryModel.FromCategory
                        { minimalCat with Description = Some "Testing"; ParentId = Some (CategoryId "parent") }
                Expect.equal model.Description "Testing" "Description not filled properly"
                Expect.equal model.ParentId "parent" "ParentId not filled properly"
            }
        ]
        testList "IsNew" [
            test "succeeds for a new category" {
                let model = EditCategoryModel.FromCategory { Category.Empty with Id = CategoryId "new" }
                Expect.isTrue model.IsNew "Category should have been considered new"
            }
            test "succeeds for a non-new category" {
                let model = EditCategoryModel.FromCategory Category.Empty
                Expect.isFalse model.IsNew "Category should not have been considered new"
            }
        ]
    ]

/// All tests for the Domain namespace
let all =
    testList
        "ViewModels"
        [ addBaseToRelativeUrlsTests
          displayCustomFeedTests
          displayPageTests
          displayRevisionTests
          displayThemeTests
          displayUploadTests
          displayUserTests
          editCategoryModelTests ]
