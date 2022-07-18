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
    let slug       = Handlers.Upload.makeSlug args[2]
    
    // If this is the first web log being created, the user will be an installation admin; otherwise, they will be an
    // admin just over their web log
    let! webLogs     = data.WebLog.All ()
    let  accessLevel = if List.isEmpty webLogs then Administrator else WebLogAdmin
        
    do! data.WebLog.Add
            { WebLog.empty with
                id          = webLogId
                name        = args[2]
                slug        = slug
                urlBase     = args[1]
                defaultPage = PageId.toString homePageId
                timeZone    = timeZone
            }
    
    // Create the admin user
    let salt = Guid.NewGuid ()
    let now  = DateTime.UtcNow
    
    do! data.WebLogUser.Add 
            { WebLogUser.empty with
                id            = userId
                webLogId      = webLogId
                userName      = args[3]
                firstName     = "Admin"
                lastName      = "User"
                preferredName = "Admin"
                passwordHash  = Handlers.User.hashedPassword args[4] args[3] salt
                salt          = salt
                accessLevel   = accessLevel
                createdOn     = now
            }

    // Create the default home page
    do! data.Page.Add
            { Page.empty with
                id          = homePageId
                webLogId    = webLogId
                authorId    = userId
                title       = "Welcome to myWebLog!"
                permalink   = Permalink "welcome-to-myweblog.html"
                publishedOn = now
                updatedOn   = now
                text        = "<p>This is your default home page.</p>"
                revisions   = [
                    { asOf = now
                      text = Html "<p>This is your default home page.</p>"
                    }
                ]
            }

    printfn $"Successfully initialized database for {args[2]} with URL base {args[1]}"
    match accessLevel with
    | Administrator -> printfn $"  ({args[3]} is an installation administrator)"
    | WebLogAdmin ->
        printfn  $"  ({args[3]} is a web log administrator;"
        printfn """   use "upgrade-user" to promote to installation administrator)"""
    | _ -> ()
}

/// Create a new web log
let createWebLog args sp = task {
    match args |> Array.length with
    | 5 -> do! doCreateWebLog args sp
    | _ -> eprintfn "Usage: MyWebLog init [url] [name] [admin-email] [admin-pw]"
}

/// Import prior permalinks from a text files with lines in the format "[old] [new]"
let private importPriorPermalinks urlBase file (sp : IServiceProvider) = task {
    let data = sp.GetRequiredService<IData> ()

    match! data.WebLog.FindByHost urlBase with
    | Some webLog ->
        
        let mapping =
            File.ReadAllLines file
            |> Seq.ofArray
            |> Seq.map (fun it ->
                let parts = it.Split " "
                Permalink parts[0], Permalink parts[1])
        
        for old, current in mapping do
            match! data.Post.FindByPermalink current webLog.id with
            | Some post ->
                let! withLinks = data.Post.FindFullById post.id post.webLogId
                let! _ = data.Post.UpdatePriorPermalinks post.id post.webLogId
                             (old :: withLinks.Value.priorPermalinks)
                printfn $"{Permalink.toString old} -> {Permalink.toString current}"
            | None -> eprintfn $"Cannot find current post for {Permalink.toString current}"
        printfn "Done!"
    | None -> eprintfn $"No web log found at {urlBase}"
}

/// Import permalinks if all is well
let importLinks args sp = task {
    match args |> Array.length with
    | 3 -> do! importPriorPermalinks args[1] args[2] sp
    | _ -> eprintfn "Usage: MyWebLog import-links [url] [file-name]"
}

