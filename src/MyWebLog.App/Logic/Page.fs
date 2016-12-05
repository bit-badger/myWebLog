/// Logic for manipulating <see cref="Page" /> entities
module MyWebLog.Logic.Page

open MyWebLog.Data
open MyWebLog.Entities

/// Find a page by its Id and web log Id
let tryFindPage (data : IMyWebLogData) webLogId pageId = data.PageById webLogId pageId true

/// Find a page by its Id and web log Id, without the revision list
let tryFindPageWithoutRevisions (data : IMyWebLogData) webLogId pageId = data.PageById webLogId pageId false

/// Find a page by its permalink
let tryFindPageByPermalink (data : IMyWebLogData) webLogId permalink = data.PageByPermalink webLogId permalink

/// Find a list of all pages (excludes text and revisions)
let findAllPages (data : IMyWebLogData) webLogId = data.AllPages webLogId

/// Save a page
let savePage (data : IMyWebLogData) (page : Page) =
  match page.Id with
  | "new" -> let newPg = { page with Id = string <| System.Guid.NewGuid () }
             data.AddPage newPg
             newPg.Id
  | _ -> data.UpdatePage page
         page.Id

/// Delete a page
let deletePage (data : IMyWebLogData) webLogId pageId = data.DeletePage webLogId pageId
