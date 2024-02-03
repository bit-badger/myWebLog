/// <summary>
/// Integration tests for <see cref="IPostData" /> implementations
/// </summary> 
module PostDataTests

open System
open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// The ID of the root web log
let private rootId = CategoryDataTests.rootId

/// The ID of podcast episode 1
let private episode1 = PostId "osxMfWGlAkyugUbJ1-xD1g"

/// The published instant for episode 1
let private episode1Published = Instant.FromDateTimeOffset(DateTimeOffset.Parse "2024-01-20T22:24:01Z")

/// The ID of podcast episode 2
let private episode2 = PostId "l4_Eh4aFO06SqqJjOymNzA"

/// The ID of "Something May Happen" post
let private something = PostId "QweKbWQiOkqqrjEdgP9wwg"

/// The published instant for "Something May Happen" post
let private somethingPublished = Instant.FromDateTimeOffset(DateTimeOffset.Parse "2024-01-20T22:32:59Z")

/// The ID of "An Incomplete Thought" post
let private incomplete = PostId "VweKbWQiOkqqrjEdgP9wwg"

/// The ID of "Test Post 1" post
let private testPost1 = PostId "RCsCU2puYEmkpzotoi8p4g"

/// The published instant for "Test Post 1" post
let private testPost1Published = Instant.FromDateTimeOffset(DateTimeOffset.Parse "2024-01-20T22:17:29Z")

/// The category IDs for "Spitball" (parent) and "Moonshot"
let private testCatIds = [ CategoryId "jw6N69YtTEWVHAO33jHU-w"; CategoryId "ScVpyu1e7UiP7bDdge3ZEw" ]

/// Ensure that a list of posts has text for each post
let private ensureHasText (posts: Post list) =
    for post in posts do Expect.isNotEmpty post.Text $"Text should not be blank (post ID {post.Id})"

/// Ensure that a list of posts has no revisions or prior permalinks
let private ensureEmpty posts =
    for post in posts do
        Expect.isEmpty post.Revisions $"There should have been no revisions (post ID {post.Id})"
        Expect.isEmpty post.PriorPermalinks $"There should have been no prior permalinks (post ID {post.Id})"

let ``Add succeeds`` (data: IData) = task {
    let post =
        { Id              = PostId "a-new-post"
          WebLogId        = WebLogId "test"
          AuthorId        = WebLogUserId "test-author"
          Status          = Published 
          Title           = "A New Test Post"
          Permalink       = Permalink "2020/test-post.html"
          PublishedOn     = Some (Noda.epoch + Duration.FromMinutes 1L)
          UpdatedOn       = Noda.epoch + Duration.FromMinutes 3L
          Template        = Some "fancy"
          Text            = "<p>Test text here"
          CategoryIds     = [ CategoryId "a"; CategoryId "b" ]
          Tags            = [ "x"; "y"; "zed" ]
          Episode         = Some { Episode.Empty with Media = "test-ep.mp3" }
          Metadata        = [ { Name = "Meta"; Value = "Data" } ]
          PriorPermalinks = [ Permalink "2020/test-post-a.html" ]
          Revisions       = [ { AsOf = Noda.epoch + Duration.FromMinutes 1L; Text = Html "<p>Test text here" } ] }
    do! data.Post.Add post
    let! stored = data.Post.FindFullById post.Id post.WebLogId
    Expect.isSome stored "The added post should have been retrieved"
    let it = stored.Value
    Expect.equal it.Id post.Id "ID not saved properly"
    Expect.equal it.WebLogId post.WebLogId "Web log ID not saved properly"
    Expect.equal it.AuthorId post.AuthorId "Author ID not saved properly"
    Expect.equal it.Status post.Status "Status not saved properly"
    Expect.equal it.Title post.Title "Title not saved properly"
    Expect.equal it.Permalink post.Permalink "Permalink not saved properly"
    Expect.equal it.PublishedOn post.PublishedOn "Published On not saved properly"
    Expect.equal it.UpdatedOn post.UpdatedOn "Updated On not saved properly"
    Expect.equal it.Template post.Template "Template not saved properly"
    Expect.equal it.Text post.Text "Text not saved properly"
    Expect.equal it.CategoryIds post.CategoryIds "Category IDs not saved properly"
    Expect.equal it.Tags post.Tags "Tags not saved properly"
    Expect.equal it.Episode post.Episode "Episode not saved properly"
    Expect.equal it.Metadata post.Metadata "Metadata items not saved properly"
    Expect.equal it.PriorPermalinks post.PriorPermalinks "Prior permalinks not saved properly"
    Expect.equal it.Revisions post.Revisions "Revisions not saved properly"
}