// Loading a theme and restoring a backup are not statically compilable; this is OK
#nowarn "3511"

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
        eprintfn "Usage: MyWebLog load-theme [theme-zip-file-name] [*clean-load]"
        eprintfn "         * optional, defaults to true"
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
        static member fromEncoded (encoded : EncodedAsset) : ThemeAsset =
            { id        = encoded.id
              updatedOn = encoded.updatedOn
              data      = Convert.FromBase64String encoded.data
            }
    
    /// An uploaded file, with the data base-64 encoded
    type EncodedUpload =
        {   /// The ID of the upload
            id : UploadId
            
            /// The ID of the web log to which the upload belongs
            webLogId : WebLogId
            
            /// The path at which this upload is served
            path : Permalink
            
            /// The date/time this upload was last updated (file time)
            updatedOn : DateTime
            
            /// The data for the upload, base-64 encoded
            data : string
        }
        
        /// Create an encoded uploaded file from the original uploaded file
        static member fromUpload (upload : Upload) : EncodedUpload =
            { id        = upload.id
              webLogId  = upload.webLogId
              path      = upload.path
              updatedOn = upload.updatedOn
              data      = Convert.ToBase64String upload.data
            }
        
        /// Create an uploaded file from an encoded uploaded file
        static member fromEncoded (encoded : EncodedUpload) : Upload =
            { id        = encoded.id
              webLogId  = encoded.webLogId
              path      = encoded.path
              updatedOn = encoded.updatedOn
              data      = Convert.FromBase64String encoded.data
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
            
            /// The uploaded files for this web log
            uploads : EncodedUpload list
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
        let uploadCount   = List.length archive.uploads
        
        // Create a pluralized output based on the count
        let plural count ifOne ifMany =
            if count = 1 then ifOne else ifMany
            
        printfn ""
        printfn $"""{msg.Replace ("<>NAME<>", webLog.name)}"""
        printfn $""" - The theme "{archive.theme.name}" with {assetCount} asset{plural assetCount "" "s"}"""
        printfn $""" - {userCount} user{plural userCount "" "s"}"""
        printfn $""" - {categoryCount} categor{plural categoryCount "y" "ies"}"""
        printfn $""" - {tagMapCount} tag mapping{plural tagMapCount "" "s"}"""
        printfn $""" - {pageCount} page{plural pageCount "" "s"}"""
        printfn $""" - {postCount} post{plural postCount "" "s"}"""
        printfn $""" - {uploadCount} uploaded file{plural uploadCount "" "s"}"""

    /// Create a backup archive
    let private createBackup webLog (fileName : string) prettyOutput (data : IData) = task {
        // Create the data structure
        let themeId = ThemeId webLog.themePath
        
        printfn "- Exporting theme..."
        let! theme  = data.Theme.FindById themeId
        let! assets = data.ThemeAsset.FindByThemeWithData themeId
        
        printfn "- Exporting users..."
        let! users = data.WebLogUser.FindByWebLog webLog.id
        
        printfn "- Exporting categories and tag mappings..."
        let! categories = data.Category.FindByWebLog webLog.id
        let! tagMaps    = data.TagMap.FindByWebLog webLog.id
        
        printfn "- Exporting pages..."
        let! pages = data.Page.FindFullByWebLog webLog.id
        
        printfn "- Exporting posts..."
        let! posts = data.Post.FindFullByWebLog webLog.id
        
        printfn "- Exporting uploads..."
        let! uploads = data.Upload.FindByWebLogWithData webLog.id
        
        printfn "- Writing archive..."
        let  archive    = {
            webLog      = webLog
            users       = users
            theme       = Option.get theme
            assets      = assets |> List.map EncodedAsset.fromAsset
            categories  = categories
            tagMappings = tagMaps
            pages       = pages   |> List.map (fun p -> { p with revisions = List.truncate 1 p.revisions })
            posts       = posts   |> List.map (fun p -> { p with revisions = List.truncate 1 p.revisions })
            uploads     = uploads |> List.map EncodedUpload.fromUpload
        }
        
        // Write the structure to the backup file
        if File.Exists fileName then File.Delete fileName
        let serializer = getSerializer prettyOutput
        use writer = new StreamWriter (fileName)
        serializer.Serialize (writer, archive)
        writer.Close ()
        
        displayStats $"{fileName} (for <>NAME<>) contains:" webLog archive
    }
    
    let private doRestore archive newUrlBase (data : IData) = task {
        let! restore = task {
            match! data.WebLog.FindById archive.webLog.id with
            | Some webLog when defaultArg newUrlBase webLog.urlBase = webLog.urlBase ->
                do! data.WebLog.Delete webLog.id
                return { archive with webLog = { archive.webLog with urlBase = defaultArg newUrlBase webLog.urlBase } }
            | Some _ ->
                // Err'body gets new IDs...
                let newWebLogId = WebLogId.create ()
                let newCatIds   = archive.categories  |> List.map (fun cat  -> cat.id,  CategoryId.create   ()) |> dict
                let newMapIds   = archive.tagMappings |> List.map (fun tm   -> tm.id,   TagMapId.create     ()) |> dict
                let newPageIds  = archive.pages       |> List.map (fun page -> page.id, PageId.create       ()) |> dict
                let newPostIds  = archive.posts       |> List.map (fun post -> post.id, PostId.create       ()) |> dict
                let newUserIds  = archive.users       |> List.map (fun user -> user.id, WebLogUserId.create ()) |> dict
                let newUpIds    = archive.uploads     |> List.map (fun up   -> up.id,   UploadId.create     ()) |> dict
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
                        uploads     = archive.uploads
                                      |> List.map (fun u -> { u with id = newUpIds[u.id]; webLogId = newWebLogId })
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
        do! data.Theme.Save restore.theme
        let! _ = restore.assets |> List.map (EncodedAsset.fromEncoded >> data.ThemeAsset.Save) |> Task.WhenAll
        
        // Restore web log data
        
        printfn "- Restoring web log..."
        do! data.WebLog.Add restore.webLog
        
        printfn "- Restoring users..."
        do! data.WebLogUser.Restore restore.users
        
        printfn "- Restoring categories and tag mappings..."
        do! data.TagMap.Restore   restore.tagMappings
        do! data.Category.Restore restore.categories
        
        printfn "- Restoring pages..."
        do! data.Page.Restore restore.pages
        
        printfn "- Restoring posts..."
        do! data.Post.Restore restore.posts
        
        // TODO: comments not yet implemented
        
        printfn "- Restoring uploads..."
        do! data.Upload.Restore (restore.uploads |> List.map EncodedUpload.fromEncoded)
        
        displayStats "Restored for <>NAME<>:" restore.webLog restore
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
        if args.Length > 1 && args.Length < 5 then
            let data = sp.GetRequiredService<IData> ()
            match! data.WebLog.FindByHost args[1] with
            | Some webLog ->
                let fileName =
                    if args.Length = 2 || (args.Length = 3 && args[2] = "pretty") then
                        $"{webLog.slug}.json"
                    elif args[2].EndsWith ".json" then
                        args[2]
                    else
                        $"{args[2]}.json"
                let prettyOutput = (args.Length = 3 && args[2] = "pretty") || (args.Length = 4 && args[3] = "pretty")
                do! createBackup webLog fileName prettyOutput data
            | None -> eprintfn $"Error: no web log found for {args[1]}"
        else
            eprintfn """Usage: MyWebLog backup [url-base] [*backup-file-name] [**"pretty"]"""
            eprintfn """          * optional - default is [web-log-slug].json"""
            eprintfn """         ** optional - default is non-pretty JSON output"""
    }
    
    /// Restore a backup archive
    let restoreFromBackup (args : string[]) (sp : IServiceProvider) = task {
        if args.Length = 2 || args.Length = 3 then
            let data       = sp.GetRequiredService<IData> ()
            let newUrlBase = if args.Length = 3 then Some args[2] else None
            do! restoreBackup args[1] newUrlBase (args[0] <> "do-restore") data
        else
            eprintfn "Usage: MyWebLog restore [backup-file-name] [*url-base]"
            eprintfn "         * optional - will restore to original URL base if omitted"
            eprintfn "       (use do-restore to skip confirmation prompt)"
    }


/// Upgrade a WebLogAdmin user to an Administrator user
let private doUserUpgrade urlBase email (data : IData) = task {
    match! data.WebLog.FindByHost urlBase with
    | Some webLog ->
        match! data.WebLogUser.FindByEmail email webLog.id with
        | Some user ->
            match user.accessLevel with
            | WebLogAdmin ->
                do! data.WebLogUser.Update { user with accessLevel = Administrator }
                printfn $"{email} is now an Administrator user"
            | other -> eprintfn $"ERROR: {email} is an {AccessLevel.toString other}, not a WebLogAdmin"
        | None -> eprintfn $"ERROR: no user {email} found at {urlBase}"
    | None -> eprintfn $"ERROR: no web log found for {urlBase}"
}

/// Upgrade a WebLogAdmin user to an Administrator user if the command-line arguments are good
let upgradeUser (args : string[]) (sp : IServiceProvider) = task {
    match args.Length with
    | 3 -> do! doUserUpgrade args[1] args[2] (sp.GetRequiredService<IData> ())
    | _ -> eprintfn "Usage: MyWebLog upgrade-user [web-log-url-base] [email-address]"
}
