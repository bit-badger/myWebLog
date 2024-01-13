module ViewModelsTests

open System
open Expecto
open MyWebLog
open MyWebLog.ViewModels
open NodaTime

/// Unit tests for the addBaseToRelativeUrls helper function
let addBaseToRelativeUrlsTests = testList "PublicHelpers.addBaseToRelativeUrls" [
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
let displayCustomFeedTests = testList "DisplayCustomFeed.FromFeed" [
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
let displayPageTests = testList "DisplayPage" [
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
                model.UpdatedOn ((Noda.epoch + Duration.FromHours 2).ToDateTimeUtc()) "UpdatedOn not filled properly"
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
let displayRevisionTests = test "DisplayRevision.FromRevision succeeds" {
    let model =
        DisplayRevision.FromRevision
            { WebLog.Empty with TimeZone = "Etc/GMT+1" }
            { Text = Html "howdy"; AsOf = Noda.epoch }
    Expect.equal model.AsOf (Noda.epoch.ToDateTimeUtc()) "AsOf not filled properly"
    Expect.equal model.AsOfLocal ((Noda.epoch - Duration.FromHours 1).ToDateTimeUtc()) "AsOfLocal not filled properly"
    Expect.equal model.Format "HTML" "Format not filled properly"
}

open System.IO

/// Unit tests for the DisplayTheme type
let displayThemeTests = testList "DisplayTheme.FromTheme" [
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
let displayUploadTests = test "DisplayUpload.FromUpload succeeds" {
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
let displayUserTests = testList "DisplayUser.FromUser" [
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
let editCategoryModelTests = testList "EditCategoryModel" [
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

/// Unit tests for the EditCustomFeedModel type
let editCustomFeedModelTests = testList "EditCustomFeedModel" [
    let minimalPodcast =
        { PodcastOptions.Empty with
            Title           = "My Minimal Podcast"
            Summary         = "As little as possible"
            DisplayedAuthor = "The Tester"
            Email           = "thetester@example.com"
            ImageUrl        = Permalink "upload/my-image.png"
            AppleCategory   = "News"
            Explicit        = Clean }
    // A GUID with all zeroes, ending in "a"
    let aGuid =
        let guidBytes = Guid.Empty.ToByteArray()
        guidBytes[15] <- byte 10
        Guid guidBytes
    let fullPodcast =
        { minimalPodcast with
            Subtitle         = Some "A Podcast about Little"
            ItemsInFeed      = 17
            AppleSubcategory = Some "Analysis"
            DefaultMediaType = Some "video/mpeg4"
            MediaBaseUrl     = Some "a/b/c"
            PodcastGuid      = Some aGuid
            FundingUrl       = Some "https://pay.me"
            FundingText      = Some "Gimme Money!"
            Medium           = Some Newsletter }
    testList "FromFeed" [
        test "succeeds with no podcast" {
            let model =
                EditCustomFeedModel.FromFeed
                    { Id      = CustomFeedId "test-feed"
                      Source  = Category (CategoryId "no-podcast")
                      Path    = Permalink "no-podcast.xml"
                      Podcast = None }
            Expect.equal model.Id "test-feed" "Id not filled properly"
            Expect.equal model.SourceType "category" "SourceType not filled properly"
            Expect.equal model.SourceValue "no-podcast" "SourceValue not filled properly"
            Expect.equal model.Path "no-podcast.xml" "Path not filled properly"
            Expect.isFalse model.IsPodcast "IsPodcast should not have been set"
            Expect.equal model.Title "" "Title should be the default value"
            Expect.equal model.Subtitle "" "Subtitle should be the default value"
            Expect.equal model.ItemsInFeed 25 "ItemsInFeed should be the default value"
            Expect.equal model.Summary "" "Summary should be the default value"
            Expect.equal model.DisplayedAuthor "" "DisplayedAuthor should be the default value"
            Expect.equal model.Email "" "Email should be the default value"
            Expect.equal model.ImageUrl "" "ImageUrl should be the default value"
            Expect.equal model.AppleCategory "" "AppleCategory should be the default value"
            Expect.equal model.AppleSubcategory "" "AppleSubcategory should be the default value"
            Expect.equal model.Explicit "no" "Explicit should be the default value"
            Expect.equal model.DefaultMediaType "audio/mpeg" "DefaultMediaType should be the default value"
            Expect.equal model.MediaBaseUrl "" "MediaBaseUrl should be the default value"
            Expect.equal model.FundingUrl "" "FundingUrl should be the default value"
            Expect.equal model.FundingText "" "FundingText should be the default value"
            Expect.equal model.PodcastGuid "" "PodcastGuid should be the default value"
            Expect.equal model.Medium "" "Medium should be the default value"
        }
        test "succeeds with minimal podcast" {
            let model =
                EditCustomFeedModel.FromFeed
                    { Id      = CustomFeedId "minimal-feed"
                      Source  = Tag "min-podcast"
                      Path    = Permalink "min-podcast.xml"
                      Podcast = Some minimalPodcast }
            Expect.equal model.Id "minimal-feed" "Id not filled properly"
            Expect.equal model.SourceType "tag" "SourceType not filled properly"
            Expect.equal model.SourceValue "min-podcast" "SourceValue not filled properly"
            Expect.equal model.Path "min-podcast.xml" "Path not filled properly"
            Expect.isTrue model.IsPodcast "IsPodcast should have been set"
            Expect.equal model.Title "My Minimal Podcast" "Title not filled properly"
            Expect.equal model.Subtitle "" "Subtitle not filled properly (should be blank)"
            Expect.equal model.ItemsInFeed 0 "ItemsInFeed not filled properly"
            Expect.equal model.Summary "As little as possible" "Summary not filled properly"
            Expect.equal model.DisplayedAuthor "The Tester" "DisplayedAuthor not filled properly"
            Expect.equal model.Email "thetester@example.com" "Email not filled properly"
            Expect.equal model.ImageUrl "upload/my-image.png" "ImageUrl not filled properly"
            Expect.equal model.AppleCategory "News" "AppleCategory not filled properly"
            Expect.equal model.AppleSubcategory "" "AppleSubcategory not filled properly (should be blank)"
            Expect.equal model.Explicit "clean" "Explicit not filled properly"
            Expect.equal model.DefaultMediaType "" "DefaultMediaType not filled properly (should be blank)"
            Expect.equal model.MediaBaseUrl "" "MediaBaseUrl not filled properly (should be blank)"
            Expect.equal model.FundingUrl "" "FundingUrl not filled properly (should be blank)"
            Expect.equal model.FundingText "" "FundingText not filled properly (should be blank)"
            Expect.equal model.PodcastGuid "" "PodcastGuid not filled properly (should be blank)"
            Expect.equal model.Medium "" "Medium not filled properly (should be blank)"
        }
        test "succeeds with full podcast" {
            let model =
                EditCustomFeedModel.FromFeed
                    { Id      = CustomFeedId "full-feed"
                      Source  = Tag "whole-enchilada"
                      Path    = Permalink "full-podcast.xml"
                      Podcast = Some fullPodcast }
            Expect.equal model.Id "full-feed" "Id not filled properly"
            Expect.equal model.SourceType "tag" "SourceType not filled properly"
            Expect.equal model.SourceValue "whole-enchilada" "SourceValue not filled properly"
            Expect.equal model.Path "full-podcast.xml" "Path not filled properly"
            Expect.isTrue model.IsPodcast "IsPodcast should have been set"
            Expect.equal model.Title "My Minimal Podcast" "Title not filled properly"
            Expect.equal model.Subtitle "A Podcast about Little" "Subtitle not filled properly"
            Expect.equal model.ItemsInFeed 17 "ItemsInFeed not filled properly"
            Expect.equal model.Summary "As little as possible" "Summary not filled properly"
            Expect.equal model.DisplayedAuthor "The Tester" "DisplayedAuthor not filled properly"
            Expect.equal model.Email "thetester@example.com" "Email not filled properly"
            Expect.equal model.ImageUrl "upload/my-image.png" "ImageUrl not filled properly"
            Expect.equal model.AppleCategory "News" "AppleCategory not filled properly"
            Expect.equal model.AppleSubcategory "Analysis" "AppleSubcategory not filled properly"
            Expect.equal model.Explicit "clean" "Explicit not filled properly"
            Expect.equal model.DefaultMediaType "video/mpeg4" "DefaultMediaType not filled properly"
            Expect.equal model.MediaBaseUrl "a/b/c" "MediaBaseUrl not filled properly"
            Expect.equal model.FundingUrl "https://pay.me" "FundingUrl not filled properly"
            Expect.equal model.FundingText "Gimme Money!" "FundingText not filled properly"
            Expect.equal model.PodcastGuid "00000000-0000-0000-0000-00000000000a" "PodcastGuid not filled properly"
            Expect.equal model.Medium "newsletter" "Medium not filled properly"
        }
    ]
    testList "UpdateFeed" [
        test "succeeds with no podcast" {
            let model =
                { EditCustomFeedModel.Empty with SourceType = "tag"; SourceValue = "no-audio"; Path = "no-podcast.xml" }
            let feed =
                model.UpdateFeed
                    { CustomFeed.Empty with Id = CustomFeedId "no-podcast-feed"; Podcast = Some fullPodcast }
            Expect.equal feed.Id (CustomFeedId "no-podcast-feed") "Id not filled properly"
            Expect.equal feed.Source (Tag "no-audio") "Source not filled properly"
            Expect.equal feed.Path (Permalink "no-podcast.xml") "Path not filled properly"
            Expect.isNone feed.Podcast "Podcast not filled properly"
        }
        test "succeeds with minimal podcast" {
            let model = EditCustomFeedModel.FromFeed { CustomFeed.Empty with Podcast = Some minimalPodcast }
            let feed = model.UpdateFeed CustomFeed.Empty
            Expect.equal feed.Source (Category (CategoryId "")) "Source not filled properly"
            Expect.equal feed.Path (Permalink "") "Path not filled properly"
            Expect.isSome feed.Podcast "Podcast should be present"
            let podcast = feed.Podcast.Value
            Expect.equal podcast.Title "My Minimal Podcast" "Podcast title not filled properly"
            Expect.isNone podcast.Subtitle "Podcast subtitle not filled properly"
            Expect.equal podcast.ItemsInFeed 0 "Podcast items in feed not filled properly"
            Expect.equal podcast.Summary "As little as possible" "Podcast summary not filled properly"
            Expect.equal podcast.DisplayedAuthor "The Tester" "Podcast author not filled properly"
            Expect.equal podcast.Email "thetester@example.com" "Podcast email not filled properly"
            Expect.equal podcast.Explicit Clean "Podcast explicit rating not filled properly"
            Expect.equal podcast.AppleCategory "News" "Podcast Apple category not filled properly"
            Expect.isNone podcast.AppleSubcategory "Podcast Apple subcategory not filled properly"
            Expect.isNone podcast.DefaultMediaType "Podcast default media type not filled properly"
            Expect.isNone podcast.MediaBaseUrl "Podcast media base URL not filled properly"
            Expect.isNone podcast.PodcastGuid "Podcast GUID not filled properly"
            Expect.isNone podcast.FundingUrl "Podcast funding URL not filled properly"
            Expect.isNone podcast.FundingText "Podcast funding text not filled properly"
            Expect.isNone podcast.Medium "Podcast medium not filled properly"
        }
        test "succeeds with full podcast" {
            let model = EditCustomFeedModel.FromFeed { CustomFeed.Empty with Podcast = Some fullPodcast }
            let feed = model.UpdateFeed CustomFeed.Empty
            Expect.equal feed.Source (Category (CategoryId "")) "Source not filled properly"
            Expect.equal feed.Path (Permalink "") "Path not filled properly"
            Expect.isSome feed.Podcast "Podcast should be present"
            let podcast = feed.Podcast.Value
            Expect.equal podcast.Title "My Minimal Podcast" "Podcast title not filled properly"
            Expect.equal podcast.Subtitle (Some "A Podcast about Little") "Podcast subtitle not filled properly"
            Expect.equal podcast.ItemsInFeed 17 "Podcast items in feed not filled properly"
            Expect.equal podcast.Summary "As little as possible" "Podcast summary not filled properly"
            Expect.equal podcast.DisplayedAuthor "The Tester" "Podcast author not filled properly"
            Expect.equal podcast.Email "thetester@example.com" "Podcast email not filled properly"
            Expect.equal podcast.Explicit Clean "Podcast explicit rating not filled properly"
            Expect.equal podcast.AppleCategory "News" "Podcast Apple category not filled properly"
            Expect.equal podcast.AppleSubcategory (Some "Analysis") "Podcast Apple subcategory not filled properly"
            Expect.equal podcast.DefaultMediaType (Some "video/mpeg4") "Podcast default media type not filled properly"
            Expect.equal podcast.MediaBaseUrl (Some "a/b/c") "Podcast media base URL not filled properly"
            Expect.equal podcast.PodcastGuid (Some aGuid) "Podcast GUID not filled properly"
            Expect.equal podcast.FundingUrl (Some "https://pay.me") "Podcast funding URL not filled properly"
            Expect.equal podcast.FundingText (Some "Gimme Money!") "Podcast funding text not filled properly"
            Expect.equal podcast.Medium (Some Newsletter) "Podcast medium not filled properly"
        }
    ]
]

/// Unit tests for the EditMyInfoModel type
let editMyInfoModelTests = test "EditMyInfoModel.FromUser succeeds" {
    let model = EditMyInfoModel.FromUser { WebLogUser.Empty with FirstName = "A"; LastName = "B"; PreferredName = "C" }
    Expect.equal model.FirstName "A" "FirstName not filled properly"
    Expect.equal model.LastName "B" "LastName not filled properly"
    Expect.equal model.PreferredName "C" "PreferredName not filled properly"
    Expect.equal model.NewPassword "" "NewPassword not filled properly"
    Expect.equal model.NewPasswordConfirm "" "NewPasswordConfirm not filled properly"
}

let editPageModelTests = testList "EditPageModel" [
    let fullPage =
        { Page.Empty with
            Id           = PageId "the-page"
            Title        = "Test Page"
            Permalink    = Permalink "blog/page.html"
            Template     = Some "bork"
            IsInPageList = true
            Revisions    =
                [ { AsOf = Noda.epoch + Duration.FromHours 1; Text = Markdown "# Howdy!" }
                  { AsOf = Noda.epoch; Text = Html "<h1>howdy</h1>" } ]
            Metadata     = [ { Name = "Test"; Value = "me" }; { Name = "Two"; Value = "2" } ] }
    testList "FromPage" [
        test "succeeds for empty page" {
            let model = EditPageModel.FromPage { Page.Empty with Id = PageId "abc" }
            Expect.equal model.PageId "abc" "PageId not filled properly"
            Expect.equal model.Title "" "Title not filled properly"
            Expect.equal model.Permalink "" "Permalink not filled properly"
            Expect.equal model.Template "" "Template not filled properly"
            Expect.isFalse model.IsShownInPageList "IsShownInPageList should not have been set"
            Expect.equal model.Source "HTML" "Source not filled properly"
            Expect.equal model.Text "" "Text not set properly"
            Expect.equal model.MetaNames.Length 1 "MetaNames should have one entry"
            Expect.equal model.MetaNames[0] "" "Meta name not set properly"
            Expect.equal model.MetaValues.Length 1 "MetaValues should have one entry"
            Expect.equal model.MetaValues[0] "" "Meta value not set properly"
        }
        test "succeeds for filled page" {
            let model = EditPageModel.FromPage fullPage
            Expect.equal model.PageId "the-page" "PageId not filled properly"
            Expect.equal model.Title "Test Page" "Title not filled properly"
            Expect.equal model.Permalink "blog/page.html" "Permalink not filled properly"
            Expect.equal model.Template "bork" "Template not filled properly"
            Expect.isTrue model.IsShownInPageList "IsShownInPageList should have been set"
            Expect.equal model.Source "Markdown" "Source not filled properly"
            Expect.equal model.Text "# Howdy!" "Text not filled properly"
            Expect.equal model.MetaNames.Length 2 "MetaNames should have two entries"
            Expect.equal model.MetaNames[0] "Test" "Meta name 0 not set properly"
            Expect.equal model.MetaNames[1] "Two" "Meta name 1 not set properly"
            Expect.equal model.MetaValues.Length 2 "MetaValues should have two entries"
            Expect.equal model.MetaValues[0] "me" "Meta value 0 not set properly"
            Expect.equal model.MetaValues[1] "2" "Meta value 1 not set properly"
        }
    ]
    testList "IsNew" [
        test "succeeds for a new page" {
            Expect.isTrue
                (EditPageModel.FromPage { Page.Empty with Id = PageId "new" }).IsNew "IsNew should have been set"
        }
        test "succeeds for an existing page" {
            Expect.isFalse (EditPageModel.FromPage Page.Empty).IsNew "IsNew should not have been set"
        }
    ]
    testList "UpdatePage" [
        test "succeeds with minimal changes" {
            let model = { EditPageModel.FromPage fullPage with Title = "Updated Page"; IsShownInPageList = false }
            let page = model.UpdatePage fullPage (Noda.epoch + Duration.FromHours 4)
            Expect.equal page.Title "Updated Page" "Title not filled properly"
            Expect.equal page.Permalink (Permalink "blog/page.html") "Permalink not filled properly"
            Expect.isEmpty page.PriorPermalinks "PriorPermalinks should be empty"
            Expect.equal page.UpdatedOn (Noda.epoch + Duration.FromHours 4) "UpdatedOn not filled properly"
            Expect.isFalse page.IsInPageList "IsInPageList should have been unset"
            Expect.equal page.Template (Some "bork") "Template not filled properly"
            Expect.equal page.Text "<h1 id=\"howdy\">Howdy!</h1>\n" "Text not filled properly"
            Expect.equal page.Metadata.Length 2 "There should be 2 metadata items"
            let item1 = List.item 0 page.Metadata
            Expect.equal item1.Name "Test" "Meta item 0 name not filled properly"
            Expect.equal item1.Value "me" "Meta item 0 value not filled properly"
            let item2 = List.item 1 page.Metadata
            Expect.equal item2.Name "Two" "Meta item 1 name not filled properly"
            Expect.equal item2.Value "2" "Meta item 1 value not filled properly"
            Expect.equal page.Revisions.Length 2 "There should be 2 revisions"
            let rev1 = List.item 0 page.Revisions
            Expect.equal rev1.AsOf (Noda.epoch + Duration.FromHours 1) "Revision 0 as-of not filled properly"
            Expect.equal rev1.Text (Markdown "# Howdy!") "Revision 0 text not filled properly"
            let rev2 = List.item 1 page.Revisions
            Expect.equal rev2.AsOf Noda.epoch "Revision 1 as-of not filled properly"
            Expect.equal rev2.Text (Html "<h1>howdy</h1>") "Revision 1 text not filled properly"
        }
        test "succeeds with all changes" {
            let model =
                { PageId            = "this-page"
                  Title             = "My Updated Page"
                  Permalink         = "blog/updated.html"
                  Template          = ""
                  IsShownInPageList = false
                  Source            = "HTML"
                  Text              = "<h1>Howdy, partners!</h1>"
                  MetaNames         = [| "banana"; "apple"; "grape" |]
                  MetaValues        = [| "monkey"; "zebra"; "ape"   |] }
            let now = Noda.epoch + Duration.FromDays 7
            let page = model.UpdatePage fullPage now
            Expect.equal page.Title "My Updated Page" "Title not filled properly"
            Expect.equal page.Permalink (Permalink "blog/updated.html") "Permalink not filled properly"
            Expect.equal page.PriorPermalinks [ Permalink "blog/page.html" ] "PriorPermalinks not filled properly"
            Expect.equal page.UpdatedOn now "UpdatedOn not filled properly"
            Expect.isFalse page.IsInPageList "IsInPageList should not have been set"
            Expect.isNone page.Template "Template not filled properly"
            Expect.equal page.Text "<h1>Howdy, partners!</h1>" "Text not filled properly"
            Expect.equal page.Metadata.Length 3 "There should be 3 metadata items"
            let item1 = List.item 0 page.Metadata
            Expect.equal item1.Name "apple" "Meta item 0 name not filled properly"
            Expect.equal item1.Value "zebra" "Meta item 0 value not filled properly"
            let item2 = List.item 1 page.Metadata
            Expect.equal item2.Name "banana" "Meta item 1 name not filled properly"
            Expect.equal item2.Value "monkey" "Meta item 1 value not filled properly"
            let item3 = List.item 2 page.Metadata
            Expect.equal item3.Name "grape" "Meta item 2 name not filled properly"
            Expect.equal item3.Value "ape" "Meta item 2 value not filled properly"
            Expect.equal page.Revisions.Length 3 "There should be 3 revisions"
            Expect.equal page.Revisions.Head.AsOf now "Head revision as-of not filled properly"
            Expect.equal
                page.Revisions.Head.Text (Html "<h1>Howdy, partners!</h1>") "Head revision text not filled properly"
        }
    ]
]

/// All tests in the Domain.ViewModels file
let all = testList "ViewModels" [
    addBaseToRelativeUrlsTests
    displayCustomFeedTests
    displayPageTests
    displayRevisionTests
    displayThemeTests
    displayUploadTests
    displayUserTests
    editCategoryModelTests
    editCustomFeedModelTests
    editMyInfoModelTests
    editPageModelTests
]
