module MyWebLog.Maintenance

open System
open System.IO
open Microsoft.Extensions.DependencyInjection
open RethinkDb.Driver.FSharp
open RethinkDb.Driver.Net

/// Create the web log information
let private doCreateWebLog (args : string[]) (sp : IServiceProvider) = task {
    
    let conn = sp.GetRequiredService<IConnection> ()
    
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
    
    do! Data.WebLog.add
            { WebLog.empty with
                id          = webLogId
                name        = args[2]
                urlBase     = args[1]
                defaultPage = PageId.toString homePageId
                timeZone    = timeZone
            } conn
    
    // Create the admin user
    let salt = Guid.NewGuid ()
    
    do! Data.WebLogUser.add 
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
            } conn

    // Create the default home page
    do! Data.Page.add
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
            } conn

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
    let conn = sp.GetRequiredService<IConnection> ()

    match! Data.WebLog.findByHost urlBase conn with
    | Some webLog ->
        
        let mapping =
            File.ReadAllLines file
            |> Seq.ofArray
            |> Seq.map (fun it ->
                let parts = it.Split " "
                Permalink parts[0], Permalink parts[1])
        
        for old, current in mapping do
            match! Data.Post.findByPermalink current webLog.id conn with
            | Some post ->
                let! withLinks = rethink<Post> {
                    withTable Data.Table.Post
                    get post.id
                    result conn
                }
                do! rethink {
                    withTable Data.Table.Post
                    get post.id
                    update [ "priorPermalinks", old :: withLinks.priorPermalinks :> obj]
                    write; ignoreResult conn
                }
                printfn $"{Permalink.toString old} -> {Permalink.toString current}"
            | None -> printfn $"Cannot find current post for {Permalink.toString current}"
        printfn "Done!"
    | None -> printfn $"No web log found at {urlBase}"
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
        | Some themeName ->
            let conn   = sp.GetRequiredService<IConnection> ()
            let clean  = if args.Length > 2 then bool.Parse args[2] else true
            use stream = File.Open (args[1], FileMode.Open)
            use copy = new MemoryStream ()
            do! stream.CopyToAsync copy
            do! Handlers.Admin.loadThemeFromZip themeName copy clean conn
            printfn $"Theme {themeName} loaded successfully"
        | None ->
            printfn $"Theme file name {args[1]} is invalid"
    else
        printfn "Usage: MyWebLog load-theme [theme-zip-file-name] [*clean-load]"
        printfn "         * optional, defaults to true"
}
