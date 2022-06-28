namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog page data implementation        
type SQLitePageData (conn : SqliteConnection) =
    
    // SUPPORT FUNCTIONS
    
    /// Add parameters for page INSERT or UPDATE statements
    let addPageParameters (cmd : SqliteCommand) (page : Page) =
        [ cmd.Parameters.AddWithValue ("@id", PageId.toString page.id)
          cmd.Parameters.AddWithValue ("@webLogId", WebLogId.toString page.webLogId)
          cmd.Parameters.AddWithValue ("@authorId", WebLogUserId.toString page.authorId)
          cmd.Parameters.AddWithValue ("@title", page.title)
          cmd.Parameters.AddWithValue ("@permalink", Permalink.toString page.permalink)
          cmd.Parameters.AddWithValue ("@publishedOn", page.publishedOn)
          cmd.Parameters.AddWithValue ("@updatedOn", page.updatedOn)
          cmd.Parameters.AddWithValue ("@showInPageList", page.showInPageList)
          cmd.Parameters.AddWithValue ("@template", maybe page.template)
          cmd.Parameters.AddWithValue ("@text", page.text)
        ] |> ignore
    
    /// Append meta items to a page
    let appendPageMeta (page : Page) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT name, value FROM page_meta WHERE page_id = @id"
        cmd.Parameters.AddWithValue ("@id", PageId.toString page.id) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return { page with metadata = toList Map.toMetaItem rdr }
    }
    
    /// Append revisions and permalinks to a page
    let appendPageRevisionsAndPermalinks (page : Page) = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.Parameters.AddWithValue ("@pageId", PageId.toString page.id) |> ignore
        
        cmd.CommandText <- "SELECT permalink FROM page_permalink WHERE page_id = @pageId"
        use! rdr = cmd.ExecuteReaderAsync ()
        let page = { page with priorPermalinks = toList Map.toPermalink rdr }
        do! rdr.CloseAsync ()
        
        cmd.CommandText <- "SELECT as_of, revision_text FROM page_revision WHERE page_id = @pageId ORDER BY as_of DESC"
        use! rdr = cmd.ExecuteReaderAsync ()
        return { page with revisions = toList Map.toRevision rdr }
    }
    
    /// Return a page with no text (or meta items, prior permalinks, or revisions)
    let pageWithoutTextOrMeta rdr =
        { Map.toPage rdr with text = "" }
    
    /// Update a page's metadata items
    let updatePageMeta pageId oldItems newItems = backgroundTask {
        let toDelete, toAdd = diffMetaItems oldItems newItems
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
              cmd.Parameters.Add ("@name", SqliteType.Text)
              cmd.Parameters.Add ("@value", SqliteType.Text)
            ] |> ignore
            let runCmd (item : MetaItem) = backgroundTask {
                cmd.Parameters["@name" ].Value <- item.name
                cmd.Parameters["@value"].Value <- item.value
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM page_meta WHERE page_id = @pageId AND name = @name AND value = @value" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO page_meta VALUES (@pageId, @name, @value)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a page's prior permalinks
    let updatePagePermalinks pageId oldLinks newLinks = backgroundTask {
        let toDelete, toAdd = diffPermalinks oldLinks newLinks
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
              cmd.Parameters.Add ("@link", SqliteType.Text)
            ] |> ignore
            let runCmd link = backgroundTask {
                cmd.Parameters["@link"].Value <- Permalink.toString link
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM page_permalink WHERE page_id = @pageId AND permalink = @link" 
            toDelete
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO page_permalink VALUES (@pageId, @link)"
            toAdd
            |> List.map runCmd
            |> Task.WhenAll
            |> ignore
    }
    
    /// Update a page's revisions
    let updatePageRevisions pageId oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = diffRevisions oldRevs newRevs
        if List.isEmpty toDelete && List.isEmpty toAdd then
            return ()
        else
            use cmd = conn.CreateCommand ()
            let runCmd withText rev = backgroundTask {
                cmd.Parameters.Clear ()
                [ cmd.Parameters.AddWithValue ("@pageId", PageId.toString pageId)
                  cmd.Parameters.AddWithValue ("@asOf", rev.asOf)
                ] |> ignore
                if withText then cmd.Parameters.AddWithValue ("@text", MarkupText.toString rev.text) |> ignore
                do! write cmd
            }
            cmd.CommandText <- "DELETE FROM page_revision WHERE page_id = @pageId AND as_of = @asOf" 
            toDelete
            |> List.map (runCmd false)
            |> Task.WhenAll
            |> ignore
            cmd.CommandText <- "INSERT INTO page_revision VALUES (@pageId, @asOf, @text)"
            toAdd
            |> List.map (runCmd true)
            |> Task.WhenAll
            |> ignore
    }
    
    // IMPLEMENTATION FUNCTIONS
    
    /// Add a page
    let add page = backgroundTask {
        use cmd = conn.CreateCommand ()
        // The page itself
        cmd.CommandText <- """
            INSERT INTO page (
                id, web_log_id, author_id, title, permalink, published_on, updated_on, show_in_page_list, template,
                page_text
            ) VALUES (
                @id, @webLogId, @authorId, @title, @permalink, @publishedOn, @updatedOn, @showInPageList, @template,
                @text
            )"""
        addPageParameters cmd page
        do! write cmd
        do! updatePageMeta       page.id [] page.metadata
        do! updatePagePermalinks page.id [] page.priorPermalinks
        do! updatePageRevisions  page.id [] page.revisions
    }
    
    /// Get all pages for a web log (without text, revisions, prior permalinks, or metadata)
    let all webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM page WHERE web_log_id = @webLogId ORDER BY LOWER(title)"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList pageWithoutTextOrMeta rdr
    }
    
    /// Count all pages for the given web log
    let countAll webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT COUNT(id) FROM page WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        return! count cmd
    }
    
    /// Count all pages shown in the page list for the given web log
    let countListed webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            SELECT COUNT(id)
              FROM page
             WHERE web_log_id        = @webLogId
               AND show_in_page_list = @showInPageList"""
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@showInPageList", true) |> ignore
        return! count cmd
    }
    
    /// Find a page by its ID (without revisions and prior permalinks)
    let findById pageId webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM page WHERE id = @id"
        cmd.Parameters.AddWithValue ("@id", PageId.toString pageId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        match Helpers.verifyWebLog<Page> webLogId (fun it -> it.webLogId) Map.toPage rdr with
        | Some page ->
            let! page = appendPageMeta page
            return Some page
        | None -> return None
    }
    
    /// Find a complete page by its ID
    let findFullById pageId webLogId = backgroundTask {
        match! findById pageId webLogId with
        | Some page ->
            let! page = appendPageRevisionsAndPermalinks page
            return Some page
        | None -> return None
    }
    
    let delete pageId webLogId = backgroundTask {
        match! findById pageId webLogId with
        | Some _ ->
            use cmd = conn.CreateCommand ()
            cmd.Parameters.AddWithValue ("@id", PageId.toString pageId) |> ignore
            cmd.CommandText <- """
                DELETE FROM page_revision  WHERE page_id = @id;
                DELETE FROM page_permalink WHERE page_id = @id;
                DELETE FROM page_meta      WHERE page_id = @id;
                DELETE FROM page           WHERE id      = @id"""
            do! write cmd
            return true
        | None -> return false
    }
    
    /// Find a page by its permalink for the given web log
    let findByPermalink permalink webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM page WHERE web_log_id = @webLogId AND permalink = @link"
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@link", Permalink.toString permalink) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        if rdr.Read () then
            let! page = appendPageMeta (Map.toPage rdr)
            return Some page
        else
            return None
    }
    
    /// Find the current permalink within a set of potential prior permalinks for the given web log
    let findCurrentPermalink permalinks webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            SELECT p.permalink
              FROM page p
                   INNER JOIN page_permalink pp ON pp.page_id = p.id
             WHERE p.web_log_id = @webLogId
               AND pp.permalink IN ("""
        permalinks
        |> List.iteri (fun idx link ->
            if idx > 0 then cmd.CommandText <- $"{cmd.CommandText}, "
            cmd.CommandText <- $"{cmd.CommandText}@link{idx}"
            cmd.Parameters.AddWithValue ($"@link{idx}", Permalink.toString link) |> ignore)
        cmd.CommandText <- $"{cmd.CommandText})"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        return if rdr.Read () then Some (Map.toPermalink rdr) else None
    }
    
    /// Get all complete pages for the given web log
    let findFullByWebLog webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM page WHERE web_log_id = @webLogId"
        addWebLogId cmd webLogId
        use! rdr = cmd.ExecuteReaderAsync ()
        let! pages =
            toList Map.toPage rdr
            |> List.map (fun page -> backgroundTask {
                let! page = appendPageMeta page
                return! appendPageRevisionsAndPermalinks page
            })
            |> Task.WhenAll
        return List.ofArray pages
    }
    
    /// Get all listed pages for the given web log (without revisions, prior permalinks, or text)
    let findListed webLogId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            SELECT *
              FROM page
             WHERE web_log_id        = @webLogId
               AND show_in_page_list = @showInPageList
             ORDER BY LOWER(title)"""
        addWebLogId cmd webLogId
        cmd.Parameters.AddWithValue ("@showInPageList", true) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        let! pages =
            toList pageWithoutTextOrMeta rdr
            |> List.map (fun page -> backgroundTask { return! appendPageMeta page })
            |> Task.WhenAll
        return List.ofArray pages
    }
    
    /// Get a page of pages for the given web log (without revisions, prior permalinks, or metadata)
    let findPageOfPages webLogId pageNbr = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- """
            SELECT *
              FROM page
             WHERE web_log_id = @webLogId
             ORDER BY LOWER(title)
             LIMIT @pageSize OFFSET @toSkip"""
        addWebLogId cmd webLogId
        [ cmd.Parameters.AddWithValue ("@pageSize", 26)
          cmd.Parameters.AddWithValue ("@toSkip", (pageNbr - 1) * 25)
        ] |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toPage rdr
    }
    
    /// Restore pages from a backup
    let restore pages = backgroundTask {
        for page in pages do
            do! add page
    }
    
    /// Update a page
    let update (page : Page) = backgroundTask {
        match! findFullById page.id page.webLogId with
        | Some oldPage ->
            use cmd = conn.CreateCommand ()
            cmd.CommandText <- """
                UPDATE page
                   SET author_id         = @authorId,
                       title             = @title,
                       permalink         = @permalink,
                       published_on      = @publishedOn,
                       updated_on        = @updatedOn,
                       show_in_page_list = @showInPageList,
                       template          = @template,
                       page_text         = @text
                 WHERE id         = @pageId
                   AND web_log_id = @webLogId"""
            addPageParameters cmd page
            do! write cmd
            do! updatePageMeta       page.id oldPage.metadata        page.metadata
            do! updatePagePermalinks page.id oldPage.priorPermalinks page.priorPermalinks
            do! updatePageRevisions  page.id oldPage.revisions       page.revisions
            return ()
        | None -> return ()
    }
    
    /// Update a page's prior permalinks
    let updatePriorPermalinks pageId webLogId permalinks = backgroundTask {
        match! findFullById pageId webLogId with
        | Some page ->
            do! updatePagePermalinks pageId page.priorPermalinks permalinks
            return true
        | None -> return false
    }
    
    interface IPageData with
        member _.add page = add page
        member _.all webLogId = all webLogId
        member _.countAll webLogId = countAll webLogId
        member _.countListed webLogId = countListed webLogId
        member _.delete pageId webLogId = delete pageId webLogId
        member _.findById pageId webLogId = findById pageId webLogId
        member _.findByPermalink permalink webLogId = findByPermalink permalink webLogId
        member _.findCurrentPermalink permalinks webLogId = findCurrentPermalink permalinks webLogId
        member _.findFullById pageId webLogId = findFullById pageId webLogId
        member _.findFullByWebLog webLogId = findFullByWebLog webLogId
        member _.findListed webLogId = findListed webLogId
        member _.findPageOfPages webLogId pageNbr = findPageOfPages webLogId pageNbr
        member _.restore pages = restore pages
        member _.update page = update page
        member _.updatePriorPermalinks pageId webLogId permalinks = updatePriorPermalinks pageId webLogId permalinks
