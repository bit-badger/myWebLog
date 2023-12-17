module MyWebLog.Maintenance

open System
open System.IO
open Microsoft.Extensions.DependencyInjection
open MyWebLog.Data
open NodaTime

/// Create the web log information
let private doCreateWebLog (args: string[]) (sp: IServiceProvider) = task {
    
    let data = sp.GetRequiredService<IData>()
    
    let timeZone =
        let local = TimeZoneInfo.Local.Id
        match TimeZoneInfo.Local.HasIanaId with
        | true -> local
        | false ->
            match TimeZoneInfo.TryConvertWindowsIdToIanaId local with
            | true, ianaId -> ianaId
            | false, _ -> raise <| TimeZoneNotFoundException $"Cannot find IANA timezone for {local}"
    
    // Create the web log
    let webLogId   = WebLogId.Create()
    let userId     = WebLogUserId.Create()
    let homePageId = PageId.Create()
    let slug       = Handlers.Upload.makeSlug args[2]
    
    // If this is the first web log being created, the user will be an installation admin; otherwise, they will be an
    // admin just over their web log
    let! webLogs     = data.WebLog.All()
    let  accessLevel = if List.isEmpty webLogs then Administrator else WebLogAdmin
        
    do! data.WebLog.Add
            { WebLog.Empty with
                Id          = webLogId
                Name        = args[2]
                Slug        = slug
                UrlBase     = args[1]
                DefaultPage = string homePageId
                TimeZone    = timeZone }
    
    // Create the admin user
    let now  = Noda.now ()
    let user =
        { WebLogUser.Empty with
            Id            = userId
            WebLogId      = webLogId
            Email         = args[3]
            FirstName     = "Admin"
            LastName      = "User"
            PreferredName = "Admin"
            AccessLevel   = accessLevel
            CreatedOn     = now }
    do! data.WebLogUser.Add { user with PasswordHash = Handlers.User.createPasswordHash user args[4] }

    // Create the default home page
    do! data.Page.Add
            { Page.Empty with
                Id          = homePageId
                WebLogId    = webLogId
                AuthorId    = userId
                Title       = "Welcome to myWebLog!"
                Permalink   = Permalink "welcome-to-myweblog.html"
                PublishedOn = now
                UpdatedOn   = now
                Text        = "<p>This is your default home page.</p>"
                Revisions   = [
                    {   AsOf = now
                        Text = Html "<p>This is your default home page.</p>" }
                ] }

    printfn $"Successfully initialized database for {args[2]} with URL base {args[1]}"
    match accessLevel with
    | Administrator -> printfn $"  ({args[3]} is an installation administrator)"
    | WebLogAdmin ->
        printfn $"  ({args[3]} is a web log administrator;"
        printfn """   use "upgrade-user" to promote to installation administrator)"""
    | _ -> ()
}

/// Create a new web log
let createWebLog args sp = task {
    match args |> Array.length with
    | 5 -> do! doCreateWebLog args sp
    | _ -> eprintfn "Usage: myWebLog init [url] [name] [admin-email] [admin-pw]"
}

/// Import prior permalinks from a text files with lines in the format "[old] [new]"
let private importPriorPermalinks urlBase file (sp: IServiceProvider) = task {
    let data = sp.GetRequiredService<IData>()

    match! data.WebLog.FindByHost urlBase with
    | Some webLog ->
        
        let mapping =
            File.ReadAllLines file
            |> Seq.ofArray
            |> Seq.map (fun it ->
                let parts = it.Split " "
                Permalink parts[0], Permalink parts[1])
        
        for old, current in mapping do
            match! data.Post.FindByPermalink current webLog.Id with
            | Some post ->
                let! withLinks = data.Post.FindFullById post.Id post.WebLogId
                let! _ = data.Post.UpdatePriorPermalinks post.Id post.WebLogId
                             (old :: withLinks.Value.PriorPermalinks)
                printfn $"{old} -> {current}"
            | None -> eprintfn $"Cannot find current post for {current}"
        printfn "Done!"
    | None -> eprintfn $"No web log found at {urlBase}"
}

