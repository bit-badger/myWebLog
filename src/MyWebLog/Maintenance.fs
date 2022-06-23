module MyWebLog.Maintenance

open System
open System.IO
open Microsoft.Extensions.DependencyInjection
open MyWebLog.Data

/// Create the web log information
let private doCreateWebLog (args : string[]) (sp : IServiceProvider) = task {
    
    let data = sp.GetRequiredService<IData> ()
    
    let timeZone =
        let local = TimeZoneInfo.Local.Id
        match TimeZoneInfo.Local.HasIanaId with
        | true -> local
        | false ->
            match TimeZoneInfo.TryConvertWindowsIdToIanaId local with
            | true, ianaId -> ianaId
            | false, _ -> raise <| TimeZoneNotFoundException $"Cannot find IANA timezone for {local}"
    
    // Create the web log
    let webLogId   = WebLogId.create ()
    let userId     = WebLogUserId.create ()
    let homePageId = PageId.create ()
    
    do! data.WebLog.add
            { WebLog.empty with
                id          = webLogId
                name        = args[2]
                urlBase     = args[1]
                defaultPage = PageId.toString homePageId
                timeZone    = timeZone
            }
    
    // Create the admin user
    let salt = Guid.NewGuid ()
    
    do! data.WebLogUser.add 
            { WebLogUser.empty with
                id                 = userId
                webLogId           = webLogId
                userName           = args[3]
                firstName          = "Admin"
                lastName           = "User"
                preferredName      = "Admin"
                passwordHash       = Handlers.User.hashedPassword args[4] args[3] salt
                salt               = salt
                authorizationLevel = Administrator
            }

    // Create the default home page
    do! data.Page.add
            { Page.empty with
                id          = homePageId
                webLogId    = webLogId
                authorId    = userId
                title       = "Welcome to myWebLog!"
                permalink   = Permalink "welcome-to-myweblog.html"
                publishedOn = DateTime.UtcNow
                updatedOn   = DateTime.UtcNow
                text        = "<p>This is your default home page.</p>"
                revisions   = [
                    { asOf = DateTime.UtcNow
                      text = Html "<p>This is your default home page.</p>"
                    }
                ]
            }

    printfn $"Successfully initialized database for {args[2]} with URL base {args[1]}"
}

/// Create a new web log
let createWebLog args sp = task {
    match args |> Array.length with
    | 5 -> do! doCreateWebLog args sp
    | _ -> printfn "Usage: MyWebLog init [url] [name] [admin-email] [admin-pw]"
}

/// Import prior permalinks from a text files with lines in the format "[old] [new]"
let importPriorPermalinks urlBase file (sp : IServiceProvider) = task {
    let data = sp.GetRequiredService<IData> ()

    match! data.WebLog.findByHost urlBase with
    | Some webLog ->
        
        let mapping =
            File.ReadAllLines file
            |> Seq.ofArray
            |> Seq.map (fun it ->
                let parts = it.Split " "
                Permalink parts[0], Permalink parts[1])
        
        for old, current in mapping do
            match! data.Post.findByPermalink current webLog.id with
            | Some post ->
                let! withLinks = data.Post.findFullById post.id post.webLogId
                let! _ = data.Post.updatePriorPermalinks post.id post.webLogId
                             (old :: withLinks.Value.priorPermalinks)
                printfn $"{Permalink.toString old} -> {Permalink.toString current}"
            | None -> printfn $"Cannot find current post for {Permalink.toString current}"
        printfn "Done!"
    | None -> eprintfn $"No web log found at {urlBase}"
}

/// Import permalinks if all is well
let importLinks args sp = task {
    match args |> Array.length with
    | 3 -> do! importPriorPermalinks args[1] args[2] sp
    | _ -> printfn "Usage: MyWebLog import-links [url] [file-name]"
}

/// Load a theme from the given ZIP file
let loadTheme (args : string[]) (sp : IServiceProvider) = task {
    if args.Length > 1 then
        let fileName =
            match args[1].LastIndexOf Path.DirectorySeparatorChar with
            | -1 -> args[1]
            | it -> args[1][(it + 1)..]
        match Handlers.Admin.getThemeName fileName with
        | Ok themeName ->
            let data   = sp.GetRequiredService<IData> ()
            let clean  = if args.Length > 2 then bool.Parse args[2] else true
            use stream = File.Open (args[1], FileMode.Open)
            use copy   = new MemoryStream ()
            do! stream.CopyToAsync copy
            do! Handlers.Admin.loadThemeFromZip themeName copy clean data
            printfn $"Theme {themeName} loaded successfully"
        | Error message -> eprintfn $"{message}"
    else
        printfn "Usage: MyWebLog load-theme [theme-zip-file-name] [*clean-load]"
        printfn "         * optional, defaults to true"
}

