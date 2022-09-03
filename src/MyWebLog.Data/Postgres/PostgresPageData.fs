namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog page data implementation        
type PostgresPageData (conn : NpgsqlConnection, ser : JsonSerializer) =
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions to a page
    let appendPageRevisions (page : Page) = backgroundTask {
        let! revisions = Revisions.findByEntityId conn Table.PageRevision Table.Page page.Id PageId.toString
        return { page with Revisions = revisions }
    }
    
    /// Shorthand to map to a page
    let toPage = Map.fromDoc<Page> ser
    
    /// Return a page with no text or revisions
    let pageWithoutText row =
        { toPage row with Text = "" }
    
    /// Update a page's revisions
    let updatePageRevisions pageId oldRevs newRevs =
        Revisions.update conn Table.PageRevision Table.Page pageId PageId.toString oldRevs newRevs
    
    /// Does the given page exist?
    let pageExists pageId webLogId =
        Document.existsByWebLog conn Table.Page pageId PageId.toString webLogId
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Get all pages for a web log (without text or revisions)
    let all webLogId =
        Document.findByWebLog conn Table.Page webLogId pageWithoutText
            (Some $"ORDER BY LOWER(data ->> '{nameof Page.empty.Title}')")
    
    /// Count all pages for the given web log
    let countAll webLogId =
        Document.countByWebLog conn Table.Page webLogId None
    
    /// Count all pages shown in the page list for the given web log
    let countListed webLogId =
        Document.countByWebLog conn Table.Page webLogId (Some $"AND data -> '{nameof Page.empty.IsInPageList}' = TRUE")
    
    /// Find a page by its ID (without revisions)
    let findById pageId webLogId =
        Document.findByIdAndWebLog conn Table.Page pageId PageId.toString webLogId toPage
    
    /// Find a complete page by its ID
    let findFullById pageId webLogId = backgroundTask {
        match! findById pageId webLogId with
        | Some page ->
            let! withMore = appendPageRevisions page
            return Some withMore
        | None -> return None
    }
    
    /// Delete a page by its ID
    let delete pageId webLogId = backgroundTask {
        match! pageExists pageId webLogId with
        | true ->
            do! Document.delete conn Table.Page (PageId.toString pageId)
            return true
        | false -> return false
    }
    
    /// Find a page by its permalink for the given web log
    let findByPermalink permalink webLogId =
        Sql.existingConnection conn
        |> Sql.query $"{docSelectForWebLogSql Table.Page} AND data ->> '{nameof Page.empty.Permalink}' = @link"
        |> Sql.parameters [ webLogIdParam webLogId; "@link", Sql.string (Permalink.toString permalink) ]
        |> Sql.executeAsync toPage
        |> tryHead
    
    /// Find the current permalink within a set of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        if List.isEmpty permalinks then return None
        else
            let linkSql, linkParams =
                jsonArrayInClause (nameof Page.empty.PriorPermalinks) Permalink.toString permalinks
            return!
                Sql.existingConnection conn
                |> Sql.query $"
                    SELECT data ->> '{nameof Page.empty.Permalink}' AS permalink
                      FROM page
                     WHERE {webLogWhere}
                       AND ({linkSql})"
                |> Sql.parameters (webLogIdParam webLogId :: linkParams)
                |> Sql.executeAsync Map.toPermalink
                |> tryHead
    }
    
    /// Get all complete pages for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        let! pages     = Document.findByWebLog conn Table.Page webLogId toPage None
        let! revisions = Revisions.findByWebLog conn Table.PageRevision Table.Page PageId webLogId 
        return
            pages
            |> List.map (fun it ->
                { it with Revisions = revisions |> List.filter (fun r -> fst r = it.Id) |> List.map snd })
    }
    
    /// Get all listed pages for the given web log (without revisions or text)
    let findListed webLogId =
        Sql.existingConnection conn
        |> Sql.query $"
            {docSelectForWebLogSql Table.Page}
               AND data -> '{nameof Page.empty.IsInPageList}' = TRUE
             ORDER BY LOWER(data ->> '{nameof Page.empty.Title}')"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync pageWithoutText
    
    /// Get a page of pages for the given web log (without revisions)
    let findPageOfPages webLogId pageNbr =
        Sql.existingConnection conn
        |> Sql.query $"
            {docSelectForWebLogSql Table.Page}
             ORDER BY LOWER(data ->> '{nameof Page.empty.Title}')
             LIMIT @pageSize OFFSET @toSkip"
        |> Sql.parameters [ webLogIdParam webLogId; "@pageSize", Sql.int 26; "@toSkip", Sql.int ((pageNbr - 1) * 25) ]
        |> Sql.executeAsync toPage
    
    /// The parameters for saving a page
    let pageParams (page : Page) = [
        "@id",   Sql.string (PageId.toString page.Id)
        "@data", Sql.jsonb  (Utils.serialize ser page)
    ]

    /// Restore pages from a backup
    let restore (pages : Page list) = backgroundTask {
        let revisions = pages |> List.collect (fun p -> p.Revisions |> List.map (fun r -> p.Id, r))
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                docInsertSql Table.Page, pages |> List.map pageParams
                Revisions.insertSql Table.PageRevision,
                    revisions |> List.map (fun (pageId, rev) -> Revisions.revParams pageId PageId.toString rev)
            ]
        ()
    }
    
    /// Save a page
    let save (page : Page) = backgroundTask {
        let! oldPage = findFullById page.Id page.WebLogId
        do! Document.upsert conn Table.Page pageParams page
        do! updatePageRevisions page.Id (match oldPage with Some p -> p.Revisions | None -> []) page.Revisions
        ()
    }
    
    /// Update a page's prior permalinks
    let updatePriorPermalinks pageId webLogId permalinks = backgroundTask {
        match! findById pageId webLogId with
        | Some page ->
            do! Document.update conn Table.Page pageParams { page with PriorPermalinks = permalinks }
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