/// Import permalinks if all is well
let importLinks args sp = task {
    match args |> Array.length with
    | 3 -> do! importPriorPermalinks args[1] args[2] sp
    | _ -> eprintfn "Usage: myWebLog import-links [url] [file-name]"
}

// Loading a theme and restoring a backup are not statically compilable; this is OK
#nowarn "3511"

open Microsoft.Extensions.Logging

/// Load a theme from the given ZIP file
let loadTheme (args: string[]) (sp: IServiceProvider) = task {
    if args.Length = 2 then
        let fileName =
            match args[1].LastIndexOf Path.DirectorySeparatorChar with
            | -1 -> args[1]
            | it -> args[1][(it + 1)..]
        match Handlers.Admin.Theme.deriveIdFromFileName fileName with
        | Ok themeId ->
            let data   = sp.GetRequiredService<IData>()
            use stream = File.Open(args[1], FileMode.Open)
            use copy   = new MemoryStream()
            do! stream.CopyToAsync copy
            let! theme = Handlers.Admin.Theme.loadFromZip themeId copy data
            let fac = sp.GetRequiredService<ILoggerFactory>()
            let log = fac.CreateLogger "MyWebLog.Themes"
            log.LogInformation $"{theme.Name} v{theme.Version} ({theme.Id}) loaded"
        | Error message -> eprintfn $"{message}"
    else
        eprintfn "Usage: myWebLog load-theme [theme-zip-file-name]"
}

