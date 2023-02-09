namespace MyWebLog.Data.Postgres

open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostgreSQL myWebLog page data implementation        
type PostgresPageData (source : NpgsqlDataSource, log : ILogger) =
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions to a page
    let appendPageRevisions (page : Page) = backgroundTask {
        log.LogTrace "Page.appendPageRevisions"
        let! revisions = Revisions.findByEntityId source Table.PageRevision Table.Page page.Id PageId.toString
        return { page with Revisions = revisions }
    }
    
    /// Return a page with no text or revisions
    let pageWithoutText (row : RowReader) =
        { fromData<Page> row with Text = "" }
    
    /// Update a page's revisions
    let updatePageRevisions pageId oldRevs newRevs =
        log.LogTrace "Page.updatePageRevisions"
        Revisions.update source Table.PageRevision Table.Page pageId PageId.toString oldRevs newRevs
    
    /// Does the given page exist?
    let pageExists pageId webLogId =
        log.LogTrace "Page.pageExists"
        Document.existsByWebLog source Table.Page pageId PageId.toString webLogId
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Get all pages for a web log (without text or revisions)
    let all webLogId =
        log.LogTrace "Page.all"
        Sql.fromDataSource source
        |> Sql.query $"{selectWithCriteria Table.Page} ORDER BY LOWER(data ->> '{nameof Page.empty.Title}')"
        |> Sql.parameters [ webLogContains webLogId ]
        |> Sql.executeAsync fromData<Page>
    
    /// Count all pages for the given web log
    let countAll webLogId =
        log.LogTrace "Page.countAll"
        Sql.fromDataSource source
        |> Query.countByContains Table.Page (webLogDoc webLogId)
    
    /// Count all pages shown in the page list for the given web log
    let countListed webLogId =
        log.LogTrace "Page.countListed"
        Sql.fromDataSource source
        |> Query.countByContains Table.Page {| webLogDoc webLogId with IsInPageList = true |}
    
    /// Find a page by its ID (without revisions)
    let findById pageId webLogId =
        log.LogTrace "Page.findById"
        Document.findByIdAndWebLog<PageId, Page> source Table.Page pageId PageId.toString webLogId
    
    /// Find a complete page by its ID
    let findFullById pageId webLogId = backgroundTask {
        log.LogTrace "Page.findFullById"
        match! findById pageId webLogId with
        | Some page ->
            let! withMore = appendPageRevisions page
            return Some withMore
        | None -> return None
    }
    
    /// Delete a page by its ID
    let delete pageId webLogId = backgroundTask {
        log.LogTrace "Page.delete"
        match! pageExists pageId webLogId with
        | true ->
            do! Sql.fromDataSource source |> Query.deleteById Table.Page (PageId.toString pageId)
            return true
        | false -> return false
    }
    
    /// Find a page by its permalink for the given web log
    let findByPermalink permalink webLogId =
        log.LogTrace "Page.findByPermalink"
        Sql.fromDataSource source
        |> Query.findByContains<Page> Table.Page {| webLogDoc webLogId with Permalink = Permalink.toString permalink |}
        |> tryHead
    
    /// Find the current permalink within a set of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        log.LogTrace "Page.findCurrentPermalink"
        if List.isEmpty permalinks then return None
        else
            let linkSql, linkParam =
                arrayContains (nameof Page.empty.PriorPermalinks) Permalink.toString permalinks
            return!
                Sql.fromDataSource source
                |> Sql.query $"""
                    SELECT data ->> '{nameof Page.empty.Permalink}' AS permalink
                      FROM page
                     WHERE {Query.whereDataContains "@criteria"}
                       AND {linkSql}"""
                |> Sql.parameters [ webLogContains webLogId; linkParam ]
                |> Sql.executeAsync Map.toPermalink
                |> tryHead
    }
    
    /// Get all complete pages for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        log.LogTrace "Page.findFullByWebLog"
        let! pages     = Document.findByWebLog<Page> source Table.Page webLogId
        let! revisions = Revisions.findByWebLog source Table.PageRevision Table.Page PageId webLogId 
        return
            pages
            |> List.map (fun it ->
                { it with Revisions = revisions |> List.filter (fun r -> fst r = it.Id) |> List.map snd })
    }
    
    /// Get all listed pages for the given web log (without revisions or text)
    let findListed webLogId =
        log.LogTrace "Page.findListed"
        Sql.fromDataSource source
        |> Sql.query $"{selectWithCriteria Table.Page} ORDER BY LOWER(data ->> '{nameof Page.empty.Title}')"
        |> Sql.parameters [ "@criteria", Query.jsonbDocParam {| webLogDoc webLogId with IsInPageList = true |} ]
        |> Sql.executeAsync pageWithoutText
    
    /// Get a page of pages for the given web log (without revisions)
    let findPageOfPages webLogId pageNbr =
        log.LogTrace "Page.findPageOfPages"
        Sql.fromDataSource source
        |> Sql.query $"
            {selectWithCriteria Table.Page}
             ORDER BY LOWER(data->>'{nameof Page.empty.Title}')
             LIMIT @pageSize OFFSET @toSkip"
        |> Sql.parameters [ webLogContains webLogId; "@pageSize", Sql.int 26; "@toSkip", Sql.int ((pageNbr - 1) * 25) ]
        |> Sql.executeAsync fromData<Page>
    
    /// Restore pages from a backup
    let restore (pages : Page list) = backgroundTask {
        log.LogTrace "Page.restore"
        let revisions = pages |> List.collect (fun p -> p.Revisions |> List.map (fun r -> p.Id, r))
        let! _ =
            Sql.fromDataSource source
            |> Sql.executeTransactionAsync [
                Query.insertQuery Table.Page,
                pages
                |> List.map (fun page -> Query.docParameters (PageId.toString page.Id) { page with Revisions = [] })
                Revisions.insertSql Table.PageRevision,
                    revisions |> List.map (fun (pageId, rev) -> Revisions.revParams pageId PageId.toString rev)
            ]
        ()
    }
    
    /// Save a page
    let save (page : Page) = backgroundTask {
        log.LogTrace "Page.save"
        let! oldPage = findFullById page.Id page.WebLogId
        do! Sql.fromDataSource source |> Query.save Table.Page (PageId.toString page.Id) { page with Revisions = [] }
        do! updatePageRevisions page.Id (match oldPage with Some p -> p.Revisions | None -> []) page.Revisions
        ()
    }
    
    /// Update a page's prior permalinks
    let updatePriorPermalinks pageId webLogId permalinks = backgroundTask {
        log.LogTrace "Page.updatePriorPermalinks"
        match! findById pageId webLogId with
        | Some page ->
            do! Sql.fromDataSource source
                |> Query.update Table.Page (PageId.toString page.Id) { page with PriorPermalinks = permalinks }
            return true
        | None -> return false
    }
    
    interface IPageData with
        member _.Add page = save page
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
        member _.Update page = save page
        member _.UpdatePriorPermalinks pageId webLogId permalinks = updatePriorPermalinks pageId webLogId permalinks
