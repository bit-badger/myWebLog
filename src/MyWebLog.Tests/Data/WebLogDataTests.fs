/// <summary>
/// Integration tests for <see cref="IWebLogData" /> implementations
/// </summary> 
module WebLogDataTests

open System
open Expecto
open MyWebLog
open MyWebLog.Data

/// The ID of the root web log
let private rootId = CategoryDataTests.rootId

let ``Add succeeds`` (data: IData) = task {
    do! data.WebLog.Add
            { Id            = WebLogId "new-weblog"
              Name          = "Test Web Log"
              Slug          = "test-web-log"
              Subtitle      = None
              DefaultPage   = ""
              PostsPerPage  = 7
              ThemeId       = ThemeId "default"
              UrlBase       = "https://example.com/new"
              TimeZone      = "America/Los_Angeles"
              Rss           =
                  { IsFeedEnabled     = true
                    FeedName          = "my-feed.xml"
                    ItemsInFeed       = None 
                    IsCategoryEnabled = false
                    IsTagEnabled      = false
                    Copyright         = Some "go for it"
                    CustomFeeds       = [] }
              AutoHtmx      = true
              Uploads       = Disk
              RedirectRules = [ { From = "/here"; To = "/there"; IsRegex = false } ] }
    let! webLog = data.WebLog.FindById (WebLogId "new-weblog")
    Expect.isSome webLog "The web log should have been returned"
    let it = webLog.Value
    Expect.equal it.Id (WebLogId "new-weblog") "ID is incorrect"
    Expect.equal it.Name "Test Web Log" "Name is incorrect"
    Expect.equal it.Slug "test-web-log" "Slug is incorrect"
    Expect.isNone it.Subtitle "Subtitle is incorrect"
    Expect.equal it.DefaultPage "" "Default page is incorrect"
    Expect.equal it.PostsPerPage 7 "Posts per page is incorrect"
    Expect.equal it.ThemeId (ThemeId "default") "Theme ID is incorrect"
    Expect.equal it.UrlBase "https://example.com/new" "URL base is incorrect"
    Expect.equal it.TimeZone "America/Los_Angeles" "Time zone is incorrect"
    Expect.isTrue it.AutoHtmx "Auto htmx flag is incorrect"
    Expect.equal it.Uploads Disk "Upload destination is incorrect"
    Expect.equal it.RedirectRules [ { From = "/here"; To = "/there"; IsRegex = false } ] "Redirect rules are incorrect"
    let rss = it.Rss
    Expect.isTrue rss.IsFeedEnabled "Is feed enabled flag is incorrect"
    Expect.equal rss.FeedName "my-feed.xml" "Feed name is incorrect"
    Expect.isNone rss.ItemsInFeed "Items in feed is incorrect"
    Expect.isFalse rss.IsCategoryEnabled "Is category enabled flag is incorrect"
    Expect.isFalse rss.IsTagEnabled "Is tag enabled flag is incorrect"
    Expect.equal rss.Copyright (Some "go for it") "Copyright is incorrect"
    Expect.isEmpty rss.CustomFeeds "Custom feeds are incorrect"
}

let ``All succeeds`` (data: IData) = task {
    let! webLogs = data.WebLog.All()
    Expect.hasLength webLogs 2 "There should have been 2 web logs returned"
    for webLog in webLogs do
        Expect.contains [ rootId; WebLogId "new-weblog" ] webLog.Id $"Unexpected web log returned ({webLog.Id})"
}

let ``FindByHost succeeds when a web log is found`` (data: IData) = task {
    let! webLog = data.WebLog.FindByHost "http://localhost:8081"
    Expect.isSome webLog "A web log should have been returned"
    Expect.equal webLog.Value.Id rootId "The wrong web log was returned"
}

let ``FindByHost succeeds when a web log is not found`` (data: IData) = task {
    let! webLog = data.WebLog.FindByHost "https://test.units"
    Expect.isNone webLog "There should not have been a web log returned"
}

