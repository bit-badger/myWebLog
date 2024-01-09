module Domain

open System
open Expecto
open MyWebLog
open NodaTime

// --- SUPPORT TYPES ---

/// Tests for the NodaTime-wrapping module
let nodaTests =
    testList "Noda" [
        test "epoch succeeds" {
            Expect.equal
                (Noda.epoch.ToDateTimeUtc())
                (DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                "The Unix epoch value is not correct"
        }
        test "toSecondsPrecision succeeds" {
            let testDate = Instant.FromDateTimeUtc(DateTime(1970, 1, 1, 0, 0, 0, 444, DateTimeKind.Utc))
            // testDate.
            Expect.equal
                ((Noda.toSecondsPrecision testDate).ToDateTimeUtc())
                (Noda.epoch.ToDateTimeUtc())
                "Instant value was not rounded to seconds precision"
        }
        test "fromDateTime succeeds" {
            let testDate = DateTime(1970, 1, 1, 0, 0, 0, 444, DateTimeKind.Utc)
            Expect.equal (Noda.fromDateTime testDate) Noda.epoch "fromDateTime did not truncate to seconds"
        }
    ]

/// Tests for the AccessLevel type
let accessLevelTests =
    testList "AccessLevel" [
        testList "Parse" [
            test "succeeds for \"Author\"" {
                Expect.equal Author (AccessLevel.Parse "Author") "Author not parsed correctly"
            }
            test "succeeds for \"Editor\"" {
                Expect.equal Editor (AccessLevel.Parse "Editor") "Editor not parsed correctly"
            }
            test "succeeds for \"WebLogAdmin\"" {
                Expect.equal WebLogAdmin (AccessLevel.Parse "WebLogAdmin") "WebLogAdmin not parsed correctly"
            }
            test "succeeds for \"Administrator\"" {
                Expect.equal Administrator (AccessLevel.Parse "Administrator") "Administrator not parsed correctly"
            }
            test "fails when given an unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (AccessLevel.Parse "Hacker")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "Author succeeds" {
                Expect.equal (string Author) "Author" "Author string incorrect"
            }
            test "Editor succeeds" {
                Expect.equal (string Editor) "Editor" "Editor string incorrect"
            }
            test "WebLogAdmin succeeds" {
                Expect.equal (string WebLogAdmin) "WebLogAdmin" "WebLogAdmin string incorrect"
            }
            test "Administrator succeeds" {
                Expect.equal (string Administrator) "Administrator" "Administrator string incorrect"
            }
        ]
        testList "HasAccess" [
            test "Author has Author access" {
                Expect.isTrue (Author.HasAccess Author) "Author should have Author access"
            }
            test "Author does not have Editor access" {
                Expect.isFalse (Author.HasAccess Editor) "Author should not have Editor access"
            }
            test "Author does not have WebLogAdmin access" {
                Expect.isFalse (Author.HasAccess WebLogAdmin) "Author should not have WebLogAdmin access"
            }
            test "Author does not have Administrator access" {
                Expect.isFalse (Author.HasAccess Administrator) "Author should not have Administrator access"
            }
            test "Editor has Author access" {
                Expect.isTrue (Editor.HasAccess Author) "Editor should have Author access"
            }
            test "Editor has Editor access" {
                Expect.isTrue (Editor.HasAccess Editor) "Editor should have Editor access"
            }
            test "Editor does not have WebLogAdmin access" {
                Expect.isFalse (Editor.HasAccess WebLogAdmin) "Editor should not have WebLogAdmin access"
            }
            test "Editor does not have Administrator access" {
                Expect.isFalse (Editor.HasAccess Administrator) "Editor should not have Administrator access"
            }
            test "WebLogAdmin has Author access" {
                Expect.isTrue (WebLogAdmin.HasAccess Author) "WebLogAdmin should have Author access"
            }
            test "WebLogAdmin has Editor access" {
                Expect.isTrue (WebLogAdmin.HasAccess Editor) "WebLogAdmin should have Editor access"
            }
            test "WebLogAdmin has WebLogAdmin access" {
                Expect.isTrue (WebLogAdmin.HasAccess WebLogAdmin) "WebLogAdmin should have WebLogAdmin access"
            }
            test "WebLogAdmin does not have Administrator access" {
                Expect.isFalse (WebLogAdmin.HasAccess Administrator) "WebLogAdmin should not have Administrator access"
            }
            test "Administrator has Author access" {
                Expect.isTrue (Administrator.HasAccess Author) "Administrator should have Author access"
            }
            test "Administrator has Editor access" {
                Expect.isTrue (Administrator.HasAccess Editor) "Administrator should have Editor access"
            }
            test "Administrator has WebLogAdmin access" {
                Expect.isTrue (Administrator.HasAccess WebLogAdmin) "Administrator should have WebLogAdmin access"
            }
            test "Administrator has Administrator access" {
                Expect.isTrue (Administrator.HasAccess Administrator) "Administrator should have Administrator access"
            }
        ]
    ]

