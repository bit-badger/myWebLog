namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open BitBadger.Documents
open BitBadger.Documents.Sqlite
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog page data implementation
type SQLitePageData(conn: SqliteConnection, log: ILogger) =
    
    /// The JSON field name for the permalink
    let linkName = nameof Page.Empty.Permalink
    
    /// The JSON field name for the "is in page list" flag
    let pgListName = nameof Page.Empty.IsInPageList
    
    /// The JSON field for the title of the page
    let titleField = $"data ->> '{nameof Page.Empty.Title}'"
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions to a page
    let appendPageRevisions (page : Page) = backgroundTask {
        log.LogTrace "Page.appendPageRevisions"
        let! revisions = Revisions.findByEntityId Table.PageRevision Table.Page page.Id conn
        return { page with Revisions = revisions }
    }
    
    /// Update a page's revisions
    let updatePageRevisions (pageId: PageId) oldRevs newRevs =
        log.LogTrace "Page.updatePageRevisions"
        Revisions.update Table.PageRevision Table.Page pageId oldRevs newRevs conn
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a page
    let add (page: Page) = backgroundTask {
        log.LogTrace "Page.add"
        do! conn.insert Table.Page { page with Revisions = [] } 
        do! updatePageRevisions page.Id [] page.Revisions
    }
    
    /// Get all pages for a web log (without text, metadata, revisions, or prior permalinks)
    let all webLogId =
        log.LogTrace "Page.all"
        conn.customList
            $"{Query.selectFromTable Table.Page} WHERE {Document.Query.whereByWebLog} ORDER BY LOWER({titleField})"
            [ webLogParam webLogId ]
            (fun rdr -> { fromData<Page> rdr with Text = ""; Metadata = []; PriorPermalinks = [] })
    
    /// Count all pages for the given web log
    let countAll webLogId =
        log.LogTrace "Page.countAll"
        Document.countByWebLog Table.Page webLogId conn
    
    /// Count all pages shown in the page list for the given web log
    let countListed webLogId =
        log.LogTrace "Page.countListed"
        conn.customScalar
            $"""{Document.Query.countByWebLog Table.Page} AND {Query.whereByField (Field.EQ pgListName "") "true"}"""
            [ webLogParam webLogId ]
            (toCount >> int)
    
    /// Find a page by its ID (without revisions and prior permalinks)
    let findById pageId webLogId = backgroundTask {
        log.LogTrace "Page.findById"
        match! Document.findByIdAndWebLog<PageId, Page> Table.Page pageId webLogId conn with
        | Some page -> return Some { page with PriorPermalinks = [] }
        | None -> return None
    }
    
    /// Find a complete page by its ID
    let findFullById pageId webLogId = backgroundTask {
        log.LogTrace "Page.findFullById"
        match! Document.findByIdAndWebLog<PageId, Page> Table.Page pageId webLogId conn with
        | Some page ->
            let! page = appendPageRevisions page
            return Some page
        | None -> return None
    }
    
    // TODO: need to handle when the page being deleted is the home page
    /// Delete a page by its ID
    let delete pageId webLogId = backgroundTask {
        log.LogTrace "Page.delete"
        match! findById pageId webLogId with
        | Some _ ->
            do! conn.customNonQuery
                    $"DELETE FROM {Table.PageRevision} WHERE page_id = @id; {Query.Delete.byId Table.Page}"
                    [ idParam pageId ]
            return true
        | None -> return false
    }
    
    /// Find a page by its permalink for the given web log
    let findByPermalink (permalink: Permalink) webLogId =
        log.LogTrace "Page.findByPermalink"
        let linkParam = Field.EQ linkName (string permalink)
        conn.customSingle
            $"""{Document.Query.selectByWebLog Table.Page} AND {Query.whereByField linkParam "@link"}"""
            (addFieldParam "@link" linkParam [ webLogParam webLogId ])
            fromData<Page>
    
    /// Find the current permalink within a set of potential prior permalinks for the given web log
    let findCurrentPermalink (permalinks: Permalink list) webLogId =
        log.LogTrace "Page.findCurrentPermalink"
        let linkSql, linkParams = inJsonArray Table.Page (nameof Page.Empty.PriorPermalinks) "link" permalinks
        conn.customSingle
            $"SELECT data ->> '{linkName}' AS permalink
                FROM {Table.Page}
               WHERE {Document.Query.whereByWebLog} AND {linkSql}"
            (webLogParam webLogId :: linkParams)
            Map.toPermalink
    
    /// Get all complete pages for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        log.LogTrace "Page.findFullByWebLog"
        let! pages    = Document.findByWebLog<Page> Table.Page webLogId conn
        let! withRevs = pages |> List.map appendPageRevisions |> Task.WhenAll
        return List.ofArray withRevs
    }
    
    /// Get all listed pages for the given web log (without revisions or text)
    let findListed webLogId =
        log.LogTrace "Page.findListed"
        conn.customList
            $"""{Document.Query.selectByWebLog Table.Page} AND {Query.whereByField (Field.EQ pgListName "") "true"}
                ORDER BY LOWER({titleField})"""
            [ webLogParam webLogId ]
            (fun rdr -> { fromData<Page> rdr with Text = "" })
    
    /// Get a page of pages for the given web log (without revisions)
    let findPageOfPages webLogId pageNbr =
        log.LogTrace "Page.findPageOfPages"
        conn.customList
            $"{Document.Query.selectByWebLog Table.Page} ORDER BY LOWER({titleField}) LIMIT @pageSize OFFSET @toSkip"
            [ webLogParam webLogId; SqliteParameter("@pageSize", 26); SqliteParameter("@toSkip", (pageNbr - 1) * 25) ]
            fromData<Page>
    
    /// Update a page
    let update (page: Page) = backgroundTask {
        log.LogTrace "Page.update"
        match! findFullById page.Id page.WebLogId with
        | Some oldPage ->
            do! conn.updateById Table.Page page.Id { page with Revisions = [] } 
            do! updatePageRevisions page.Id oldPage.Revisions page.Revisions
        | None -> ()
    }
    
    /// Restore pages from a backup
    let restore pages = backgroundTask {
        log.LogTrace "Page.restore"
        for page in pages do do! add page
    }
    
    /// Update a page's prior permalinks
    let updatePriorPermalinks pageId webLogId (permalinks: Permalink list) = backgroundTask {
        log.LogTrace "Page.updatePriorPermalinks"
        match! findById pageId webLogId with
        | Some _ ->
            do! conn.patchById Table.Page pageId {| PriorPermalinks = permalinks |}
            return true
         | None -> return false
    }
    
    interface IPageData with
        member _.Add page = add page
        member _.All webLogId = all webLogId
        member _.CountAll webLogId = countAll webLogId
        member _.CountListed webLogId = countListed webLogId
        member _.Delete pageId webLogId = delete pageId webLogId
        member _.FindById pageId webLogId = findById pageId webLogId
        member _.FindByPermalink permalink webLogId = findByPermalink permalink webLogId
        member _.FindCurrentPermalink permalinks webLogId = findCurrentPermalink permalinks webLogId
        member _.FindFullById pageId webLogId = findFullById pageId webLogId
        member _.FindFullByWebLog webLogId = findFullByWebLog webLogId
        member _.FindListed webLogId = findListed webLogId
        member _.FindPageOfPages webLogId pageNbr = findPageOfPages webLogId pageNbr
        member _.Restore pages = restore pages
        member _.Update page = update page
        member _.UpdatePriorPermalinks pageId webLogId permalinks = updatePriorPermalinks pageId webLogId permalinks