/// Back up a web log's data
module Backup =
    
    open System.Threading.Tasks
    open MyWebLog.Converters
    open Newtonsoft.Json

    /// A theme asset, with the data base-64 encoded
    type EncodedAsset =
        {   /// The ID of the theme asset
            id : ThemeAssetId
            
            /// The updated date for this asset
            updatedOn : DateTime
            
            /// The data for this asset, base-64 encoded
            data : string
        }
        
        /// Create an encoded theme asset from the original theme asset
        static member fromAsset (asset : ThemeAsset) =
            { id        = asset.id
              updatedOn = asset.updatedOn
              data      = Convert.ToBase64String asset.data
            }
    
        /// Create a theme asset from an encoded theme asset
        static member fromAsset (asset : EncodedAsset) : ThemeAsset =
            { id        = asset.id
              updatedOn = asset.updatedOn
              data      = Convert.FromBase64String asset.data
            }
    
        
    /// A unified archive for a web log
    type Archive =
        {   /// The web log to which this archive belongs
            webLog : WebLog
            
            /// The users for this web log
            users : WebLogUser list
            
            /// The theme used by this web log at the time the archive was made
            theme : Theme
            
            /// Assets for the theme used by this web log at the time the archive was made
            assets : EncodedAsset list
            
            /// The categories for this web log
            categories : Category list
            
            /// The tag mappings for this web log
            tagMappings : TagMap list
            
            /// The pages for this web log (containing only the most recent revision)
            pages : Page list
            
            /// The posts for this web log (containing only the most recent revision)
            posts : Post list
        }
    
    /// Create a JSON serializer (uses RethinkDB data implementation's JSON converters)
    let private getSerializer prettyOutput =
        let serializer = JsonSerializer.CreateDefault ()
        Json.all () |> Seq.iter serializer.Converters.Add
        if prettyOutput then serializer.Formatting <- Formatting.Indented
        serializer
    
    /// Display statistics for a backup archive
    let private displayStats (msg : string) (webLog : WebLog) archive =

        let userCount     = List.length archive.users
        let assetCount    = List.length archive.assets
        let categoryCount = List.length archive.categories
        let tagMapCount   = List.length archive.tagMappings
        let pageCount     = List.length archive.pages
        let postCount     = List.length archive.posts
        
        // Create a pluralized output based on the count
        let plural count ifOne ifMany =
            if count = 1 then ifOne else ifMany
            
        printfn ""
        printfn $"""{msg.Replace ("{{NAME}}", webLog.name)}"""
        printfn $""" - The theme "{archive.theme.name}" with {assetCount} asset{plural assetCount "" "s"}"""
        printfn $""" - {userCount} user{plural userCount "" "s"}"""
        printfn $""" - {categoryCount} categor{plural categoryCount "y" "ies"}"""
        printfn $""" - {tagMapCount} tag mapping{plural tagMapCount "" "s"}"""
        printfn $""" - {pageCount} page{plural pageCount "" "s"}"""
        printfn $""" - {postCount} post{plural postCount "" "s"}"""

    /// Create a backup archive
    let private createBackup webLog (fileName : string) prettyOutput (data : IData) = task {
        // Create the data structure
        let themeId = ThemeId webLog.themePath
        
        printfn "- Exporting theme..."
        let! theme  = data.Theme.findById themeId
        let! assets = data.ThemeAsset.findByThemeWithData themeId
        
        printfn "- Exporting users..."
        let! users = data.WebLogUser.findByWebLog webLog.id
        
        printfn "- Exporting categories and tag mappings..."
        let! categories = data.Category.findByWebLog webLog.id
        let! tagMaps    = data.TagMap.findByWebLog webLog.id
        
        printfn "- Exporting pages..."
        let! pages = data.Page.findFullByWebLog webLog.id
        
        printfn "- Exporting posts..."
        let! posts = data.Post.findFullByWebLog webLog.id
        
        printfn "- Writing archive..."
        let  archive    = {
            webLog      = webLog
            users       = users
            theme       = Option.get theme
            assets      = assets |> List.map EncodedAsset.fromAsset
            categories  = categories
            tagMappings = tagMaps
            pages       = pages |> List.map (fun p -> { p with revisions = List.truncate 1 p.revisions })
            posts       = posts |> List.map (fun p -> { p with revisions = List.truncate 1 p.revisions })
        }
        
        // Write the structure to the backup file
        if File.Exists fileName then File.Delete fileName
        let serializer = getSerializer prettyOutput
        use writer = new StreamWriter (fileName)
        serializer.Serialize (writer, archive)
        writer.Close ()
        
        displayStats "{{NAME}} backup contains:" webLog archive
    }
    
    let private doRestore archive newUrlBase (data : IData) = task {
        let! restore = task {
            match! data.WebLog.findById archive.webLog.id with
            | Some webLog when defaultArg newUrlBase webLog.urlBase = webLog.urlBase ->
                do! data.WebLog.delete webLog.id
                return archive
            | Some _ ->
                // Err'body gets new IDs...
                let newWebLogId = WebLogId.create ()
                let newCatIds   = archive.categories  |> List.map (fun cat  -> cat.id,  CategoryId.create   ()) |> dict
                let newMapIds   = archive.tagMappings |> List.map (fun tm   -> tm.id,   TagMapId.create     ()) |> dict
                let newPageIds  = archive.pages       |> List.map (fun page -> page.id, PageId.create       ()) |> dict
                let newPostIds  = archive.posts       |> List.map (fun post -> post.id, PostId.create       ()) |> dict
                let newUserIds  = archive.users       |> List.map (fun user -> user.id, WebLogUserId.create ()) |> dict
                return
                    { archive with
                        webLog      = { archive.webLog with id = newWebLogId; urlBase = Option.get newUrlBase }
                        users       = archive.users
                                      |> List.map (fun u -> { u with id = newUserIds[u.id]; webLogId = newWebLogId })
                        categories  = archive.categories
                                      |> List.map (fun c -> { c with id = newCatIds[c.id]; webLogId = newWebLogId })
                        tagMappings = archive.tagMappings
                                      |> List.map (fun tm -> { tm with id = newMapIds[tm.id]; webLogId = newWebLogId })
                        pages       = archive.pages
                                      |> List.map (fun page ->
                                          { page with
                                              id       = newPageIds[page.id]
                                              webLogId = newWebLogId
                                              authorId = newUserIds[page.authorId]
                                          })
                        posts       = archive.posts
                                      |> List.map (fun post ->
                                          { post with
                                              id          = newPostIds[post.id]
                                              webLogId    = newWebLogId
                                              authorId    = newUserIds[post.authorId]
                                              categoryIds = post.categoryIds |> List.map (fun c -> newCatIds[c])
                                          })
                    }
            | None ->
                return
                    { archive with
                        webLog = { archive.webLog with urlBase = defaultArg newUrlBase archive.webLog.urlBase }
                    }
        }
        
        // Restore theme and assets (one at a time, as assets can be large)
        printfn ""
        printfn "- Importing theme..."
        do! data.Theme.save restore.theme
        let! _ = restore.assets |> List.map (EncodedAsset.fromAsset >> data.ThemeAsset.save) |> Task.WhenAll
        
        // Restore web log data
        
        printfn "- Restoring web log..."
        do! data.WebLog.add restore.webLog
        
        printfn "- Restoring users..."
        do! data.WebLogUser.restore restore.users
        
        printfn "- Restoring categories and tag mappings..."
        do! data.TagMap.restore   restore.tagMappings
        do! data.Category.restore restore.categories
        
        printfn "- Restoring pages..."
        do! data.Page.restore restore.pages
        
        printfn "- Restoring posts..."
        do! data.Post.restore restore.posts
        
        // TODO: comments not yet implemented
        
        displayStats "Restored for {{NAME}}:" restore.webLog restore
    }
    
    /// Decide whether to restore a backup
    let private restoreBackup (fileName : string) newUrlBase promptForOverwrite data = task {
        
        let serializer = getSerializer false
        use stream     = new FileStream (fileName, FileMode.Open)
        use reader     = new StreamReader (stream)
        use jsonReader = new JsonTextReader (reader)
        let archive    = serializer.Deserialize<Archive> jsonReader
        
        let mutable doOverwrite = not promptForOverwrite
        if promptForOverwrite then
            printfn "** WARNING: Restoring a web log will delete existing data for that web log"
            printfn "            (unless restoring to a different URL base), and will overwrite the"
            printfn "            theme in either case."
            printfn ""
            printf  "Continue? [Y/n] "
            doOverwrite <- not ((Console.ReadKey ()).Key = ConsoleKey.N)
        
        if doOverwrite then
            do! doRestore archive newUrlBase data
        else
            printfn $"{archive.webLog.name} backup restoration canceled"
    }
        
    /// Generate a backup archive
    let generateBackup (args : string[]) (sp : IServiceProvider) = task {
        if args.Length = 3 || args.Length = 4 then
            let data = sp.GetRequiredService<IData> ()
            match! data.WebLog.findByHost args[1] with
            | Some webLog ->
                let fileName     = if args[2].EndsWith ".json" then args[2] else $"{args[2]}.json"
                let prettyOutput = args.Length = 4 && args[3] = "pretty"
                do! createBackup webLog fileName prettyOutput data
            | None -> printfn $"Error: no web log found for {args[1]}"
        else
            printfn """Usage: MyWebLog backup [url-base] [backup-file-name] [*"pretty"]"""
            printfn """         * optional - default is non-pretty JSON output"""
    }
    
    /// Restore a backup archive
    let restoreFromBackup (args : string[]) (sp : IServiceProvider) = task {
        if args.Length = 2 || args.Length = 3 then
            let data       = sp.GetRequiredService<IData> ()
            let newUrlBase = if args.Length = 3 then Some args[2] else None
            do! restoreBackup args[1] newUrlBase (args[0] <> "do-restore") data
        else
            printfn "Usage: MyWebLog restore [backup-file-name] [*url-base]"
            printfn "         * optional - will restore to original URL base if omitted"
            printfn "       (use do-restore to skip confirmation prompt)"
    }
    