/// Tests for the CommentStatus type
let commentStatusTests =
    testList "CommentStatus" [
        testList "Parse" [
            test "succeeds for \"Approved\"" {
                Expect.equal Approved (CommentStatus.Parse "Approved") "Approved not parsed correctly"
            }
            test "succeeds for \"Pending\"" {
                Expect.equal Pending (CommentStatus.Parse "Pending") "Pending not parsed correctly"
            }
            test "succeeds for \"Spam\"" {
                Expect.equal Spam (CommentStatus.Parse "Spam") "Spam not parsed correctly"
            }
            test "fails for unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (CommentStatus.Parse "Live")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "Approved succeeds" {
                Expect.equal (string Approved) "Approved" "Approved string incorrect"
            }
            test "Pending succeeds" {
                Expect.equal (string Pending) "Pending" "Pending string incorrect"
            }
            test "Spam succeeds" {
                Expect.equal (string Spam) "Spam" "Spam string incorrect"
            }
        ]
    ]

let explicitRatingTests =
    testList "ExplicitRating" [
        testList "Parse" [
            test "succeeds for \"yes\"" {
                Expect.equal Yes (ExplicitRating.Parse "yes") "\"yes\" not parsed correctly"
            }
            test "succeeds for \"no\"" {
                Expect.equal No (ExplicitRating.Parse "no") "\"no\" not parsed correctly"
            }
            test "succeeds for \"clean\"" {
                Expect.equal Clean (ExplicitRating.Parse "clean") "\"clean\" not parsed correctly"
            }
            test "fails for unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (ExplicitRating.Parse "maybe")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "Yes succeeds" {
                Expect.equal (string Yes) "yes" "Yes string incorrect"
            }
            test "No succeeds" {
                Expect.equal (string No) "no" "No string incorrect"
            }
            test "Clean succeeds" {
                Expect.equal (string Clean) "clean" "Clean string incorrect"
            }
        ]
    ]

/// Tests for the Episode type
let episodeTests =
    testList "Episode" [
        testList "FormatDuration" [
            test "succeeds when no duration is specified" {
                Expect.isNone (Episode.Empty.FormatDuration()) "A missing duration should have returned None"
            }
            test "succeeds when duration is specified" {
                Expect.equal
                    ({ Episode.Empty with
                        Duration = Some (Duration.FromMinutes 3L + Duration.FromSeconds 13L) }.FormatDuration())
                    (Some "0:03:13")
                    "Duration not formatted correctly"
            }
            test "succeeds when duration is > 10 hours" {
                Expect.equal
                    ({ Episode.Empty with Duration = Some (Duration.FromHours 11) }.FormatDuration())
                    (Some "11:00:00")
                    "Duration not formatted correctly"
            }
        ]
    ]