/// Back up a web log's data
module Backup =
    
    open MyWebLog.Converters
    open Newtonsoft.Json

    /// A theme asset, with the data base-64 encoded
    type EncodedAsset =
        {   /// The ID of the theme asset
            Id: ThemeAssetId
            
            /// The updated date for this asset
            UpdatedOn: Instant
            
            /// The data for this asset, base-64 encoded
            Data: string }
        
        /// Create an encoded theme asset from the original theme asset
        static member fromAsset (asset: ThemeAsset) =
            {   Id        = asset.Id
                UpdatedOn = asset.UpdatedOn
                Data      = Convert.ToBase64String asset.Data }
    
        /// Create a theme asset from an encoded theme asset
        static member toAsset (encoded: EncodedAsset) : ThemeAsset =
            {   Id        = encoded.Id
                UpdatedOn = encoded.UpdatedOn
                Data      = Convert.FromBase64String encoded.Data }
    
    /// An uploaded file, with the data base-64 encoded
    type EncodedUpload =
        {   /// The ID of the upload
            Id: UploadId
            
            /// The ID of the web log to which the upload belongs
            WebLogId: WebLogId
            
            /// The path at which this upload is served
            Path: Permalink
            
            /// The date/time this upload was last updated (file time)
            UpdatedOn: Instant
            
            /// The data for the upload, base-64 encoded
            Data: string }
        
        /// Create an encoded uploaded file from the original uploaded file
        static member fromUpload (upload: Upload) : EncodedUpload =
            {   Id        = upload.Id
                WebLogId  = upload.WebLogId
                Path      = upload.Path
                UpdatedOn = upload.UpdatedOn
                Data      = Convert.ToBase64String upload.Data }
        
        /// Create an uploaded file from an encoded uploaded file
        static member toUpload (encoded: EncodedUpload) : Upload =
            {   Id        = encoded.Id
                WebLogId  = encoded.WebLogId
                Path      = encoded.Path
                UpdatedOn = encoded.UpdatedOn
                Data      = Convert.FromBase64String encoded.Data }
    
    /// A unified archive for a web log
    type Archive =
        {   /// The web log to which this archive belongs
            WebLog: WebLog
            
            /// The users for this web log
            Users: WebLogUser list
            
            /// The theme used by this web log at the time the archive was made
            Theme: Theme
            
            /// Assets for the theme used by this web log at the time the archive was made
            Assets: EncodedAsset list
            
            /// The categories for this web log
            Categories: Category list
            
            /// The tag mappings for this web log
            TagMappings: TagMap list
            
            /// The pages for this web log (containing only the most recent revision)
            Pages: Page list
            
            /// The posts for this web log (containing only the most recent revision)
            Posts: Post list
            
            /// The uploaded files for this web log
            Uploads: EncodedUpload list }
    
    /// Create a JSON serializer
    let private getSerializer prettyOutput =
        let serializer = Json.configure (JsonSerializer.CreateDefault())
        if prettyOutput then serializer.Formatting <- Formatting.Indented
        serializer
    
    /// Display statistics for a backup archive
    let private displayStats (msg: string) (webLog: WebLog) archive =

        let userCount     = List.length archive.Users
        let assetCount    = List.length archive.Assets
        let categoryCount = List.length archive.Categories
        let tagMapCount   = List.length archive.TagMappings
        let pageCount     = List.length archive.Pages
        let postCount     = List.length archive.Posts
        let uploadCount   = List.length archive.Uploads
        
        // Create a pluralized output based on the count
        let plural count ifOne ifMany =
            if count = 1 then ifOne else ifMany
            
        printfn ""
        printfn $"""{msg.Replace ("<>NAME<>", webLog.Name)}"""
        printfn $""" - The theme "{archive.Theme.Name}" with {assetCount} asset{plural assetCount "" "s"}"""
        printfn $""" - {userCount} user{plural userCount "" "s"}"""
        printfn $""" - {categoryCount} categor{plural categoryCount "y" "ies"}"""
        printfn $""" - {tagMapCount} tag mapping{plural tagMapCount "" "s"}"""
        printfn $""" - {pageCount} page{plural pageCount "" "s"}"""
        printfn $""" - {postCount} post{plural postCount "" "s"}"""
        printfn $""" - {uploadCount} uploaded file{plural uploadCount "" "s"}"""

    /// Create a backup archive
    let private createBackup webLog (fileName: string) prettyOutput (data: IData) = task {
        // Create the data structure
        printfn "- Exporting theme..."
        let! theme  = data.Theme.FindById webLog.ThemeId
        let! assets = data.ThemeAsset.FindByThemeWithData webLog.ThemeId
        
        printfn "- Exporting users..."
        let! users = data.WebLogUser.FindByWebLog webLog.Id
        
        printfn "- Exporting categories and tag mappings..."
        let! categories = data.Category.FindByWebLog webLog.Id
        let! tagMaps    = data.TagMap.FindByWebLog webLog.Id
        
        printfn "- Exporting pages..."
        let! pages = data.Page.FindFullByWebLog webLog.Id
        
        printfn "- Exporting posts..."
        let! posts = data.Post.FindFullByWebLog webLog.Id
        
        printfn "- Exporting uploads..."
        let! uploads = data.Upload.FindByWebLogWithData webLog.Id
        
        printfn "- Writing archive..."
        let archive =
            {   WebLog      = webLog
                Users       = users
                Theme       = Option.get theme
                Assets      = assets |> List.map EncodedAsset.fromAsset
                Categories  = categories
                TagMappings = tagMaps
                Pages       = pages   |> List.map (fun p -> { p with Revisions = List.truncate 1 p.Revisions })
                Posts       = posts   |> List.map (fun p -> { p with Revisions = List.truncate 1 p.Revisions })
                Uploads     = uploads |> List.map EncodedUpload.fromUpload }
        
        // Write the structure to the backup file
        if File.Exists fileName then File.Delete fileName
        let serializer = getSerializer prettyOutput
        use writer = new StreamWriter(fileName)
        serializer.Serialize(writer, archive)
        writer.Close()
        
        displayStats $"{fileName} (for <>NAME<>) contains:" webLog archive
    }
    
    let private doRestore archive newUrlBase (data: IData) = task {
        let! restore = task {
            match! data.WebLog.FindById archive.WebLog.Id with
            | Some webLog when defaultArg newUrlBase webLog.UrlBase = webLog.UrlBase ->
                do! data.WebLog.Delete webLog.Id
                return { archive with Archive.WebLog.UrlBase = defaultArg newUrlBase webLog.UrlBase }
            | Some _ ->
                // Err'body gets new IDs...
                let newWebLogId = WebLogId.Create()
                let newCatIds   = archive.Categories  |> List.map (fun cat  -> cat.Id,  CategoryId.Create()  ) |> dict
                let newMapIds   = archive.TagMappings |> List.map (fun tm   -> tm.Id,   TagMapId.Create()    ) |> dict
                let newPageIds  = archive.Pages       |> List.map (fun page -> page.Id, PageId.Create()      ) |> dict
                let newPostIds  = archive.Posts       |> List.map (fun post -> post.Id, PostId.Create()      ) |> dict
                let newUserIds  = archive.Users       |> List.map (fun user -> user.Id, WebLogUserId.Create()) |> dict
                let newUpIds    = archive.Uploads     |> List.map (fun up   -> up.Id,   UploadId.Create()    ) |> dict
                return
                    { archive with
                        WebLog      = { archive.WebLog with Id = newWebLogId; UrlBase = Option.get newUrlBase }
                        Users       = archive.Users
                                      |> List.map (fun u -> { u with Id = newUserIds[u.Id]; WebLogId = newWebLogId })
                        Categories  = archive.Categories
                                      |> List.map (fun c -> { c with Id = newCatIds[c.Id]; WebLogId = newWebLogId })
                        TagMappings = archive.TagMappings
                                      |> List.map (fun tm -> { tm with Id = newMapIds[tm.Id]; WebLogId = newWebLogId })
                        Pages       = archive.Pages
                                      |> List.map (fun page ->
                                          { page with
                                              Id       = newPageIds[page.Id]
                                              WebLogId = newWebLogId
                                              AuthorId = newUserIds[page.AuthorId] })
                        Posts       = archive.Posts
                                      |> List.map (fun post ->
                                          { post with
                                              Id          = newPostIds[post.Id]
                                              WebLogId    = newWebLogId
                                              AuthorId    = newUserIds[post.AuthorId]
                                              CategoryIds = post.CategoryIds |> List.map (fun c -> newCatIds[c]) })
                        Uploads     = archive.Uploads
                                      |> List.map (fun u -> { u with Id = newUpIds[u.Id]; WebLogId = newWebLogId }) }
            | None ->
                return { archive with Archive.WebLog.UrlBase = defaultArg newUrlBase archive.WebLog.UrlBase }
        }
        
        // Restore theme and assets (one at a time, as assets can be large)
        printfn ""
        printfn "- Importing theme..."
        do! data.Theme.Save restore.Theme
        restore.Assets
        |> List.iter (EncodedAsset.toAsset >> data.ThemeAsset.Save >> Async.AwaitTask >> Async.RunSynchronously)
        
        // Restore web log data
        
        printfn "- Restoring web log..."
        // v2.0 backups will not have redirect rules; fix that if restoring to v2.1 or later
        let webLog =
            if isNull (box restore.WebLog.RedirectRules) then { restore.WebLog with RedirectRules = [] }
            else restore.WebLog
        do! data.WebLog.Add webLog
        
        printfn "- Restoring users..."
        do! data.WebLogUser.Restore restore.Users
        
        printfn "- Restoring categories and tag mappings..."
        if not (List.isEmpty restore.TagMappings) then do! data.TagMap.Restore   restore.TagMappings
        if not (List.isEmpty restore.Categories)  then do! data.Category.Restore restore.Categories
        
        printfn "- Restoring pages..."
        if not (List.isEmpty restore.Pages) then do! data.Page.Restore restore.Pages
        
        printfn "- Restoring posts..."
        if not (List.isEmpty restore.Posts) then do! data.Post.Restore restore.Posts
        
        // TODO: comments not yet implemented
        
        printfn "- Restoring uploads..."
        if not (List.isEmpty restore.Uploads) then
            do! data.Upload.Restore (restore.Uploads |> List.map EncodedUpload.toUpload)
        
        displayStats "Restored for <>NAME<>:" restore.WebLog restore
    }
    
    /// Decide whether to restore a backup
    let private restoreBackup fileName newUrlBase promptForOverwrite data = task {
        
        let serializer = getSerializer false
        use stream     = new FileStream(fileName, FileMode.Open)
        use reader     = new StreamReader(stream)
        use jsonReader = new JsonTextReader(reader)
        let archive    = serializer.Deserialize<Archive> jsonReader
        
        let mutable doOverwrite = not promptForOverwrite
        if promptForOverwrite then
            printfn "** WARNING: Restoring a web log will delete existing data for that web log"
            printfn "            (unless restoring to a different URL base), and will overwrite the"
            printfn "            theme in either case."
            printfn ""
            printf  "Continue? [Y/n] "
            doOverwrite <- not (Console.ReadKey().Key = ConsoleKey.N)
        
        if doOverwrite then
            do! doRestore archive newUrlBase data
        else
            printfn $"{archive.WebLog.Name} backup restoration canceled"
    }
        
    /// Generate a backup archive
    let generateBackup (args: string[]) (sp: IServiceProvider) = task {
        if args.Length > 1 && args.Length < 5 then
            let data = sp.GetRequiredService<IData>()
            match! data.WebLog.FindByHost args[1] with
            | Some webLog ->
                let fileName =
                    if args.Length = 2 || (args.Length = 3 && args[2] = "pretty") then
                        $"{webLog.Slug}.json"
                    elif args[2].EndsWith ".json" then
                        args[2]
                    else
                        $"{args[2]}.json"
                let prettyOutput = (args.Length = 3 && args[2] = "pretty") || (args.Length = 4 && args[3] = "pretty")
                do! createBackup webLog fileName prettyOutput data
            | None -> eprintfn $"Error: no web log found for {args[1]}"
        else
            eprintfn """Usage: myWebLog backup [url-base] [*backup-file-name] [**"pretty"]"""
            eprintfn """          * optional - default is [web-log-slug].json"""
            eprintfn """         ** optional - default is non-pretty JSON output"""
    }
    
    /// Restore a backup archive
    let restoreFromBackup (args: string[]) (sp: IServiceProvider) = task {
        if args.Length = 2 || args.Length = 3 then
            let data       = sp.GetRequiredService<IData>()
            let newUrlBase = if args.Length = 3 then Some args[2] else None
            do! restoreBackup args[1] newUrlBase (args[0] <> "do-restore") data
        else
            eprintfn "Usage: myWebLog restore [backup-file-name] [*url-base]"
            eprintfn "         * optional - will restore to original URL base if omitted"
            eprintfn "       (use do-restore to skip confirmation prompt)"
    }


