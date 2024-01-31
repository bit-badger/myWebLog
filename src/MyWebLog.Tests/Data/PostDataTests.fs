/// <summary>
/// Integration tests for <see cref="IPostData" /> implementations
/// </summary> 
module PostDataTests

open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// The ID of the root web log
let rootId = WebLogId "uSitJEuD3UyzWC9jgOHc8g"

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