/// Unit tests for the MarkupText type
let markupTextTests =
    testList "MarkupText" [
        testList "Parse" [
            test "succeeds with HTML content" {
                let txt = MarkupText.Parse "HTML: <p>howdy</p>"
                match txt with
                | Html it when it = "<p>howdy</p>" -> ()
                | _ -> Expect.isTrue false $"Unexpected parse result for HTML: %A{txt}"
            }
            test "succeeds with Markdown content" {
                let txt = MarkupText.Parse "Markdown: # A Title"
                match txt with
                | Markdown it when it = "# A Title" -> ()
                | _ -> Expect.isTrue false $"Unexpected parse result for Markdown: %A{txt}"
            }
            test "fails with unexpected content" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (MarkupText.Parse "LaTEX: nope")) "Invalid value should have raised an exception"
            }
        ]
        testList "SourceType" [
            test "succeeds for HTML" {
                Expect.equal (MarkupText.Parse "HTML: something").SourceType "HTML" "HTML source type incorrect"
            }
            test "succeeds for Markdown" {
                Expect.equal (MarkupText.Parse "Markdown: blah").SourceType "Markdown" "Markdown source type incorrect"
            }
        ]
        testList "Text" [
            test "succeeds for HTML" {
                Expect.equal (MarkupText.Parse "HTML: test").Text "test" "HTML text incorrect"
            }
            test "succeeds for Markdown" {
                Expect.equal (MarkupText.Parse "Markdown: test!").Text "test!" "Markdown text incorrect"
            }
        ]
        testList "ToString" [
            test "succeeds for HTML" {
                Expect.equal
                    (string (MarkupText.Parse "HTML: <h1>HTML</h1>"))
                    "HTML: <h1>HTML</h1>"
                    "HTML string value incorrect"
            }
            test "succeeds for Markdown" {
                Expect.equal
                    (string (MarkupText.Parse "Markdown: # Some Content"))
                    "Markdown: # Some Content"
                    "Markdown string value incorrect"
            }
        ]
        testList "AsHtml" [
            test "succeeds for HTML" {
                Expect.equal
                    ((MarkupText.Parse "HTML: <h1>The Heading</h1>").AsHtml())
                    "<h1>The Heading</h1>"
                    "HTML value incorrect"
            }
            test "succeeds for Markdown" {
                Expect.equal
                    ((MarkupText.Parse "Markdown: *emphasis*").AsHtml())
                    "<p><em>emphasis</em></p>\n"
                    "Markdown HTML value incorrect"
            }
        ]
    ]

/// Unit tests for the PodcastMedium type
let podcastMediumTests =
    testList "PodcastMedium" [
        testList "Parse" [
            test "succeeds for \"podcast\"" {
                Expect.equal (PodcastMedium.Parse "podcast") Podcast "\"podcast\" not parsed correctly"
            }
            test "succeeds for \"music\"" {
                Expect.equal (PodcastMedium.Parse "music") Music "\"music\" not parsed correctly"
            }
            test "succeeds for \"video\"" {
                Expect.equal (PodcastMedium.Parse "video") Video "\"video\" not parsed correctly"
            }
            test "succeeds for \"film\"" {
                Expect.equal (PodcastMedium.Parse "film") Film "\"film\" not parsed correctly"
            }
            test "succeeds for \"audiobook\"" {
                Expect.equal (PodcastMedium.Parse "audiobook") Audiobook "\"audiobook\" not parsed correctly"
            }
            test "succeeds for \"newsletter\"" {
                Expect.equal (PodcastMedium.Parse "newsletter") Newsletter "\"newsletter\" not parsed correctly"
            }
            test "succeeds for \"blog\"" {
                Expect.equal (PodcastMedium.Parse "blog") Blog "\"blog\" not parsed correctly"
            }
            test "fails for invalid type" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (PodcastMedium.Parse "laser")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "succeeds for Podcast" {
                Expect.equal (string Podcast) "podcast" "Podcast string incorrect"
            }
            test "succeeds for Music" {
                Expect.equal (string Music) "music" "Music string incorrect"
            }
            test "succeeds for Video" {
                Expect.equal (string Video) "video" "Video string incorrect"
            }
            test "succeeds for Film" {
                Expect.equal (string Film) "film" "Film string incorrect"
            }
            test "succeeds for Audiobook" {
                Expect.equal (string Audiobook) "audiobook" "Audiobook string incorrect"
            }
            test "succeeds for Newsletter" {
                Expect.equal (string Newsletter) "newsletter" "Newsletter string incorrect"
            }
            test "succeeds for Blog" {
                Expect.equal (string Blog) "blog" "Blog string incorrect"
            }
        ]
    ]

