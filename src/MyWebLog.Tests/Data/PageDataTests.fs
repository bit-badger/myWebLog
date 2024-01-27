module PageDataTests

open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// The ID of the root web log
let rootId = WebLogId "uSitJEuD3UyzWC9jgOHc8g"

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
}

let ``CountAll succeeds`` (data: IData) = task {
    let! pages = data.Page.CountAll rootId
    Expect.equal pages 2 "There should have been 2 pages counted"
}

let ``CountListed succeeds`` (data: IData) = task {
    let! pages = data.Page.CountListed rootId
    Expect.equal pages 1 "There should have been 1 page in the page list"
}
