namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json

/// SQLite myWebLog page data implementation
type SQLitePageData(conn: SqliteConnection, ser: JsonSerializer, log: ILogger) =
    
    /// The JSON field for the permalink
    let linkField = $"data ->> '{nameof Page.Empty.Permalink}'"
    
    /// The JSON field for the "is in page list" flag
    let pgListField = $"data ->> '{nameof Page.Empty.IsInPageList}'"
    
    /// The JSON field for the title of the page
    let titleField = $"data ->> '{nameof Page.Empty.Title}'"
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions to a page
    let appendPageRevisions (page : Page) = backgroundTask {
        log.LogTrace "Page.appendPageRevisions"
        let! revisions = Revisions.findByEntityId conn Table.PageRevision Table.Page page.Id
        return { page with Revisions = revisions }
    }
    
    /// Return a page with no text
    let withoutText (page: Page) =
        { page with Text = "" }
    
    /// Update a page's revisions
    let updatePageRevisions (pageId: PageId) oldRevs newRevs =
        log.LogTrace "Page.updatePageRevisions"
        Revisions.update conn Table.PageRevision Table.Page pageId oldRevs newRevs
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a page
    let add page = backgroundTask {
        log.LogTrace "Page.add"
        do! Document.insert<Page> conn ser Table.Page { page with Revisions = [] } 
        do! updatePageRevisions page.Id [] page.Revisions
    }
    
    /// Get all pages for a web log (without text or revisions)
    let all webLogId = backgroundTask {
        log.LogTrace "Page.all"
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
            $"{Query.selectFromTable Table.Page} WHERE {Query.whereByWebLog} ORDER BY LOWER({titleField})"
        addWebLogId cmd webLogId
        let! pages = cmdToList<Page> cmd ser
        return pages |> List.map withoutText
    }
    
    /// Count all pages for the given web log
    let countAll webLogId =
        log.LogTrace "Page.countAll"
        Document.countByWebLog conn Table.Page webLogId
    
    /// Count all pages shown in the page list for the given web log
    let countListed webLogId = backgroundTask {
        log.LogTrace "Page.countListed"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"{Query.countByWebLog} AND {pgListField} = 'true'"
        addWebLogId cmd webLogId
        return! count cmd
    }
    
    /// Find a page by its ID (without revisions)
    let findById pageId webLogId =
        log.LogTrace "Page.findById"
        Document.findByIdAndWebLog<PageId, Page> conn ser Table.Page pageId webLogId
    
    /// Find a complete page by its ID
    let findFullById pageId webLogId = backgroundTask {
        log.LogTrace "Page.findFullById"
        match! findById pageId webLogId with
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
            use cmd = conn.CreateCommand()
            cmd.CommandText <- $"DELETE FROM {Table.PageRevision} WHERE page_id = @id; {Query.deleteById}"
            addDocId cmd pageId
            do! write cmd
            return true
        | None -> return false
    }
    
    /// Find a page by its permalink for the given web log
    let findByPermalink (permalink: Permalink) webLogId = backgroundTask {
        log.LogTrace "Page.findByPermalink"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $" {Query.selectFromTable Table.Page} WHERE {Query.whereByWebLog} AND {linkField} = @link"
        addWebLogId cmd webLogId
        addParam cmd "@link" (string permalink)
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.fromDoc<Page> ser rdr) else None
    }
    
    /// Find the current permalink within a set of potential prior permalinks for the given web log
    let findCurrentPermalink (permalinks: Permalink list) webLogId = backgroundTask {
        log.LogTrace "Page.findCurrentPermalink"
        let linkSql, linkParams = inJsonArray Table.Page (nameof Page.Empty.PriorPermalinks) "link" permalinks
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
            $"SELECT {linkField} AS permalink FROM {Table.Page} WHERE {Query.whereByWebLog} AND {linkSql}"
        addWebLogId cmd webLogId
        cmd.Parameters.AddRange linkParams
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.toPermalink rdr) else None
    }
    
    /// Get all complete pages for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        log.LogTrace "Page.findFullByWebLog"
        let! pages = Document.findByWebLog<Page> conn ser Table.Page webLogId
        let! withRevs =
            pages
            |> List.map (fun page -> backgroundTask { return! appendPageRevisions page })
            |> Task.WhenAll
        return List.ofArray withRevs
    }
    
    /// Get all listed pages for the given web log (without revisions or text)
    let findListed webLogId = backgroundTask {
        log.LogTrace "Page.findListed"
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"
            {Query.selectFromTable Table.Page}
              WHERE {Query.whereByWebLog}
                AND {pgListField} = 'true'
              ORDER BY LOWER({titleField})"
        addWebLogId cmd webLogId
        let! pages = cmdToList<Page> cmd ser
        return pages |> List.map withoutText
    }
    
    /// Get a page of pages for the given web log (without revisions)
    let findPageOfPages webLogId pageNbr =
        log.LogTrace "Page.findPageOfPages"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"
            {Query.selectFromTable Table.Page} WHERE {Query.whereByWebLog}
             ORDER BY LOWER({titleField})
             LIMIT @pageSize OFFSET @toSkip"
        addWebLogId cmd webLogId
        addParam cmd "@pageSize" 26
        addParam cmd "@toSkip"   ((pageNbr - 1) * 25)
        cmdToList<Page> cmd ser
    
    /// Restore pages from a backup
    let restore pages = backgroundTask {
        log.LogTrace "Page.restore"
        for page in pages do
            do! add page
    }
    
    /// Update a page
    let update (page: Page) = backgroundTask {
        log.LogTrace "Page.update"
        match! findFullById page.Id page.WebLogId with
        | Some oldPage ->
            do! Document.update conn ser Table.Page page.Id { page with Revisions = [] } 
            do! updatePageRevisions page.Id oldPage.Revisions page.Revisions
        | None -> ()
    }
    
    /// Update a page's prior permalinks
    let updatePriorPermalinks pageId webLogId (permalinks: Permalink list) = backgroundTask {
        log.LogTrace "Page.updatePriorPermalinks"
        match! findById pageId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand()
            cmd.CommandText <- $"
                UPDATE {Table.Page}
                   SET data = json_set(data, '$.{nameof Page.Empty.PriorPermalinks}', json(@links))
                 WHERE {Query.whereById}"
            addDocId cmd pageId
            addParam cmd "@links" (Utils.serialize ser permalinks)
            do! write cmd
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