let ``FindById succeeds when a web log is found`` (data: IData) = task {
    let! webLog = data.WebLog.FindById rootId
    Expect.isSome webLog "There should have been a web log returned"
    let it = webLog.Value
    Expect.equal it.Id rootId "ID is incorrect"
    Expect.equal it.Name "Root WebLog" "Name is incorrect"
    Expect.equal it.Slug "root-weblog" "Slug is incorrect"
    Expect.equal it.Subtitle (Some "This is the main one") "Subtitle is incorrect"
    Expect.equal it.DefaultPage "posts" "Default page is incorrect"
    Expect.equal it.PostsPerPage 9 "Posts per page is incorrect"
    Expect.equal it.ThemeId (ThemeId "default") "Theme ID is incorrect"
    Expect.equal it.UrlBase "http://localhost:8081" "URL base is incorrect"
    Expect.equal it.TimeZone "America/Denver" "Time zone is incorrect"
    Expect.isTrue it.AutoHtmx "Auto htmx flag is incorrect"
    Expect.equal it.Uploads Database "Upload destination is incorrect"
    Expect.isEmpty it.RedirectRules "Redirect rules are incorrect"
    let rss = it.Rss
    Expect.isTrue rss.IsFeedEnabled "Is feed enabled flag is incorrect"
    Expect.equal rss.FeedName "feed" "Feed name is incorrect"
    Expect.equal rss.ItemsInFeed (Some 7) "Items in feed is incorrect"
    Expect.isTrue rss.IsCategoryEnabled "Is category enabled flag is incorrect"
    Expect.isTrue rss.IsTagEnabled "Is tag enabled flag is incorrect"
    Expect.equal rss.Copyright (Some "CC40-NC-BY") "Copyright is incorrect"
    Expect.hasLength rss.CustomFeeds 1 "There should be 1 custom feed"
    Expect.equal rss.CustomFeeds[0].Id (CustomFeedId "isPQ6drbDEydxohQzaiYtQ") "Custom feed ID incorrect"
    Expect.equal rss.CustomFeeds[0].Source (Tag "podcast") "Custom feed source is incorrect"
    Expect.equal rss.CustomFeeds[0].Path (Permalink "podcast-feed") "Custom feed path is incorrect"
    Expect.isSome rss.CustomFeeds[0].Podcast "There should be podcast settings for this custom feed"
    let pod = rss.CustomFeeds[0].Podcast.Value
    Expect.equal pod.Title "Root Podcast" "Podcast title is incorrect"
    Expect.equal pod.ItemsInFeed 23 "Podcast items in feed is incorrect"
    Expect.equal pod.Summary "All things that happen in the domain root" "Podcast summary is incorrect"
    Expect.equal pod.DisplayedAuthor "Podcaster Extraordinaire" "Podcast author is incorrect"
    Expect.equal pod.Email "podcaster@example.com" "Podcast e-mail is incorrect"
    Expect.equal pod.ImageUrl (Permalink "images/cover-art.png") "Podcast image URL is incorrect"
    Expect.equal pod.AppleCategory "Fiction" "Podcast Apple category is incorrect"
    Expect.equal pod.AppleSubcategory (Some "Drama") "Podcast Apple subcategory is incorrect"
    Expect.equal pod.Explicit No "Podcast explicit rating is incorrect"
    Expect.equal pod.DefaultMediaType (Some "audio/mpeg") "Podcast default media type is incorrect"
    Expect.equal pod.MediaBaseUrl (Some "https://media.example.com/root/") "Podcast media base URL is incorrect"
    Expect.equal pod.PodcastGuid (Some (Guid.Parse "10fd7f79-c719-4e1d-9da7-10405dd4fd96")) "Podcast GUID is incorrect"
    Expect.equal pod.FundingUrl (Some "https://example.com/support-us") "Podcast funding URL is incorrect"
    Expect.equal pod.FundingText (Some "Support Our Work") "Podcast funding text is incorrect"
    Expect.equal pod.Medium (Some Newsletter) "Podcast medium is incorrect"
}

