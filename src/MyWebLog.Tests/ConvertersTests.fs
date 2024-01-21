module ConvertersTests

open Expecto
open Microsoft.FSharpLu.Json
open MyWebLog
open MyWebLog.Converters.Json
open Newtonsoft.Json

/// Unit tests for the CategoryIdConverter type
let categoryIdConverterTests = testList "CategoryIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(CategoryIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(CategoryId "test-cat-id", opts)
        Expect.equal after "\"test-cat-id\"" "Category ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<CategoryId>("\"test-cat-id\"", opts)
        Expect.equal after (CategoryId "test-cat-id") "Category ID not serialized incorrectly"
    }
]

/// Unit tests for the CommentIdConverter type
let commentIdConverterTests = testList "CommentIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(CommentIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(CommentId "test-id", opts)
        Expect.equal after "\"test-id\"" "Comment ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<CommentId>("\"my-test\"", opts)
        Expect.equal after (CommentId "my-test") "Comment ID deserialized incorrectly"
    }
]

/// Unit tests for the CommentStatusConverter type
let commentStatusConverterTests = testList "CommentStatusConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(CommentStatusConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(Approved, opts)
        Expect.equal after "\"Approved\"" "Comment status serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<CommentStatus>("\"Spam\"", opts)
        Expect.equal after Spam "Comment status deserialized incorrectly"
    }
]

/// Unit tests for the CustomFeedIdConverter type
let customFeedIdConverterTests = testList "CustomFeedIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(CustomFeedIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(CustomFeedId "my-feed", opts)
        Expect.equal after "\"my-feed\"" "Custom feed ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<CustomFeedId>("\"feed-me\"", opts)
        Expect.equal after (CustomFeedId "feed-me") "Custom feed ID deserialized incorrectly"
    }
]

/// Unit tests for the CustomFeedSourceConverter type
let customFeedSourceConverterTests = testList "CustomFeedSourceConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(CustomFeedSourceConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(Category (CategoryId "abc-123"), opts)
        Expect.equal after "\"category:abc-123\"" "Custom feed source serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<CustomFeedSource>("\"tag:testing\"", opts)
        Expect.equal after (Tag "testing") "Custom feed source deserialized incorrectly"
    }
]

/// Unit tests for the ExplicitRating type
let explicitRatingConverterTests = testList "ExplicitRatingConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(ExplicitRatingConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(Yes, opts)
        Expect.equal after "\"yes\"" "Explicit rating serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<ExplicitRating>("\"clean\"", opts)
        Expect.equal after Clean "Explicit rating deserialized incorrectly"
    }
]

/// Unit tests for the MarkupText type
let markupTextConverterTests = testList "MarkupTextConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(MarkupTextConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(Html "<h4>test</h4>", opts)
        Expect.equal after "\"HTML: <h4>test</h4>\"" "Markup text serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<MarkupText>("\"Markdown: #### test\"", opts)
        Expect.equal after (Markdown "#### test") "Markup text deserialized incorrectly"
    }
]

/// Unit tests for the PermalinkConverter type
let permalinkConverterTests = testList "PermalinkConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(PermalinkConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(Permalink "2022/test", opts)
        Expect.equal after "\"2022/test\"" "Permalink serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<Permalink>("\"2023/unit.html\"", opts)
        Expect.equal after (Permalink "2023/unit.html") "Permalink deserialized incorrectly"
    }
]

/// Unit tests for the PageIdConverter type
let pageIdConverterTests = testList "PageIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(PageIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(PageId "test-page", opts)
        Expect.equal after "\"test-page\"" "Page ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<PageId>("\"page-test\"", opts)
        Expect.equal after (PageId "page-test") "Page ID deserialized incorrectly"
    }
]

/// Unit tests for the PodcastMedium type
let podcastMediumConverterTests = testList "PodcastMediumConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(PodcastMediumConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(Audiobook, opts)
        Expect.equal after "\"audiobook\"" "Podcast medium serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<PodcastMedium>("\"newsletter\"", opts)
        Expect.equal after Newsletter "Podcast medium deserialized incorrectly"
    }
]

/// Unit tests for the PostIdConverter type
let postIdConverterTests = testList "PostIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(PostIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(PostId "test-post", opts)
        Expect.equal after "\"test-post\"" "Post ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<PostId>("\"post-test\"", opts)
        Expect.equal after (PostId "post-test") "Post ID deserialized incorrectly"
    }
]

/// Unit tests for the TagMapIdConverter type
let tagMapIdConverterTests = testList "TagMapIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(TagMapIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(TagMapId "test-map", opts)
        Expect.equal after "\"test-map\"" "Tag map ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<TagMapId>("\"map-test\"", opts)
        Expect.equal after (TagMapId "map-test") "Tag map ID deserialized incorrectly"
    }
]