let ``CountByStatus succeeds`` (data: IData) = task {
    let! count = data.Post.CountByStatus Published rootId
    Expect.equal count 4 "There should be 4 published posts"
}

let ``FindById succeeds when a post is found`` (data: IData) = task {
    let! post = data.Post.FindById episode1 rootId
    Expect.isSome post "There should have been a post returned"
    let it = post.Value
    Expect.equal it.Id episode1 "An incorrect post was retrieved"
    Expect.equal it.WebLogId rootId "The post belongs to an incorrect web log"
    Expect.equal it.AuthorId (WebLogUserId "5EM2rimH9kONpmd2zQkiVA") "Author ID is incorrect"
    Expect.equal it.Status Published "Status is incorrect"
    Expect.equal it.Title "Episode 1" "Title is incorrect"
    Expect.equal it.Permalink (Permalink "2024/episode-1.html") "Permalink is incorrect"
    Expect.equal it.PublishedOn (Some episode1Published) "Published On is incorrect"
    Expect.equal it.UpdatedOn episode1Published "Updated On is incorrect"
    Expect.equal it.Text "<p>It's the launch of my new podcast - y'all come listen!" "Text is incorrect"
    Expect.equal it.CategoryIds [ CategoryId "S5JflPsJ9EG7gA2LD4m92A" ] "Category IDs are incorrect"
    Expect.equal it.Tags [ "general"; "podcast" ] "Tags are incorrect"
    Expect.isSome it.Episode "There should be an episode associated with this post"
    let ep = it.Episode.Value
    Expect.equal ep.Media "episode-1.mp3" "Episode media is incorrect"
    Expect.equal ep.Length 124302L "Episode length is incorrect"
    Expect.equal
        ep.Duration (Some (Duration.FromMinutes 12L + Duration.FromSeconds 22L)) "Episode duration is incorrect"
    Expect.equal ep.ImageUrl (Some "images/ep1-cover.png") "Episode image URL is incorrect"
    Expect.equal ep.Subtitle (Some "An introduction to this podcast") "Episode subtitle is incorrect"
    Expect.equal ep.Explicit (Some Clean) "Episode explicit rating is incorrect"
    Expect.equal ep.ChapterFile (Some "uploads/chapters.json") "Episode chapter file is incorrect"
    Expect.equal ep.TranscriptUrl (Some "uploads/transcript.srt") "Episode transcript URL is incorrect"
    Expect.equal ep.TranscriptType (Some "application/srt") "Episode transcript type is incorrect"
    Expect.equal ep.TranscriptLang (Some "en") "Episode transcript language is incorrect"
    Expect.equal ep.TranscriptCaptions (Some true) "Episode transcript caption flag is incorrect"
    Expect.equal ep.SeasonNumber (Some 1) "Episode season number is incorrect"
    Expect.equal ep.SeasonDescription (Some "The First Season") "Episode season description is incorrect"
    Expect.equal ep.EpisodeNumber (Some 1.) "Episode number is incorrect"
    Expect.equal ep.EpisodeDescription (Some "The first episode ever!") "Episode description is incorrect"
    Expect.equal
        it.Metadata
        [ { Name = "Density"; Value = "Non-existent" }; { Name = "Intensity"; Value = "Low" } ]
        "Metadata is incorrect"
    ensureEmpty [ it ]
}

let ``FindById succeeds when a post is not found (incorrect weblog)`` (data: IData) = task {
    let! post = data.Post.FindById episode1 (WebLogId "wrong")
    Expect.isNone post "The post should not have been retrieved"
}