let ``FindById succeeds when a web log is not found`` (data: IData) = task {
    let! webLog = data.WebLog.FindById (WebLogId "no-web-log")
    Expect.isNone webLog "There should not have been a web log returned"
}

let ``UpdateRedirectRules succeeds when the web log exists`` (data: IData) = task {
    let! webLog = data.WebLog.FindById (WebLogId "new-weblog")
    Expect.isSome webLog "The test web log should have been returned"
    do! data.WebLog.UpdateRedirectRules
            { webLog.Value with
                RedirectRules = { From = "/now"; To = "/later"; IsRegex = false } :: webLog.Value.RedirectRules }
    let! updated = data.WebLog.FindById (WebLogId "new-weblog")
    Expect.isSome updated "The updated web log should have been returned"
    Expect.equal
        updated.Value.RedirectRules
        [ { From = "/now"; To = "/later"; IsRegex = false }; { From = "/here"; To = "/there"; IsRegex = false } ]
        "Redirect rules not updated correctly"
}

let ``UpdateRedirectRules succeeds when the web log does not exist`` (data: IData) = task {
    do! data.WebLog.UpdateRedirectRules { WebLog.Empty with Id = WebLogId "no-rules" }
    Expect.isTrue true "This not raising an exception is the test"
}

let ``UpdateRssOptions succeeds when the web log exists`` (data: IData) = task {
    let! webLog = data.WebLog.FindById rootId
    Expect.isSome webLog "The root web log should have been returned"
    do! data.WebLog.UpdateRssOptions { webLog.Value with Rss = { webLog.Value.Rss with CustomFeeds = [] } }
    let! updated = data.WebLog.FindById rootId
    Expect.isSome updated "The updated web log should have been returned"
    Expect.isEmpty updated.Value.Rss.CustomFeeds "RSS options not updated correctly"
}

let ``UpdateRssOptions succeeds when the web log does not exist`` (data: IData) = task {
    do! data.WebLog.UpdateRssOptions { WebLog.Empty with Id = WebLogId "rss-less" }
    Expect.isTrue true "This not raising an exception is the test"
}

let ``UpdateSettings succeeds when the web log exists`` (data: IData) = task {
    let! webLog = data.WebLog.FindById rootId
    Expect.isSome webLog "The root web log should have been returned"
    do! data.WebLog.UpdateSettings { webLog.Value with AutoHtmx = false; Subtitle = None }
    let! updated = data.WebLog.FindById rootId
    Expect.isSome updated "The updated web log should have been returned"
    Expect.isFalse updated.Value.AutoHtmx "Auto htmx flag not updated correctly"
    Expect.isNone updated.Value.Subtitle "Subtitle not updated correctly"
}

let ``UpdateSettings succeeds when the web log does not exist`` (data: IData) = task {
    do! data.WebLog.UpdateRedirectRules { WebLog.Empty with Id = WebLogId "no-settings" }
    let! webLog = data.WebLog.FindById (WebLogId "no-settings")
    Expect.isNone webLog "Updating settings should not have created a web log"
}

let ``Delete succeeds when the web log exists`` (data: IData) = task {
    do! data.WebLog.Delete rootId
    let! cats = data.Category.FindByWebLog rootId
    Expect.isEmpty cats "There should be no categories remaining"
    let! pages = data.Page.FindFullByWebLog rootId
    Expect.isEmpty pages "There should be no pages remaining"
    let! posts = data.Post.FindFullByWebLog rootId
    Expect.isEmpty posts "There should be no posts remaining"
    let! tagMappings = data.TagMap.FindByWebLog rootId
    Expect.isEmpty tagMappings "There should be no tag mappings remaining"
    let! uploads = data.Upload.FindByWebLog rootId
    Expect.isEmpty uploads "There should be no uploads remaining"
    let! users = data.WebLogUser.FindByWebLog rootId
    Expect.isEmpty users "There should be no users remaining"
}

let ``Delete succeeds when the web log does not exist`` (data: IData) = task {
    do! data.WebLog.Delete rootId // already deleted above
    Expect.isTrue true "This not raising an exception is the test"
}
