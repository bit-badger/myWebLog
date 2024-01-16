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

/// Unit tests for the EditPostModel type
let editPostModelTests = testList "EditPostModel" [
    let fullPost =
        { Post.Empty with
            Id          = PostId "a-post"
            Status      = Published
            Title       = "A Post"
            Permalink   = Permalink "1970/01/a-post.html"
            PublishedOn = Some (Noda.epoch + Duration.FromDays 7)
            UpdatedOn   = Noda.epoch + Duration.FromDays 365
            Template    = Some "demo"
            Text        = "<p>A post!</p>"
            CategoryIds = [ CategoryId "cat-a"; CategoryId "cat-b"; CategoryId "cat-n" ]
            Tags        = [ "demo"; "post" ]
            Metadata    = [ { Name = "A Meta"; Value = "A Value" } ]
            Revisions   =
                [ { AsOf = Noda.epoch + Duration.FromDays 365; Text = Html "<p>A post!</p>" }
                  { AsOf = Noda.epoch + Duration.FromDays 7;   Text = Markdown "A post!" } ]
            Episode     =
                Some { Media              = "a-post-ep.mp3"
                       Length             = 15555L
                       Duration           = Some (Duration.FromMinutes 15L + Duration.FromSeconds 22L)
                       MediaType          = Some "audio/mpeg3"
                       ImageUrl           = Some "uploads/podcast-cover.jpg"
                       Subtitle           = Some "Narration"
                       Explicit           = Some Clean
                       Chapters           = None // for future implementation 
                       ChapterFile        = Some "uploads/1970/01/chapters.txt"
                       ChapterType        = Some "chapters"
                       ChapterWaypoints   = Some true
                       TranscriptUrl      = Some "uploads/1970/01/transcript.txt"
                       TranscriptType     = Some "transcript"
                       TranscriptLang     = Some "EN-us"
                       TranscriptCaptions = Some true
                       SeasonNumber       = Some 3
                       SeasonDescription  = Some "Season Three"
                       EpisodeNumber      = Some 322.
                       EpisodeDescription = Some "Episode 322" } }
    testList "FromPost" [
        test "succeeds for empty post" {
            let model = EditPostModel.FromPost WebLog.Empty { Post.Empty with Id = PostId "lalala" }
            Expect.equal model.PostId "lalala" "PostId not filled properly"
            Expect.equal model.Title "" "Title not filled properly"
            Expect.equal model.Permalink "" "Permalink not filled properly"
            Expect.equal model.Source "HTML" "Source not filled properly"
            Expect.equal model.Text "" "Text not filled properly"
            Expect.equal model.Tags "" "Tags not filled properly"
            Expect.equal model.Template "" "Template not filled properly"
            Expect.isEmpty model.CategoryIds "CategoryIds not filled properly"
            Expect.equal model.Status (string Draft) "Status not filled properly"
            Expect.isFalse model.DoPublish "DoPublish should not have been set"
            Expect.equal model.MetaNames.Length 1 "MetaNames not filled properly"
            Expect.equal model.MetaNames[0] "" "Meta name 0 not filled properly"
            Expect.equal model.MetaValues.Length 1 "MetaValues not filled properly"
            Expect.equal model.MetaValues[0] "" "Meta value 0 not filled properly"
            Expect.isFalse model.SetPublished "SetPublished should not have been set"
            Expect.isFalse model.PubOverride.HasValue "PubOverride not filled properly"
            Expect.isFalse model.SetUpdated "SetUpdated should not have been set"
            Expect.isFalse model.IsEpisode "IsEpisode should not have been set"
            Expect.equal model.Media "" "Media not filled properly"
            Expect.equal model.Length 0L "Length not filled properly"
            Expect.equal model.Duration "" "Duration not filled properly"
            Expect.equal model.MediaType "" "MediaType not filled properly"
            Expect.equal model.ImageUrl "" "ImageUrl not filled properly"
            Expect.equal model.Subtitle "" "Subtitle not filled properly"
            Expect.equal model.Explicit "" "Explicit not filled properly"
            Expect.equal model.ChapterFile "" "ChapterFile not filled properly"
            Expect.equal model.ChapterType "" "ChapterType not filled properly"
            Expect.isFalse model.ContainsWaypoints "ContainsWaypoints should not have been set"
            Expect.equal model.TranscriptUrl "" "TranscriptUrl not filled properly"
            Expect.equal model.TranscriptType "" "TranscriptType not filled properly"
            Expect.equal model.TranscriptLang "" "TranscriptLang not filled properly"
            Expect.isFalse model.TranscriptCaptions "TranscriptCaptions should not have been set"
            Expect.equal model.SeasonNumber 0 "SeasonNumber not filled properly"
            Expect.equal model.SeasonDescription "" "SeasonDescription not filled properly"
            Expect.equal model.EpisodeNumber "" "EpisodeNumber not filled properly"
            Expect.equal model.EpisodeDescription "" "EpisodeDescription not filled properly"
        }
        test "succeeds for full post" {
            let model = EditPostModel.FromPost { WebLog.Empty with TimeZone = "Etc/GMT+1" } fullPost
            Expect.equal model.PostId "a-post" "PostId not filled properly"
            Expect.equal model.Title "A Post" "Title not filled properly"
            Expect.equal model.Permalink "1970/01/a-post.html" "Permalink not filled properly"
            Expect.equal model.Source "HTML" "Source not filled properly"
            Expect.equal model.Text "<p>A post!</p>" "Text not filled properly"
            Expect.equal model.Tags "demo, post" "Tags not filled properly"
            Expect.equal model.Template "demo" "Template not filled properly"
            Expect.equal model.CategoryIds [| "cat-a"; "cat-b"; "cat-n" |] "CategoryIds not filled properly"
            Expect.equal model.Status (string Published) "Status not filled properly"
            Expect.isFalse model.DoPublish "DoPublish should not have been set"
            Expect.equal model.MetaNames.Length 1 "MetaNames not filled properly"
            Expect.equal model.MetaNames[0] "A Meta" "Meta name 0 not filled properly"
            Expect.equal model.MetaValues.Length 1 "MetaValues not filled properly"
            Expect.equal model.MetaValues[0] "A Value" "Meta value 0 not filled properly"
            Expect.isFalse model.SetPublished "SetPublished should not have been set"
            Expect.isTrue model.PubOverride.HasValue "PubOverride should not have been null"
            Expect.equal
                model.PubOverride.Value
                ((Noda.epoch + Duration.FromDays 7 - Duration.FromHours 1).ToDateTimeUtc())
                "PubOverride not filled properly"
            Expect.isFalse model.SetUpdated "SetUpdated should not have been set"
            Expect.isTrue model.IsEpisode "IsEpisode should have been set"
            Expect.equal model.Media "a-post-ep.mp3" "Media not filled properly"
            Expect.equal model.Length 15555L "Length not filled properly"
            Expect.equal model.Duration "0:15:22" "Duration not filled properly"
            Expect.equal model.MediaType "audio/mpeg3" "MediaType not filled properly"
            Expect.equal model.ImageUrl "uploads/podcast-cover.jpg" "ImageUrl not filled properly"
            Expect.equal model.Subtitle "Narration" "Subtitle not filled properly"
            Expect.equal model.Explicit "clean" "Explicit not filled properly"
            Expect.equal model.ChapterFile "uploads/1970/01/chapters.txt" "ChapterFile not filled properly"
            Expect.equal model.ChapterType "chapters" "ChapterType not filled properly"
            Expect.isTrue model.ContainsWaypoints "ContainsWaypoints should have been set"
            Expect.equal model.TranscriptUrl "uploads/1970/01/transcript.txt" "TranscriptUrl not filled properly"
            Expect.equal model.TranscriptType "transcript" "TranscriptType not filled properly"
            Expect.equal model.TranscriptLang "EN-us" "TranscriptLang not filled properly"
            Expect.isTrue model.TranscriptCaptions "TranscriptCaptions should have been set"
            Expect.equal model.SeasonNumber 3 "SeasonNumber not filled properly"
            Expect.equal model.SeasonDescription "Season Three" "SeasonDescription not filled properly"
            Expect.equal model.EpisodeNumber "322" "EpisodeNumber not filled properly"
            Expect.equal model.EpisodeDescription "Episode 322" "EpisodeDescription not filled properly"
        }
    ]
    testList "IsNew" [
        test "succeeds for a new post" {
            Expect.isTrue
                (EditPostModel.FromPost WebLog.Empty { Post.Empty with Id = PostId "new" }).IsNew
                "IsNew should be set for new post"
        }
        test "succeeds for a not-new post" {
            Expect.isFalse
                (EditPostModel.FromPost WebLog.Empty { Post.Empty with Id = PostId "nu" }).IsNew
                "IsNew should not be set for not-new post"
        }
    ]
    let updatedModel =
        { EditPostModel.FromPost WebLog.Empty fullPost with
            Title              = "An Updated Post"
            Permalink          = "1970/01/updated-post.html"
            Source             = "HTML"
            Text               = "<p>An updated post!</p>"
            Tags               = "Zebras, Aardvarks, , Turkeys"
            Template           = "updated" 
            CategoryIds        = [| "cat-x"; "cat-y" |]
            MetaNames          = [| "Zed Meta"; "A Meta" |]
            MetaValues         = [| "A Value"; "Zed Value" |]
            Media              = "an-updated-ep.mp3"
            Length             = 14444L
            Duration           = "0:14:42"
            MediaType          = "audio/mp3"
            ImageUrl           = "updated-cover.png"
            Subtitle           = "Talking"
            Explicit           = "no"
            ChapterFile        = "updated-chapters.txt"
            ChapterType        = "indexes"
            TranscriptUrl      = "updated-transcript.txt"
            TranscriptType     = "subtitles"
            TranscriptLang     = "ES-mx"
            SeasonNumber       = 4
            SeasonDescription  = "Season Fo"
            EpisodeNumber      = "432.1" 
            EpisodeDescription = "Four Three Two pt One" }
    testList "UpdatePost" [
        test "succeeds for a full podcast episode" {
            let post = updatedModel.UpdatePost fullPost (Noda.epoch + Duration.FromDays 400)
            Expect.equal post.Title "An Updated Post" "Title not filled properly"
            Expect.equal post.Permalink (Permalink "1970/01/updated-post.html") "Permalink not filled properly"
            Expect.equal post.PriorPermalinks [ Permalink "1970/01/a-post.html" ] "PriorPermalinks not filled properly"
            Expect.equal post.PublishedOn fullPost.PublishedOn "PublishedOn should not have changed"
            Expect.equal post.UpdatedOn (Noda.epoch + Duration.FromDays 400) "UpdatedOn not filled properly"
            Expect.equal post.Text "<p>An updated post!</p>" "Text not filled properly"
            Expect.equal post.Tags [ "aardvarks"; "turkeys"; "zebras" ] "Tags not filled properly"
            Expect.equal post.Template (Some "updated") "Template not filled properly"
            Expect.equal post.CategoryIds [ CategoryId "cat-x"; CategoryId "cat-y" ] "Categories not filled properly"
            Expect.equal post.Metadata.Length 2 "There should have been 2 meta items"
            Expect.equal post.Metadata[0].Name "A Meta" "Meta item 0 name not filled properly"
            Expect.equal post.Metadata[0].Value "Zed Value" "Meta item 0 value not filled properly"
            Expect.equal post.Metadata[1].Name "Zed Meta" "Meta item 1 name not filled properly"
            Expect.equal post.Metadata[1].Value "A Value" "Meta item 1 value not filled properly"
            Expect.equal post.Revisions.Length 3 "There should have been 3 revisions"
            Expect.equal
                post.Revisions[0].AsOf (Noda.epoch + Duration.FromDays 400) "Revision 0 AsOf not filled properly"
            Expect.equal post.Revisions[0].Text (Html "<p>An updated post!</p>") "Revision 0 Text not filled properly"
            Expect.isSome post.Episode "There should have been a podcast episode"
            let ep = post.Episode.Value
            Expect.equal ep.Media "an-updated-ep.mp3" "Media not filled properly"
            Expect.equal ep.Length 14444L "Length not filled properly"
            Expect.equal
                ep.Duration (Some (Duration.FromMinutes 14L + Duration.FromSeconds 42L)) "Duration not filled properly"
            Expect.equal ep.MediaType (Some "audio/mp3") "MediaType not filled properly"
            Expect.equal ep.ImageUrl (Some "updated-cover.png") "ImageUrl not filled properly"
            Expect.equal ep.Subtitle (Some "Talking") "Subtitle not filled properly"
            Expect.equal ep.Explicit (Some No) "ExplicitRating not filled properly"
            Expect.equal ep.ChapterFile (Some "updated-chapters.txt") "ChapterFile not filled properly"
            Expect.equal ep.ChapterType (Some "indexes") "ChapterType not filled properly"
            Expect.equal ep.ChapterWaypoints (Some true) "ChapterWaypoints should have been set"
            Expect.equal ep.TranscriptUrl (Some "updated-transcript.txt") "TranscriptUrl not filled properly"
            Expect.equal ep.TranscriptType (Some "subtitles") "TranscriptType not filled properly"
            Expect.equal ep.TranscriptLang (Some "ES-mx") "TranscriptLang not filled properly"
            Expect.equal ep.TranscriptCaptions (Some true) "TranscriptCaptions should have been set"
            Expect.equal ep.SeasonNumber (Some 4) "SeasonNumber not filled properly"
            Expect.equal ep.SeasonDescription (Some "Season Fo") "SeasonDescription not filled properly"
            Expect.equal ep.EpisodeNumber (Some 432.1) "EpisodeNumber not filled properly"
            Expect.equal ep.EpisodeDescription (Some "Four Three Two pt One") "EpisodeDescription not filled properly"
        }
        test "succeeds for a minimal podcast episode" {
            let minModel =
                { updatedModel with
                    Duration           = ""
                    MediaType          = ""
                    ImageUrl           = ""
                    Subtitle           = ""
                    Explicit           = ""
                    ChapterFile        = ""
                    ChapterType        = ""
                    ContainsWaypoints  = false
                    TranscriptUrl      = ""
                    TranscriptType     = ""
                    TranscriptLang     = ""
                    TranscriptCaptions = false
                    SeasonNumber       = 0
                    SeasonDescription  = ""
                    EpisodeNumber      = ""
                    EpisodeDescription = "" }
            let post = minModel.UpdatePost fullPost (Noda.epoch + Duration.FromDays 500)
            Expect.isSome post.Episode "There should have been a podcast episode"
            let ep = post.Episode.Value
            Expect.equal ep.Media "an-updated-ep.mp3" "Media not filled properly"
            Expect.equal ep.Length 14444L "Length not filled properly"
            Expect.isNone ep.Duration "Duration not filled properly"
            Expect.isNone ep.MediaType "MediaType not filled properly"
            Expect.isNone ep.ImageUrl "ImageUrl not filled properly"
            Expect.isNone ep.Subtitle "Subtitle not filled properly"
            Expect.isNone ep.Explicit "ExplicitRating not filled properly"
            Expect.isNone ep.ChapterFile "ChapterFile not filled properly"
            Expect.isNone ep.ChapterType "ChapterType not filled properly"
            Expect.isNone ep.ChapterWaypoints "ChapterWaypoints should have been set"
            Expect.isNone ep.TranscriptUrl "TranscriptUrl not filled properly"
            Expect.isNone ep.TranscriptType "TranscriptType not filled properly"
            Expect.isNone ep.TranscriptLang "TranscriptLang not filled properly"
            Expect.isNone ep.TranscriptCaptions "TranscriptCaptions should have been set"
            Expect.isNone ep.SeasonNumber "SeasonNumber not filled properly"
            Expect.isNone ep.SeasonDescription "SeasonDescription not filled properly"
            Expect.isNone ep.EpisodeNumber "EpisodeNumber not filled properly"
            Expect.isNone ep.EpisodeDescription "EpisodeDescription not filled properly"
        }
        test "succeeds for no podcast episode and no template" {
            let post = { updatedModel with IsEpisode = false; Template = "" }.UpdatePost fullPost Noda.epoch
            Expect.isNone post.Template "Template not filled properly"
            Expect.isNone post.Episode "Episode not filled properly"
        }
        test "succeeds when publishing a draft" {
            let post =
                { updatedModel with DoPublish = true }.UpdatePost
                    { fullPost with Status = Draft } (Noda.epoch + Duration.FromDays 375)
            Expect.equal post.Status Published "Status not set properly"
            Expect.equal post.PublishedOn (Some (Noda.epoch + Duration.FromDays 375)) "PublishedOn not set properly"
        }
    ]
]

/// Unit tests for the EditRedirectRuleModel type
let editRedirectRuleModelTests = testList "EditRedirectRuleModel" [
    test "FromRule succeeds" {
        let model = EditRedirectRuleModel.FromRule 15 { From = "here"; To = "there"; IsRegex = true }
        Expect.equal model.RuleId 15 "RuleId not filled properly"
        Expect.equal model.From "here" "From not filled properly"
        Expect.equal model.To "there" "To not filled properly"
        Expect.isTrue model.IsRegex "IsRegex should have been set"
        Expect.isFalse model.InsertAtTop "InsertAtTop should not have been set"
    }
    test "ToRule succeeds" {
        let rule = { RuleId = 10; From = "me"; To = "you"; IsRegex = false; InsertAtTop = false }.ToRule()
        Expect.equal rule.From "me" "From not filled properly"
        Expect.equal rule.To "you" "To not filled properly"
        Expect.isFalse rule.IsRegex "IsRegex should not have been set"
    }
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
    editPostModelTests
    editRedirectRuleModelTests
]