let ``FindById succeeds when a post is not found (bad post ID)`` (data: IData) = task {
    let! post = data.Post.FindById (PostId "absent") rootId
    Expect.isNone post "The post should not have been retrieved"
}

let ``FindByPermalink succeeds when a post is found`` (data: IData) = task {
    let! post = data.Post.FindByPermalink (Permalink "2024/episode-1.html") rootId
    Expect.isSome post "A post should have been returned"
    let it = post.Value
    Expect.equal it.Id episode1 "The wrong post was retrieved"
    ensureEmpty [ it ]
}

let ``FindByPermalink succeeds when a post is not found (incorrect weblog)`` (data: IData) = task {
    let! post = data.Post.FindByPermalink (Permalink "2024/episode-1.html") (WebLogId "incorrect")
    Expect.isNone post "The post should not have been retrieved"
}

let ``FindByPermalink succeeds when a post is not found (no such permalink)`` (data: IData) = task {
    let! post = data.Post.FindByPermalink (Permalink "404") rootId
    Expect.isNone post "The post should not have been retrieved"
}

let ``FindCurrentPermalink succeeds when a post is found`` (data: IData) = task {
    let! link = data.Post.FindCurrentPermalink [ Permalink "2024/ep-1.html"; Permalink "2024/ep-1.html/" ] rootId
    Expect.isSome link "A permalink should have been returned"
    Expect.equal link (Some (Permalink "2024/episode-1.html")) "The wrong permalink was retrieved"
}

let ``FindCurrentPermalink succeeds when a post is not found`` (data: IData) = task {
    let! link = data.Post.FindCurrentPermalink [ Permalink "oops/"; Permalink "oops" ] rootId
    Expect.isNone link "A permalink should not have been returned"
}

let ``FindFullById succeeds when a post is found`` (data: IData) = task {
    let! post = data.Post.FindFullById episode1 rootId
    Expect.isSome post "A post should have been returned"
    let it = post.Value
    Expect.equal it.Id episode1 "The wrong post was retrieved"
    Expect.equal it.WebLogId rootId "The post's web log did not match the called parameter"
    Expect.equal
        it.Revisions
        [ { AsOf = episode1Published; Text = Html "<p>It's the launch of my new podcast - y'all come listen!" } ]
        "Revisions are incorrect"
    Expect.equal it.PriorPermalinks [ Permalink "2024/ep-1.html" ] "Prior permalinks are incorrect"
}

let ``FindFullById succeeds when a post is not found`` (data: IData) = task {
    let! post = data.Post.FindFullById (PostId "no-post") rootId
    Expect.isNone post "A page should not have been retrieved"
}

let ``FindFullByWebLog succeeds when posts are found`` (data: IData) = task {
    let! posts = data.Post.FindFullByWebLog rootId
    Expect.hasLength posts 5 "There should have been 5 posts returned"
    let allPosts = [ testPost1; episode1; episode2; something; incomplete ]
    posts |> List.iter (fun it ->
        Expect.contains allPosts it.Id $"Post ID {it.Id} unexpected"
        if it.Id = episode1 then
            Expect.isNonEmpty it.Metadata "Metadata should have been retrieved"
            Expect.isNonEmpty it.PriorPermalinks "Prior permalinks should have been retrieved"
        Expect.isNonEmpty it.Revisions "Revisions should have been retrieved")
}

let ``FindFullByWebLog succeeds when posts are not found`` (data: IData) = task {
    let! posts = data.Post.FindFullByWebLog (WebLogId "nonexistent")
    Expect.isEmpty posts "No posts should have been retrieved"
}

let ``FindPageOfCategorizedPosts succeeds when posts are found`` (data: IData) = task {
    let! posts = data.Post.FindPageOfCategorizedPosts rootId testCatIds 1 1
    Expect.hasLength posts 2 "There should be 2 posts returned"
    Expect.equal posts[0].Id something "The wrong post was returned for page 1"
    ensureEmpty posts
    let! posts = data.Post.FindPageOfCategorizedPosts rootId testCatIds 2 1
    Expect.hasLength posts 1 "There should be 1 post returned"
    Expect.equal posts[0].Id testPost1 "The wrong post was returned for page 2"
    ensureEmpty posts
}