/// Unit tests for the PostStatus type
let postStatusTests =
    testList "PostStatus" [
        testList "Parse" [
            test "succeeds for \"Draft\"" {
                Expect.equal (PostStatus.Parse "Draft") Draft "\"Draft\" not parsed correctly"
            }
            test "succeeds for \"Published\"" {
                Expect.equal (PostStatus.Parse "Published") Published "\"Published\" not parsed correctly"
            }
            test "fails for unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (PostStatus.Parse "Rescinded")) "Invalid value should have raised an exception"
            }
        ]
    ]

/// Unit tests for the CustomFeedSource type
let customFeedSourceTests =
    testList "CustomFeedSource" [
        testList "Parse" [
            test "succeeds for category feeds" {
                Expect.equal
                    (CustomFeedSource.Parse "category:abc123")
                    (Category (CategoryId "abc123"))
                    "Category feed not parsed correctly"
            }
            test "succeeds for tag feeds" {
                Expect.equal (CustomFeedSource.Parse "tag:turtles") (Tag "turtles") "Tag feed not parsed correctly"
            }
            test "fails for unknown type" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (CustomFeedSource.Parse "nasa:sat1"))
                    "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "succeeds for category feed" {
                Expect.equal
                    (string (CustomFeedSource.Parse "category:fish")) "category:fish" "Category feed string incorrect"
            }
            test "succeeds for tag feed" {
                Expect.equal (string (CustomFeedSource.Parse "tag:rocks")) "tag:rocks" "Tag feed string incorrect"
            }
        ]
    ]

/// Unit tests for the ThemeAssetId type
let themeAssetIdTests =
    testList "ThemeAssetId" [
        testList "Parse" [
            test "succeeds with expected values" {
                Expect.equal
                    (ThemeAssetId.Parse "test-theme/the-asset")
                    (ThemeAssetId ((ThemeId "test-theme"), "the-asset"))
                    "Theme asset ID not parsed correctly"
            }
            test "fails if no slash is present" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (ThemeAssetId.Parse "my-theme-asset"))
                    "Invalid value should have raised an exception"
            }
        ]
        test "ToString succeeds" {
            Expect.equal
                (string (ThemeAssetId ((ThemeId "howdy"), "pardner"))) "howdy/pardner" "Theme asset ID string incorrect"
        }
    ]

/// Unit tests for the UploadDestination type
let uploadDestinationTests =
    testList "UploadDestination" [
        testList "Parse" [
            test "succeeds for \"Database\"" {
                Expect.equal (UploadDestination.Parse "Database") Database "\"Database\" not parsed correctly"
            }
            test "succeeds for \"Disk\"" {
                Expect.equal (UploadDestination.Parse "Disk") Disk "\"Disk\" not parsed correctly"
            }
            test "fails for unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (UploadDestination.Parse "Azure")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "succeeds for Database" {
                Expect.equal (string Database) "Database" "Database string incorrect"
            }
            test "succeeds for Disk" {
                Expect.equal (string Disk) "Disk" "Disk string incorrect"
            }
        ]
    ]

// --- DATA TYPES ---

