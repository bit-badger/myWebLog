namespace MyWebLog.Data.Postgres

open BitBadger.Documents
open BitBadger.Documents.Postgres
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql.FSharp

/// PostgreSQL myWebLog page data implementation
type PostgresPageData(log: ILogger) =
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions to a page
    let appendPageRevisions (page: Page) = backgroundTask {
        log.LogTrace "Page.appendPageRevisions"
        let! revisions = Revisions.findByEntityId Table.PageRevision Table.Page page.Id
        return { page with Revisions = revisions }
    }
    
    /// Return a page with no text or revisions
    let pageWithoutText (row: RowReader) =
        { fromData<Page> row with Text = "" }
    
    /// Update a page's revisions
    let updatePageRevisions (pageId: PageId) oldRevs newRevs =
        log.LogTrace "Page.updatePageRevisions"
        Revisions.update Table.PageRevision Table.Page pageId oldRevs newRevs
    
    /// Does the given page exist?
    let pageExists (pageId: PageId) webLogId =
        log.LogTrace "Page.pageExists"
        Document.existsByWebLog Table.Page pageId webLogId
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a page
    let add (page: Page) = backgroundTask {
        log.LogTrace "Page.add"
        do! insert Table.Page { page with Revisions = [] }
        do! updatePageRevisions page.Id [] page.Revisions
        ()
    }
    
    /// Get all pages for a web log (without text, metadata, revisions, or prior permalinks)
    let all webLogId =
        log.LogTrace "Page.all"
        Custom.list
            $"{selectWithCriteria Table.Page} ORDER BY LOWER(data ->> '{nameof Page.Empty.Title}')"
            [ webLogContains webLogId ]
            (fun row -> { fromData<Page> row with Text = ""; Metadata = []; PriorPermalinks = [] }) 
    
    /// Count all pages for the given web log
    let countAll webLogId =
        log.LogTrace "Page.countAll"
        Count.byContains Table.Page (webLogDoc webLogId)
    
    /// Count all pages shown in the page list for the given web log
    let countListed webLogId =
        log.LogTrace "Page.countListed"
        Count.byContains Table.Page {| webLogDoc webLogId with IsInPageList = true |}
    
    /// Find a page by its ID (without revisions or prior permalinks)
    let findById pageId webLogId = backgroundTask {
        log.LogTrace "Page.findById"
        match! Document.findByIdAndWebLog<PageId, Page> Table.Page pageId webLogId with
        | Some page -> return Some { page with PriorPermalinks = [] }
        | None -> return None
    }
    
    /// Find a complete page by its ID
    let findFullById pageId webLogId = backgroundTask {
        log.LogTrace "Page.findFullById"
        match! Document.findByIdAndWebLog<PageId, Page> Table.Page pageId webLogId with
        | Some page ->
            let! withMore = appendPageRevisions page
            return Some withMore
        | None -> return None
    }
    
    // TODO: need to handle when the page being deleted is the home page
    /// Delete a page by its ID
    let delete pageId webLogId = backgroundTask {
        log.LogTrace "Page.delete"
        match! pageExists pageId webLogId with
        | true ->
            do! Custom.nonQuery
                    $"""DELETE FROM {Table.PageRevision} WHERE page_id = @id;
                        DELETE FROM {Table.Page}         WHERE {Query.whereById "@id"}"""
                    [ idParam pageId ]
            return true
        | false -> return false
    }
    
    /// Find a page by its permalink for the given web log
    let findByPermalink (permalink: Permalink) webLogId = backgroundTask {
        log.LogTrace "Page.findByPermalink"
        let! page =
            Find.byContains<Page> Table.Page {| webLogDoc webLogId with Permalink = permalink |}
            |> tryHead
        return page |> Option.map (fun pg -> { pg with PriorPermalinks = [] })
    }
    
    /// Find the current permalink within a set of potential prior permalinks for the given web log
    let findCurrentPermalink (permalinks: Permalink list) webLogId = backgroundTask {
        log.LogTrace "Page.findCurrentPermalink"
        if List.isEmpty permalinks then return None
        else
            let linkSql, linkParam = arrayContains (nameof Page.Empty.PriorPermalinks) string permalinks
            return!
                Custom.single
                    $"""SELECT data ->> '{nameof Page.Empty.Permalink}' AS permalink
                          FROM page
                         WHERE {Query.whereDataContains "@criteria"}
                           AND {linkSql}"""
                    [ webLogContains webLogId; linkParam ]
                    Map.toPermalink
    }
    
    /// Get all complete pages for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        log.LogTrace "Page.findFullByWebLog"
        let! pages     = Document.findByWebLog<Page> Table.Page webLogId
        let! revisions = Revisions.findByWebLog Table.PageRevision Table.Page PageId webLogId 
        return
            pages
            |> List.map (fun it ->
                { it with Revisions = revisions |> List.filter (fun r -> fst r = it.Id) |> List.map snd })
    }
    
    /// Get all listed pages for the given web log (without revisions or text)
    let findListed webLogId =
        log.LogTrace "Page.findListed"
        Custom.list
            $"{selectWithCriteria Table.Page} ORDER BY LOWER(data ->> '{nameof Page.Empty.Title}')"
            [ jsonParam "@criteria" {| webLogDoc webLogId with IsInPageList = true |} ]
            pageWithoutText
    
    /// Get a page of pages for the given web log (without revisions)
    let findPageOfPages webLogId pageNbr =
        log.LogTrace "Page.findPageOfPages"
        Custom.list
            $"{selectWithCriteria Table.Page}
               ORDER BY LOWER(data->>'{nameof Page.Empty.Title}')
               LIMIT @pageSize OFFSET @toSkip"
            [ webLogContains webLogId; "@pageSize", Sql.int 26; "@toSkip", Sql.int ((pageNbr - 1) * 25) ]
            (fun row -> { fromData<Page> row with Metadata = []; PriorPermalinks = [] })
    
    /// Restore pages from a backup
    let restore (pages: Page list) = backgroundTask {
        log.LogTrace "Page.restore"
        let revisions = pages |> List.collect (fun p -> p.Revisions |> List.map (fun r -> p.Id, r))
        let! _ =
            Configuration.dataSource ()
            |> Sql.fromDataSource
            |> Sql.executeTransactionAsync
                [ Query.insert Table.Page,
                    pages |> List.map (fun page -> [ jsonParam "@data" { page with Revisions = [] } ])
                  Revisions.insertSql Table.PageRevision,
                    revisions |> List.map (fun (pageId, rev) -> Revisions.revParams pageId rev) ]
        ()
    }
    
    /// Update a page
    let update (page: Page) = backgroundTask {
        log.LogTrace "Page.update"
        match! findFullById page.Id page.WebLogId with
        | Some oldPage ->
            do! Update.byId Table.Page page.Id { page with Revisions = [] }
            do! updatePageRevisions page.Id oldPage.Revisions page.Revisions
        | None -> ()
        ()
    }
    
    /// Update a page's prior permalinks
    let updatePriorPermalinks pageId webLogId (permalinks: Permalink list) = backgroundTask {
        log.LogTrace "Page.updatePriorPermalinks"
        match! pageExists pageId webLogId with
        | true ->
            do! Patch.byId Table.Page pageId {| PriorPermalinks = permalinks |}
            return true
        | false -> return false
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