let ``FindPageOfCategorizedPosts succeeds when finding a too-high page number`` (data: IData) = task {
    let! posts = data.Post.FindPageOfCategorizedPosts rootId testCatIds 17 2
    Expect.hasLength posts 0 "There should have been no posts returned (not enough posts)"
}

let ``FindPageOfCategorizedPosts succeeds when a category has no posts`` (data: IData) = task {
    let! posts = data.Post.FindPageOfCategorizedPosts rootId [ CategoryId "nope" ] 1 1
    Expect.hasLength posts 0 "There should have been no posts returned (none match)"
}

let ``FindPageOfPosts succeeds when posts are found`` (data: IData) = task {
    let ensureNoText (posts: Post list) =
        for post in posts do Expect.equal post.Text "" $"There should be no text (post ID {post.Id})"
    let! posts = data.Post.FindPageOfPosts rootId 1 2
    Expect.hasLength posts 3 "There should have been 3 posts returned for page 1"
    Expect.equal posts[0].Id incomplete "Page 1, post 1 is incorrect"
    Expect.equal posts[1].Id something "Page 1, post 2 is incorrect"
    Expect.equal posts[2].Id episode2 "Page 1, post 3 is incorrect"
    ensureNoText posts
    ensureEmpty posts
    let! posts = data.Post.FindPageOfPosts rootId 2 2
    Expect.hasLength posts 3 "There should have been 3 posts returned for page 2"
    Expect.equal posts[0].Id episode2 "Page 2, post 1 is incorrect"
    Expect.equal posts[1].Id episode1 "Page 2, post 2 is incorrect"
    Expect.equal posts[2].Id testPost1 "Page 2, post 3 is incorrect"
    ensureNoText posts
    ensureEmpty posts
    let! posts = data.Post.FindPageOfPosts rootId 3 2
    Expect.hasLength posts 1 "There should have been 1 post returned for page 3"
    Expect.equal posts[0].Id testPost1 "Page 3, post 1 is incorrect"
    ensureNoText posts
    ensureEmpty posts
}

let ``FindPageOfPosts succeeds when finding a too-high page number`` (data: IData) = task {
    let! posts = data.Post.FindPageOfPosts rootId 88 3
    Expect.isEmpty posts "There should have been no posts returned (not enough posts)"
}

let ``FindPageOfPosts succeeds when there are no posts`` (data: IData) = task {
    let! posts = data.Post.FindPageOfPosts (WebLogId "no-posts") 1 25
    Expect.isEmpty posts "There should have been no posts returned (no posts)"
}

let ``FindPageOfPublishedPosts succeeds when posts are found`` (data: IData) = task {
    let! posts = data.Post.FindPageOfPublishedPosts rootId 1 3
    Expect.hasLength posts 4 "There should have been 4 posts returned for page 1"
    Expect.equal posts[0].Id something "Page 1, post 1 is incorrect"
    Expect.equal posts[1].Id episode2 "Page 1, post 2 is incorrect"
    Expect.equal posts[2].Id episode1 "Page 1, post 3 is incorrect"
    Expect.equal posts[3].Id testPost1 "Page 1, post 4 is incorrect"
    ensureHasText posts
    ensureEmpty posts
    let! posts = data.Post.FindPageOfPublishedPosts rootId 2 2
    Expect.hasLength posts 2 "There should have been 2 posts returned for page 2"
    Expect.equal posts[0].Id episode1 "Page 2, post 1 is incorrect"
    Expect.equal posts[1].Id testPost1 "Page 2, post 2 is incorrect"
    ensureHasText posts
    ensureEmpty posts
}

let ``FindPageOfPublishedPosts succeeds when finding a too-high page number`` (data: IData) = task {
    let! posts = data.Post.FindPageOfPublishedPosts rootId 7 22
    Expect.isEmpty posts "There should have been no posts returned (not enough posts)"
}