/// Upgrade a WebLogAdmin user to an Administrator user
let private doUserUpgrade urlBase email (data: IData) = task {
    match! data.WebLog.FindByHost urlBase with
    | Some webLog ->
        match! data.WebLogUser.FindByEmail email webLog.Id with
        | Some user ->
            match user.AccessLevel with
            | WebLogAdmin ->
                do! data.WebLogUser.Update { user with AccessLevel = Administrator }
                printfn $"{email} is now an Administrator user"
            | other -> eprintfn $"ERROR: {email} is an {other}, not a WebLogAdmin"
        | None -> eprintfn $"ERROR: no user {email} found at {urlBase}"
    | None -> eprintfn $"ERROR: no web log found for {urlBase}"
}

/// Upgrade a WebLogAdmin user to an Administrator user if the command-line arguments are good
let upgradeUser (args: string[]) (sp: IServiceProvider) = task {
    match args.Length with
    | 3 -> do! doUserUpgrade args[1] args[2] (sp.GetRequiredService<IData>())
    | _ -> eprintfn "Usage: myWebLog upgrade-user [web-log-url-base] [email-address]"
}

/// Set a user's password
let doSetPassword urlBase email password (data: IData) = task {
    match! data.WebLog.FindByHost urlBase with
    | Some webLog ->
        match! data.WebLogUser.FindByEmail email webLog.Id with
        | Some user ->
            do! data.WebLogUser.Update { user with PasswordHash = Handlers.User.createPasswordHash user password }
            printfn $"Password for user {email} at {webLog.Name} set successfully"
        | None -> eprintfn $"ERROR: no user {email} found at {urlBase}"
    | None -> eprintfn $"ERROR: no web log found for {urlBase}"
}

/// Set a user's password if the command-line arguments are good
let setPassword (args: string[]) (sp: IServiceProvider) = task {
    match args.Length with
    | 4 -> do! doSetPassword args[1] args[2] args[3] (sp.GetRequiredService<IData>())
    | _ -> eprintfn "Usage: myWebLog set-password [web-log-url-base] [email-address] [password]"
}
