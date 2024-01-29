module PageDataTests

open System
open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// The ID of the root web log
let rootId = WebLogId "uSitJEuD3UyzWC9jgOHc8g"

/// The ID of the "A cool page" page
let coolPageId = PageId "hgc_BLEZ50SoAWLuPNISvA"

/// The published and updated time of the "A cool page" page
let coolPagePublished = Instant.FromDateTimeOffset(DateTimeOffset.Parse "2024-01-20T22:14:28Z")

/// The ID of the "Yet Another Page" page
let otherPageId = PageId "KouRjvSmm0Wz6TMD8xf67A"

let ``Add succeeds`` (data: IData) = task {
    let page =
        { Id              = PageId "added-page"
          WebLogId        = WebLogId "test"
          AuthorId        = WebLogUserId "the-author"
          Title           = "A New Page"
          Permalink       = Permalink "2024/the-page.htm"
          PublishedOn     = Noda.epoch + Duration.FromDays 3
          UpdatedOn       = Noda.epoch + Duration.FromDays 3 + Duration.FromMinutes 2L
          IsInPageList    = true
          Template        = Some "new-page-template"
          Text            = "<h1>A new page</h1>"
          Metadata        = [ { Name = "Meta Item"; Value = "Meta Value" } ]
          PriorPermalinks = [ Permalink "2024/the-new-page.htm" ]
          Revisions       = [ { AsOf = Noda.epoch + Duration.FromDays 3; Text = Html "<h1>A new page</h1>" } ] }
    do! data.Page.Add page 
    let! stored = data.Page.FindFullById (PageId "added-page") (WebLogId "test")
    Expect.isSome stored "The page should have been added"
    let pg = stored.Value
    Expect.equal pg.Id page.Id "ID not saved properly"
    Expect.equal pg.WebLogId page.WebLogId "Web log ID not saved properly"
    Expect.equal pg.AuthorId page.AuthorId "Author ID not saved properly"
    Expect.equal pg.Title page.Title "Title not saved properly"
    Expect.equal pg.Permalink page.Permalink "Permalink not saved properly"
    Expect.equal pg.PublishedOn page.PublishedOn "Published On not saved properly"
    Expect.equal pg.UpdatedOn page.UpdatedOn "Updated On not saved properly"
    Expect.equal pg.IsInPageList page.IsInPageList "Is in page list flag not saved properly"
    Expect.equal pg.Template page.Template "Template not saved properly"
    Expect.equal pg.Text page.Text "Text not saved properly"
    Expect.hasLength pg.Metadata 1 "There should have been one meta item properly"
    Expect.equal pg.Metadata[0].Name page.Metadata[0].Name "Metadata name not saved properly"
    Expect.equal pg.Metadata[0].Value page.Metadata[0].Value "Metadata value not saved properly"
    Expect.hasLength pg.PriorPermalinks 1 "There should have been one prior permalink"
    Expect.equal pg.PriorPermalinks[0] page.PriorPermalinks[0] "Prior permalink not saved properly"
    Expect.hasLength pg.Revisions 1 "There should have been one revision"
    Expect.equal pg.Revisions[0].AsOf page.Revisions[0].AsOf "Revision as of not saved properly"
    Expect.equal pg.Revisions[0].Text page.Revisions[0].Text "Revision text not saved properly"
}

let ``All succeeds`` (data: IData) = task {
    let! pages = data.Page.All rootId
    Expect.hasLength pages 2 "There should have been 2 pages retrieved"
    pages |> List.iteri (fun idx pg ->
        Expect.equal pg.Text "" $"Page {idx} should have had no text"
        Expect.isEmpty pg.Metadata $"Page {idx} should have had no metadata"
        Expect.isEmpty pg.Revisions $"Page {idx} should have had no revisions"
        Expect.isEmpty pg.PriorPermalinks $"Page {idx} should have had no prior permalinks")
    let! others = data.Page.All (WebLogId "not-there")
    Expect.isEmpty others "There should not be pages retrieved"
}

let ``CountAll succeeds`` (data: IData) = task {
    let! pages = data.Page.CountAll rootId
    Expect.equal pages 2 "There should have been 2 pages counted"
}

let ``CountListed succeeds`` (data: IData) = task {
    let! pages = data.Page.CountListed rootId
    Expect.equal pages 1 "There should have been 1 page in the page list"
}

let ``FindById succeeds when a page is found`` (data: IData) = task {
    let! page = data.Page.FindById coolPageId rootId
    Expect.isSome page "A page should have been returned"
    let pg = page.Value
    Expect.equal pg.Id coolPageId "The wrong page was retrieved"
    Expect.equal pg.WebLogId rootId "The page's web log did not match the called parameter"
    Expect.equal pg.AuthorId (WebLogUserId "5EM2rimH9kONpmd2zQkiVA") "Author ID is incorrect"
    Expect.equal pg.Title "Page Title" "Title is incorrect"
    Expect.equal pg.Permalink (Permalink "a-cool-page.html") "Permalink is incorrect"
    Expect.equal pg.PublishedOn coolPagePublished "Published On is incorrect"
    Expect.equal pg.UpdatedOn coolPagePublished "Updated On is incorrect"
    Expect.isFalse pg.IsInPageList "Is in page list flag should not have been set"
    Expect.equal pg.Text "<h1 id=\"a-cool-page\">A Cool Page</h1>\n<p>It really is cool!</p>\n" "Text is incorrect"
    Expect.hasLength pg.Metadata 2 "There should be 2 metadata items on this page"
    Expect.equal pg.Metadata[0].Name "Cool" "Meta item 0 name is incorrect"
    Expect.equal pg.Metadata[0].Value "true" "Meta item 0 value is incorrect"
    Expect.equal pg.Metadata[1].Name "Warm" "Meta item 1 name is incorrect"
    Expect.equal pg.Metadata[1].Value "false" "Meta item 1 value is incorrect"
    Expect.isEmpty pg.Revisions "Revisions should not have been retrieved"
    Expect.isEmpty pg.PriorPermalinks "Prior permalinks should not have been retrieved"
}