let ``FindPageOfPublishedPosts succeeds when there are no posts`` (data: IData) = task {
    let! posts = data.Post.FindPageOfPublishedPosts (WebLogId "empty") 1 8
    Expect.isEmpty posts "There should have been no posts returned (no posts)"
}

let ``FindPageOfTaggedPosts succeeds when posts are found`` (data: IData) = task {
    let! posts = data.Post.FindPageOfTaggedPosts rootId "f#" 1 1
    Expect.hasLength posts 2 "There should have been 2 posts returned"
    Expect.equal posts[0].Id something "Page 1, post 1 is incorrect"
    Expect.equal posts[1].Id testPost1 "Page 1, post 2 is incorrect"
    ensureHasText posts
    ensureEmpty posts
    let! posts = data.Post.FindPageOfTaggedPosts rootId "f#" 2 1
    Expect.hasLength posts 1 "There should have been 1 posts returned"
    Expect.equal posts[0].Id testPost1 "Page 2, post 1 is incorrect"
    ensureHasText posts
    ensureEmpty posts
}

let ``FindPageOfTaggedPosts succeeds when posts are found (excluding drafts)`` (data: IData) = task {
    let! posts = data.Post.FindPageOfTaggedPosts rootId "speculation" 1 10
    Expect.hasLength posts 1 "There should have been 1 post returned"
    Expect.equal posts[0].Id something "Post 1 is incorrect"
    ensureHasText posts
    ensureEmpty posts
}

let ``FindPageOfTaggedPosts succeeds when finding a too-high page number`` (data: IData) = task {
    let! posts = data.Post.FindPageOfTaggedPosts rootId "f#" 436 18
    Expect.isEmpty posts "There should have been no posts returned (not enough posts)"
}

let ``FindPageOfTaggedPosts succeeds when there are no posts`` (data: IData) = task {
    let! posts = data.Post.FindPageOfTaggedPosts rootId "non-existent-tag" 1 8
    Expect.isEmpty posts "There should have been no posts returned (no posts)"
}

let ``FindSurroundingPosts succeeds when there is no next newer post`` (data: IData) = task {
    let! older, newer = data.Post.FindSurroundingPosts rootId somethingPublished
    Expect.isSome older "There should have been an older post"
    Expect.equal older.Value.Id episode2 "The next older post is incorrect"
    ensureHasText [ older.Value ]
    ensureEmpty [ older.Value ]
    Expect.isNone newer "There should not have been a newer post"
}

let ``FindSurroundingPosts succeeds when there is no next older post`` (data: IData) = task {
    let! older, newer = data.Post.FindSurroundingPosts rootId testPost1Published
    Expect.isNone older "There should not have been an older post"
    Expect.isSome newer "There should have been a newer post"
    Expect.equal newer.Value.Id episode1 "The next newer post is incorrect"
    ensureHasText [ newer.Value ]
    ensureEmpty [ newer.Value ]
}

let ``FindSurroundingPosts succeeds when older and newer exist`` (data: IData) = task {
    let! older, newer = data.Post.FindSurroundingPosts rootId episode1Published
    Expect.isSome older "There should have been an older post"
    Expect.equal older.Value.Id testPost1 "The next older post is incorrect"
    Expect.isSome newer "There should have been a newer post"
    Expect.equal newer.Value.Id episode2 "The next newer post is incorrect"
    ensureHasText [ older.Value; newer.Value ]
    ensureEmpty [ older.Value; newer.Value ]
}

