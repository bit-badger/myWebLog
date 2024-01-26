module PageDataTests

open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

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
