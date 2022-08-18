namespace MyWebLog.Data.PostgreSql

open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp

/// PostgreSQL myWebLog page data implementation        
type PostgreSqlPageData (conn : NpgsqlConnection) =
    
    // SUPPORT FUNCTIONS
    
    /// Append revisions and permalinks to a page
    let appendPageRevisions (page : Page) = backgroundTask {
        let! revisions =
            Sql.existingConnection conn
            |> Sql.query "SELECT as_of, revision_text FROM page_revision WHERE page_id = @pageId ORDER BY as_of DESC"
            |> Sql.parameters [ "@pageId", Sql.string (PageId.toString page.Id) ]
            |> Sql.executeAsync Map.toRevision
        return { page with Revisions = revisions }
    }
    
    /// Return a page with no text or revisions
    let pageWithoutText row =
        { Map.toPage row with Text = "" }
    
    /// The INSERT statement for a page revision
    let revInsert = "INSERT INTO page_revision VALUES (@pageId, @asOf, @text)"
    
    /// Parameters for a revision INSERT statement
    let revParams pageId rev = [
        "@pageId", Sql.string      (PageId.toString pageId)
        "@asOf",   Sql.timestamptz rev.AsOf
        "@text",   Sql.string      (MarkupText.toString rev.Text)
    ]
    
    /// Update a page's revisions
    let updatePageRevisions pageId oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = Utils.diffRevisions oldRevs newRevs
        if not (List.isEmpty toDelete) || not (List.isEmpty toAdd) then
            let! _ =
                Sql.existingConnection conn
                |> Sql.executeTransactionAsync [
                    if not (List.isEmpty toDelete) then
                        "DELETE FROM page_revision WHERE page_id = @pageId AND as_of = @asOf",
                        toDelete
                        |> List.map (fun it -> [
                            "@pageId", Sql.string      (PageId.toString pageId)
                            "@asOf",   Sql.timestamptz it.AsOf
                        ])
                    if not (List.isEmpty toAdd) then
                        revInsert, toAdd |> List.map (revParams pageId)
                ]
            ()
    }
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Get all pages for a web log (without text, revisions, prior permalinks, or metadata)
    let all webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM page WHERE web_log_id = @webLogId ORDER BY LOWER(title)"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync pageWithoutText
    
    /// Count all pages for the given web log
    let countAll webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT COUNT(id) AS the_count FROM page WHERE web_log_id = @webLogId"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Count all pages shown in the page list for the given web log
    let countListed webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT COUNT(id) AS the_count FROM page WHERE web_log_id = @webLogId AND is_in_page_list = TRUE"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toCount
    
    /// Find a page by its ID (without revisions)
    let findById pageId webLogId = backgroundTask {
        let! page =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM page WHERE id = @id AND web_log_id = @webLogId"
            |> Sql.parameters [ "@id", Sql.string (PageId.toString pageId); webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toPage
        return List.tryHead page
    }
    
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
        match! findById pageId webLogId with
        | Some _ ->
            let! _ =
                Sql.existingConnection conn
                |> Sql.query """
                    DELETE FROM page_revision WHERE page_id = @id;
                    DELETE FROM page          WHERE id      = @id"""
                |> Sql.parameters [ "@id", Sql.string (PageId.toString pageId) ]
                |> Sql.executeNonQueryAsync
            return true
        | None -> return false
    }
    
    /// Find a page by its permalink for the given web log
    let findByPermalink permalink webLogId = backgroundTask {
        let! page =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM page WHERE web_log_id = @webLogId AND permalink = @link"
            |> Sql.parameters [ webLogIdParam webLogId; "@link", Sql.string (Permalink.toString permalink) ]
            |> Sql.executeAsync Map.toPage
        return List.tryHead page
    }
    
    /// Find the current permalink within a set of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        if List.isEmpty permalinks then return None
        else
            let linkSql, linkParams = priorPermalinkSql permalinks
            let! links =
                Sql.existingConnection conn
                |> Sql.query $"SELECT permalink FROM page WHERE web_log_id = @webLogId AND ({linkSql})"
                |> Sql.parameters (webLogIdParam webLogId :: linkParams)
                |> Sql.executeAsync Map.toPermalink
            return List.tryHead links
    }
    
    /// Get all complete pages for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        let! pages =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM page WHERE web_log_id = @webLogId"
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeAsync Map.toPage
        let! revisions =
            Sql.existingConnection conn
            |> Sql.query """
                SELECT *
                  FROM page_revision pr
                       INNER JOIN page p ON p.id = pr.page_id
                 WHERE p.web_log_id = @webLogId
                 ORDER BY pr.as_of DESC"""
            |> Sql.parameters [ webLogIdParam webLogId ]
            |> Sql.executeAsync (fun row -> PageId (row.string "page_id"), Map.toRevision row)
        return
            pages
            |> List.map (fun it ->
                { it with Revisions = revisions |> List.filter (fun r -> fst r = it.Id) |> List.map snd })
    }
    
    /// Get all listed pages for the given web log (without revisions or text)
    let findListed webLogId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM page WHERE web_log_id = @webLogId AND is_in_page_list = TRUE ORDER BY LOWER(title)"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync pageWithoutText
    
    /// Get a page of pages for the given web log (without revisions)
    let findPageOfPages webLogId pageNbr =
        Sql.existingConnection conn
        |> Sql.query"""
            SELECT *
              FROM page
             WHERE web_log_id = @webLogId
             ORDER BY LOWER(title)
             LIMIT @pageSize OFFSET @toSkip"""
        |> Sql.parameters [ webLogIdParam webLogId; "@pageSize", Sql.int 26; "@toSkip", Sql.int ((pageNbr - 1) * 25) ]
        |> Sql.executeAsync Map.toPage
    
    /// The INSERT statement for a page
    let pageInsert = """
        INSERT INTO page (
            id, web_log_id, author_id, title, permalink, prior_permalinks, published_on, updated_on, is_in_page_list,
            template, page_text, meta_items
        ) VALUES (
            @id, @webLogId, @authorId, @title, @permalink, @priorPermalinks, @publishedOn, @updatedOn, @isInPageList,
            @template, @text, @metaItems
        )"""
    
    /// The parameters for saving a page
    let pageParams (page : Page) = [
        webLogIdParam page.WebLogId
        "@id",              Sql.string       (PageId.toString page.Id)
        "@authorId",        Sql.string       (WebLogUserId.toString page.AuthorId)
        "@title",           Sql.string       page.Title
        "@permalink",       Sql.string       (Permalink.toString page.Permalink)
        "@publishedOn",     Sql.timestamptz  page.PublishedOn
        "@updatedOn",       Sql.timestamptz  page.UpdatedOn
        "@isInPageList",    Sql.bool         page.IsInPageList
        "@template",        Sql.stringOrNone page.Template
        "@text",            Sql.string       page.Text
        "@metaItems",       Sql.jsonb        (JsonConvert.SerializeObject page.Metadata)
        "@priorPermalinks", Sql.stringArray  (page.PriorPermalinks |> List.map Permalink.toString |> Array.ofList)
    ]

    /// Save a page
    let save (page : Page) = backgroundTask {
        let! oldPage = findFullById page.Id page.WebLogId
        let! _ =
            Sql.existingConnection conn
            |> Sql.query $"""
                {pageInsert} ON CONFLICT (id) DO UPDATE
                SET author_id        = EXCLUDED.author_id,
                    title            = EXCLUDED.title,
                    permalink        = EXCLUDED.permalink,
                    prior_permalinks = EXCLUDED.prior_permalinks,
                    published_on     = EXCLUDED.published_on,
                    updated_on       = EXCLUDED.updated_on,
                    is_in_page_list  = EXCLUDED.is_in_page_list,
                    template         = EXCLUDED.template,
                    page_text        = EXCLUDED.text,
                    meta_items       = EXCLUDED.meta_items"""
            |> Sql.parameters (pageParams page)
            |> Sql.executeNonQueryAsync
        do! updatePageRevisions page.Id (match oldPage with Some p -> p.Revisions | None -> []) page.Revisions
        ()
    }
    
    /// Restore pages from a backup
    let restore (pages : Page list) = backgroundTask {
        let revisions = pages |> List.collect (fun p -> p.Revisions |> List.map (fun r -> p.Id, r))
        let! _ =
            Sql.existingConnection conn
            |> Sql.executeTransactionAsync [
                pageInsert, pages     |> List.map pageParams
                revInsert,  revisions |> List.map (fun (pageId, rev) -> revParams pageId rev)
            ]
        ()
    }
    
    /// Update a page's prior permalinks
    let updatePriorPermalinks pageId webLogId permalinks = backgroundTask {
        match! findById pageId webLogId with
        | Some _ ->
            let! _ =
                Sql.existingConnection conn
                |> Sql.query "UPDATE page SET prior_permalinks = @prior WHERE id = @id"
                |> Sql.parameters
                    [   "@id",    Sql.string      (PageId.toString pageId)
                        "@prior", Sql.stringArray (permalinks |> List.map Permalink.toString |> Array.ofList) ]
                |> Sql.executeNonQueryAsync
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