/// Unit tests for the ThemeAssetIdConverter type
let themeAssetIdConverterTests = testList "ThemeAssetIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(ThemeAssetIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(ThemeAssetId (ThemeId "test", "unit.jpg"), opts)
        Expect.equal after "\"test/unit.jpg\"" "Theme asset ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<ThemeAssetId>("\"theme/test.png\"", opts)
        Expect.equal after (ThemeAssetId (ThemeId "theme", "test.png")) "Theme asset ID deserialized incorrectly"
    }
]

/// Unit tests for the ThemeIdConverter type
let themeIdConverterTests = testList "ThemeIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(ThemeIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(ThemeId "test-theme", opts)
        Expect.equal after "\"test-theme\"" "Theme ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<ThemeId>("\"theme-test\"", opts)
        Expect.equal after (ThemeId "theme-test") "Theme ID deserialized incorrectly"
    }
]

/// Unit tests for the UploadIdConverter type
let uploadIdConverterTests = testList "UploadIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(UploadIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(UploadId "test-up", opts)
        Expect.equal after "\"test-up\"" "Upload ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<UploadId>("\"up-test\"", opts)
        Expect.equal after (UploadId "up-test") "Upload ID deserialized incorrectly"
    }
]

/// Unit tests for the WebLogIdConverter type
let webLogIdConverterTests = testList "WebLogIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(WebLogIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(WebLogId "test-web", opts)
        Expect.equal after "\"test-web\"" "Web log ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<WebLogId>("\"web-test\"", opts)
        Expect.equal after (WebLogId "web-test") "Web log ID deserialized incorrectly"
    }
]

/// Unit tests for the WebLogUserIdConverter type
let webLogUserIdConverterTests = testList "WebLogUserIdConverter" [
    let opts = JsonSerializerSettings()
    opts.Converters.Add(WebLogUserIdConverter())
    test "succeeds when serializing" {
        let after = JsonConvert.SerializeObject(WebLogUserId "test-user", opts)
        Expect.equal after "\"test-user\"" "Web log user ID serialized incorrectly"
    }
    test "succeeds when deserializing" {
        let after = JsonConvert.DeserializeObject<WebLogUserId>("\"user-test\"", opts)
        Expect.equal after (WebLogUserId "user-test") "Web log user ID deserialized incorrectly"
    }
]

open NodaTime.Serialization.JsonNet

/// Unit tests for the Json.configure function
let configureTests = test "Json.configure succeeds" {
    let has typ (converter: JsonConverter) = converter.GetType() = typ
    let ser = configure (JsonSerializer.Create())
    Expect.hasCountOf ser.Converters 1u (has typeof<CategoryIdConverter>) "Category ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<CommentIdConverter>) "Comment ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<CommentStatusConverter>) "Comment status converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<CustomFeedIdConverter>) "Custom feed ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<CustomFeedSourceConverter>) "Custom feed source converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<ExplicitRatingConverter>) "Explicit rating converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<MarkupTextConverter>) "Markup text converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<PermalinkConverter>) "Permalink converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<PageIdConverter>) "Page ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<PodcastMediumConverter>) "Podcast medium converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<PostIdConverter>) "Post ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<TagMapIdConverter>) "Tag map ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<ThemeAssetIdConverter>) "Theme asset ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<ThemeIdConverter>) "Theme ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<UploadIdConverter>) "Upload ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<WebLogIdConverter>) "Web log ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<WebLogUserIdConverter>) "Web log user ID converter not found"
    Expect.hasCountOf ser.Converters 1u (has typeof<CompactUnionJsonConverter>) "F# type converter not found"
    Expect.hasCountOf ser.Converters 1u (has (NodaConverters.InstantConverter.GetType())) "NodaTime converter not found"
    Expect.equal ser.NullValueHandling NullValueHandling.Ignore "Null handling set incorrectly"
    Expect.equal ser.MissingMemberHandling MissingMemberHandling.Ignore "Missing member handling set incorrectly"
}

/// All tests for the Data.Converters file
let all = testList "Converters" [
    categoryIdConverterTests
    commentIdConverterTests
    commentStatusConverterTests
    customFeedIdConverterTests
    customFeedSourceConverterTests
    explicitRatingConverterTests
    markupTextConverterTests
    permalinkConverterTests
    pageIdConverterTests
    podcastMediumConverterTests
    postIdConverterTests
    tagMapIdConverterTests
    themeAssetIdConverterTests
    themeIdConverterTests
    uploadIdConverterTests
    webLogIdConverterTests
    webLogUserIdConverterTests
    configureTests
]