let ``FindById succeeds when a page is not found (incorrect weblog)`` (data: IData) = task {
    let! page = data.Page.FindById coolPageId (WebLogId "wrong")
    Expect.isNone page "The page should not have been retrieved"
}

let ``FindById succeeds when a page is not found (bad page ID)`` (data: IData) = task {
    let! page = data.Page.FindById (PageId "missing") rootId
    Expect.isNone page "The page should not have been retrieved"
}

let ``FindByPermalink succeeds when a page is found`` (data: IData) = task {
    let! page = data.Page.FindByPermalink (Permalink "a-cool-page.html") rootId
    Expect.isSome page "A page should have been returned"
    let pg = page.Value
    Expect.equal pg.Id coolPageId "The wrong page was retrieved"
}

let ``FindByPermalink succeeds when a page is not found (incorrect weblog)`` (data: IData) = task {
    let! page = data.Page.FindByPermalink (Permalink "a-cool-page.html") (WebLogId "wrong")
    Expect.isNone page "The page should not have been retrieved"
}

let ``FindByPermalink succeeds when a page is not found (no such permalink)`` (data: IData) = task {
    let! page = data.Page.FindByPermalink (Permalink "1970/no-www-then.html") rootId
    Expect.isNone page "The page should not have been retrieved"
}

let ``FindCurrentPermalink succeeds when a page is found`` (data: IData) = task {
    let! link = data.Page.FindCurrentPermalink [ Permalink "a-cool-pg.html"; Permalink "a-cool-pg.html/" ] rootId
    Expect.isSome link "A permalink should have been returned"
    Expect.equal link (Some (Permalink "a-cool-page.html")) "The wrong permalink was retrieved"
}

let ``FindCurrentPermalink succeeds when a page is not found`` (data: IData) = task {
    let! link = data.Page.FindCurrentPermalink [ Permalink "blah/"; Permalink "blah" ] rootId
    Expect.isNone link "A permalink should not have been returned"
}

let ``FindFullById succeeds when a page is found`` (data: IData) = task {
    let! page = data.Page.FindFullById coolPageId rootId
    Expect.isSome page "A page should have been returned"
    let pg = page.Value
    Expect.equal pg.Id coolPageId "The wrong page was retrieved"
    Expect.equal pg.WebLogId rootId "The page's web log did not match the called parameter"
    Expect.hasLength pg.Revisions 1 "There should be 1 revision"
    Expect.equal pg.Revisions[0].AsOf coolPagePublished "Revision 0 as-of is incorrect"
    Expect.equal pg.Revisions[0].Text (Markdown "# A Cool Page\n\nIt really is cool!") "Revision 0 text is incorrect"
    Expect.hasLength pg.PriorPermalinks 1 "There should be 1 prior permalink"
    Expect.equal pg.PriorPermalinks[0] (Permalink "a-cool-pg.html") "Prior permalink 0 is incorrect"
}

let ``FindFullById succeeds when a page is not found`` (data: IData) = task {
    let! page = data.Page.FindFullById (PageId "not-there") rootId
    Expect.isNone page "A page should not have been retrieved"
}

let ``FindFullByWebLog succeeds when pages are found`` (data: IData) = task {
    let! pages = data.Page.FindFullByWebLog rootId
    Expect.hasLength pages 2 "There should have been 2 pages returned"
    pages |> List.iter (fun pg ->
        Expect.contains [ coolPageId; otherPageId ] pg.Id $"Page ID {pg.Id} unexpected"
        if pg.Id = coolPageId then
            Expect.isNonEmpty pg.Metadata "Metadata should have been retrieved"
            Expect.isNonEmpty pg.PriorPermalinks "Prior permalinks should have been retrieved"
        Expect.isNonEmpty pg.Revisions "Revisions should have been retrieved")
}

let ``FindFullByWebLog succeeds when pages are not found`` (data: IData) = task {
    let! pages = data.Page.FindFullByWebLog (WebLogId "does-not-exist")
    Expect.isEmpty pages "No pages should have been retrieved"
}

let ``FindListed succeeds when pages are found`` (data: IData) = task {
    let! pages = data.Page.FindListed rootId
    Expect.hasLength pages 1 "There should have been 1 page returned"
    Expect.equal pages[0].Id otherPageId "An unexpected page was returned"
    Expect.equal pages[0].Text "" "Text should not have been returned"
    Expect.isEmpty pages[0].PriorPermalinks "Prior permalinks should not have been retrieved"
    Expect.isEmpty pages[0].Revisions "Revisions should not have been retrieved"
}

let ``FindListed succeeds when pages are not found`` (data: IData) = task {
    let! pages = data.Page.FindListed (WebLogId "none")
    Expect.isEmpty pages "No pages should have been retrieved"
}
