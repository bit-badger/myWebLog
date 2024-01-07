module Domain

open System
open Expecto
open MyWebLog
open NodaTime

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
                Expect.equal (UploadDestination.Parse "Disk") Database "\"Disk\" not parsed correctly"
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

/// All tests for the Domain namespace
let all =
    testList
        "Domain"
        [ nodaTests
          accessLevelTests
          commentStatusTests
          explicitRatingTests
          episodeTests
          markupTextTests
          podcastMediumTests
          postStatusTests
          customFeedSourceTests
          themeAssetIdTests
          uploadDestinationTests ]
