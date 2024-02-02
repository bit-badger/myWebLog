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
let rootId = WebLogId "uSitJEuD3UyzWC9jgOHc8g"

/// The ID of podcast episode 1
let episode1 = PostId "osxMfWGlAkyugUbJ1-xD1g"

/// The published instant for episode 1
let episode1Published = Instant.FromDateTimeOffset(DateTimeOffset.Parse "2024-01-20T22:24:01Z")

/// The ID of podcast episode 2
let episode2 = PostId "l4_Eh4aFO06SqqJjOymNzA"

/// The ID of "Something May Happen" post
let something = PostId "QweKbWQiOkqqrjEdgP9wwg"

/// The ID of "Test Post 1" post
let testPost1 = PostId "RCsCU2puYEmkpzotoi8p4g"

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
    Expect.isEmpty it.PriorPermalinks "Prior permalinks should have been empty"
    Expect.isEmpty it.Revisions "Revisions should have been empty"
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
    Expect.isEmpty it.PriorPermalinks "Prior permalinks should have been empty"
    Expect.isEmpty it.Revisions "Revisions should have been empty"
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
    Expect.hasLength posts 4 "There should have been 4 posts returned"
    let allPosts = [ testPost1; episode1; episode2; something ]
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