let ``Update succeeds when the post exists`` (data: IData) = task {
    let! before = data.Post.FindFullById (PostId "a-new-post") (WebLogId "test")
    Expect.isSome before "The post to be updated should have been found"
    do! data.Post.Update
            { before.Value with
                AuthorId        = WebLogUserId "someone-else"
                Status          = Draft
                Title           = "An Updated Test Post"
                Permalink       = Permalink "2021/updated-post.html"
                PublishedOn     = None
                UpdatedOn       = Noda.epoch + Duration.FromDays 4
                Template        = Some "other"
                Text            = "<p>Updated text here"
                CategoryIds     = [ CategoryId "c"; CategoryId "d"; CategoryId "e" ]
                Tags            = [ "alpha"; "beta"; "nu"; "zeta" ]
                Episode         = None
                Metadata        = [ { Name = "Howdy"; Value = "Pardner" } ]
                PriorPermalinks = Permalink "2020/test-post.html" :: before.Value.PriorPermalinks
                Revisions       =
                    { AsOf = Noda.epoch + Duration.FromDays 4; Text = Html "<p>Updated text here" }
                        :: before.Value.Revisions }
    let! after = data.Post.FindFullById (PostId "a-new-post") (WebLogId "test")
    Expect.isSome after "The updated post should have been found"
    let post = after.Value
    Expect.equal post.AuthorId (WebLogUserId "someone-else") "Updated author is incorrect"
    Expect.equal post.Status Draft "Updated status is incorrect"
    Expect.equal post.Title "An Updated Test Post" "Updated title is incorrect"
    Expect.equal post.Permalink (Permalink "2021/updated-post.html") "Updated permalink is incorrect"
    Expect.isNone post.PublishedOn "Updated post should not have had a published-on date/time"
    Expect.equal post.UpdatedOn (Noda.epoch + Duration.FromDays 4) "Updated updated-on date/time is incorrect"
    Expect.equal post.Template (Some "other") "Updated template is incorrect"
    Expect.equal post.Text "<p>Updated text here" "Updated text is incorrect"
    Expect.equal
        post.CategoryIds [ CategoryId "c"; CategoryId "d"; CategoryId "e" ] "Updated category IDs are incorrect"
    Expect.equal post.Tags [ "alpha"; "beta"; "nu"; "zeta" ] "Updated tags are incorrect"
    Expect.isNone post.Episode "Update episode is incorrect"
    Expect.equal post.Metadata [ { Name = "Howdy"; Value = "Pardner" } ] "Updated metadata is incorrect"
    Expect.equal
        post.PriorPermalinks
        [ Permalink "2020/test-post.html"; Permalink "2020/test-post-a.html" ]
        "Updated prior permalinks are incorrect"
    Expect.equal
        post.Revisions
        [ { AsOf = Noda.epoch + Duration.FromDays 4; Text = Html "<p>Updated text here" }
          { AsOf = Noda.epoch + Duration.FromMinutes 1L; Text = Html "<p>Test text here" } ]
        "Updated revisions are incorrect"
}

let ``Update succeeds when the post does not exist`` (data: IData) = task {
    let postId = PostId "lost-post"
    do! data.Post.Update { Post.Empty with Id = postId; WebLogId = rootId }
    let! post = data.Post.FindById postId rootId
    Expect.isNone post "A post should not have been retrieved"
}

let ``UpdatePriorPermalinks succeeds when the post exists`` (data: IData) = task {
    let links = [ Permalink "2024/ep-1.html"; Permalink "2023/ep-1.html" ]
    let! found = data.Post.UpdatePriorPermalinks episode1 rootId links
    Expect.isTrue found "The permalinks should have been updated"
    let! post = data.Post.FindFullById episode1 rootId
    Expect.isSome post "The post should have been found"
    Expect.equal post.Value.PriorPermalinks links "The prior permalinks were not correct"
}

let ``UpdatePriorPermalinks succeeds when the post does not exist`` (data: IData) = task {
    let! found =
        data.Post.UpdatePriorPermalinks (PostId "silence") WebLogId.Empty [ Permalink "a.html"; Permalink "b.html" ]
    Expect.isFalse found "The permalinks should not have been updated"
}

let ``Delete succeeds when a post is deleted`` (data: IData) = task {
    let! deleted = data.Post.Delete episode2 rootId
    Expect.isTrue deleted "The post should have been deleted"
}

let ``Delete succeeds when a post is not deleted`` (data: IData) = task {
    let! deleted = data.Post.Delete episode2 rootId // this was deleted above
    Expect.isFalse deleted "A post should not have been deleted"
}
