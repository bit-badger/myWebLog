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
    
    // TODO: finish implementation; paused for LiteDB data capability development, will work with both
    
    /// Create a JSON serializer (uses RethinkDB data implementation's JSON converters)
    let private getSerializer () =
        let serializer = JsonSerializer.CreateDefault ()
        Json.all () |> Seq.iter serializer.Converters.Add
        serializer
    
    /// Create a backup archive
    let createBackup webLog (fileName : string) (data : IData) = task {
        // Create the data structure
        let  themeId    = ThemeId webLog.themePath
        let! theme      = data.Theme.findById themeId
        let! assets     = data.ThemeAsset.findByThemeWithData themeId
        let! users      = data.WebLogUser.findByWebLog webLog.id
        let! categories = data.Category.findByWebLog webLog.id
        let! tagMaps    = data.TagMap.findByWebLog webLog.id
        let! pages      = data.Page.findFullByWebLog webLog.id
        let! posts      = data.Post.findFullByWebLog webLog.id
        let archive = {
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
        let serializer = getSerializer ()
        use writer = new StreamWriter (fileName)
        serializer.Serialize (writer, archive)
        writer.Close ()
        
        printfn "Backup Stats:"
        printfn $" - Users: {archive.users |> List.length}"
        printfn $" - Categories: {archive.categories |> List.length}"
        printfn $" - Tag Maps: {archive.tagMappings |> List.length}"
        printfn $" - Pages: {archive.pages |> List.length}"
        printfn $" - Posts: {archive.posts |> List.length}"
        printfn ""
    }
    
    /// Generate a backup archive
    let generateBackup (args : string[]) (sp : IServiceProvider) = task {
        if args.Length = 3 then
            let data = sp.GetRequiredService<IData> ()
            match! data.WebLog.findByHost args[1] with
            | Some webLog ->
                let fileName = if args[2].EndsWith ".json" then args[2] else $"{args[1]}.json"
                do! createBackup webLog fileName data
                printfn $"Backup created for {args[1]}"
            | None -> printfn $"Error: no web log found for {args[1]}"
        else
            printfn "Usage: MyWebLog backup [url-base] [backup-file-name]"
    }
    