/// Unit tests for the WebLog type
let webLogTests =
    testList "WebLog" [
        testList "ExtraPath" [
            test "succeeds for blank URL base" {
                Expect.equal WebLog.Empty.ExtraPath "" "Extra path should have been blank for blank URL base"
            }
            test "succeeds for domain root URL" {
                Expect.equal
                    { WebLog.Empty with UrlBase = "https://example.com" }.ExtraPath
                    ""
                    "Extra path should have been blank for domain root"
            }
            test "succeeds for single subdirectory" {
                Expect.equal
                    { WebLog.Empty with UrlBase = "http://a.com/subdir" }.ExtraPath
                    "/subdir"
                    "Extra path incorrect for a single subdirectory"
            }
            test "succeeds for deeper nesting" {
                Expect.equal
                    { WebLog.Empty with UrlBase = "http://b.com/users/test/units" }.ExtraPath
                    "/users/test/units"
                    "Extra path incorrect for deeper nesting"
            }
        ]
        test "AbsoluteUrl succeeds" {
            Expect.equal
                ({ WebLog.Empty with UrlBase = "http://my.site" }.AbsoluteUrl(Permalink "blog/page.html"))
                "http://my.site/blog/page.html"
                "Absolute URL is incorrect"
        }
        testList "RelativeUrl" [
            test "succeeds for domain root URL" {
                Expect.equal
                    ({ WebLog.Empty with UrlBase = "http://test.me" }.RelativeUrl(Permalink "about.htm"))
                    "/about.htm"
                    "Relative URL is incorrect for domain root site"
            }
            test "succeeds for domain non-root URL" {
                Expect.equal
                    ({ WebLog.Empty with UrlBase = "http://site.page/a/b/c" }.RelativeUrl(Permalink "x/y/z"))
                    "/a/b/c/x/y/z"
                    "Relative URL is incorrect for domain non-root site"
            }
        ]
        testList "LocalTime" [
            test "succeeds when no time zone is set" {
                Expect.equal
                    (WebLog.Empty.LocalTime(Noda.epoch))
                    (Noda.epoch.ToDateTimeUtc())
                    "Reference should be UTC when no time zone is specified"
            }
            test "succeeds when time zone is set" {
                Expect.equal
                    ({ WebLog.Empty with TimeZone = "Etc/GMT-1" }.LocalTime(Noda.epoch))
                    (Noda.epoch.ToDateTimeUtc().AddHours 1)
                    "The time should have been adjusted by one hour"
            }
        ]
    ]

/// Unit tests for the WebLogUser type
let webLogUserTests =
    testList "WebLogUser" [
        testList "DisplayName" [
            test "succeeds when a preferred name is present" {
                Expect.equal
                    { WebLogUser.Empty with
                        FirstName = "Thomas"; PreferredName = "Tom"; LastName = "Tester" }.DisplayName
                    "Tom Tester"
                    "Display name incorrect when preferred name is present"
            }
            test "succeeds when a preferred name is absent" {
                Expect.equal
                    { WebLogUser.Empty with FirstName = "Test"; LastName = "Units" }.DisplayName
                    "Test Units"
                    "Display name incorrect when preferred name is absent"
            }
        ]
    ]

// --- VIEW MODELS ---

open MyWebLog.ViewModels

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
            let file = File.Create "the-theme-theme.zip"
            try
                let model = DisplayTheme.FromTheme (fun _ -> false) theme
                Expect.isFalse model.IsInUse "IsInUse should not have been set"
                Expect.isTrue model.IsOnDisk "IsOnDisk should have been set"
            finally
               file.Close()
               file.Dispose()
               File.Delete "the-theme-theme.zip"
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

/// All tests for the Domain namespace
let all =
    testList
        "Domain"
        [ // support types
          nodaTests
          accessLevelTests
          commentStatusTests
          explicitRatingTests
          episodeTests
          markupTextTests
          podcastMediumTests
          postStatusTests
          customFeedSourceTests
          themeAssetIdTests
          uploadDestinationTests
          // data types
          webLogTests
          webLogUserTests
          // view models
          addBaseToRelativeUrlsTests
          displayCustomFeedTests
          displayPageTests
          displayRevisionTests
          displayThemeTests
          displayUploadTests ]
