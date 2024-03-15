/// Handlers to manipulate uploaded files
module MyWebLog.Handlers.Upload

open System
open System.IO
open Microsoft.Net.Http.Headers

/// Helper functions for this module
[<AutoOpen>]
module private Helpers =
    
    open Microsoft.AspNetCore.StaticFiles
    
    /// A MIME type mapper instance to use when serving files from the database
    let mimeMap = FileExtensionContentTypeProvider()

    /// A cache control header that instructs the browser to cache the result for no more than 30 days
    let cacheForThirtyDays =
        let hdr = CacheControlHeaderValue()
        hdr.MaxAge <- Some (TimeSpan.FromDays 30) |> Option.toNullable
        hdr
    
    /// Shorthand for the directory separator
    let slash = Path.DirectorySeparatorChar
    
    /// The base directory where uploads are stored, relative to the executable
    let uploadDir = Path.Combine("wwwroot", "upload")


// ~~ SERVING UPLOADS ~~

open System.Globalization
open Giraffe
open Microsoft.AspNetCore.Http
open NodaTime

/// Determine if the file has been modified since the date/time specified by the If-Modified-Since header
let checkModified since (ctx: HttpContext) : HttpHandler option =
    match ctx.Request.Headers.IfModifiedSince with
    | it when it.Count < 1 -> None
    | it when since > Instant.FromDateTimeUtc(DateTime.Parse(it[0], null, DateTimeStyles.AdjustToUniversal)) -> None
    | _ -> Some (setStatusCode 304)


open Microsoft.AspNetCore.Http.Headers

/// Derive a MIME type based on the extension of the file
let deriveMimeType path =
    match mimeMap.TryGetContentType path with true, typ -> typ | false, _ -> "application/octet-stream"

/// Send a file, caching the response for 30 days
let sendFile updatedOn path (data : byte[]) : HttpHandler = fun next ctx ->
    let headers = ResponseHeaders ctx.Response.Headers
    headers.ContentType  <- (deriveMimeType >> MediaTypeHeaderValue) path
    headers.CacheControl <- cacheForThirtyDays
    let stream = new MemoryStream(data)
    streamData true stream None (Some (DateTimeOffset updatedOn)) next ctx


open MyWebLog

// GET /upload/{web-log-slug}/{**path}
let serve (urlParts: string seq) : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let parts  = (urlParts |> Seq.skip 1 |> Seq.head).Split '/'
    let slug   = Array.head parts
    if slug = webLog.Slug then
        // Static file middleware will not work in subdirectories; check for an actual file first
        let fileName = Path.Combine("wwwroot", (Seq.head urlParts)[1..])
        if File.Exists fileName then
            return! streamFile true fileName None None next ctx
        else
            let path = String.Join('/', Array.skip 1 parts)
            match! ctx.Data.Upload.FindByPath path webLog.Id with
            | Some upload ->
                match checkModified upload.UpdatedOn ctx with
                | Some threeOhFour -> return! threeOhFour next ctx
                | None -> return! sendFile (upload.UpdatedOn.ToDateTimeUtc()) path upload.Data next ctx
            | None -> return! Error.notFound next ctx
    else
        return! Error.notFound next ctx
}

// ~~ ADMINISTRATION ~~

open System.Text.RegularExpressions
open MyWebLog.ViewModels

/// Turn a string into a lowercase URL-safe slug
let makeSlug it = (Regex """\s+""").Replace((Regex "[^A-z0-9 -]").Replace(it, ""), "-").ToLowerInvariant()

// GET /admin/uploads
let list : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let  webLog      = ctx.WebLog
    let! dbUploads   = ctx.Data.Upload.FindByWebLog webLog.Id
    let  diskUploads =
        let path = Path.Combine(uploadDir, webLog.Slug)
        try
            Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            |> Seq.map (fun file ->
                let name = Path.GetFileName file
                let create =
                    match File.GetCreationTime(Path.Combine(path, file)) with
                    | dt when dt > DateTime.UnixEpoch -> Some dt
                    | _ -> None
                {   DisplayUpload.Id = ""
                    Name             = name
                    Path             = file.Replace($"{path}{slash}", "").Replace(name, "").Replace(slash, '/')
                    UpdatedOn        = create
                    Source           = string Disk })
        with
        | :? DirectoryNotFoundException -> [] // This is fine
        | ex ->
            warn "Upload" ctx $"Encountered {ex.GetType().Name} listing uploads for {path}:\n{ex.Message}"
            []
    return!
        dbUploads
        |> Seq.ofList
        |> Seq.map (DisplayUpload.FromUpload webLog Database)
        |> Seq.append diskUploads
        |> Seq.sortByDescending (fun file -> file.UpdatedOn, file.Path)
        |> Views.WebLog.uploadList
        |> adminPage "Uploaded Files" true next ctx
}

// GET /admin/upload/new
let showNew : HttpHandler = requireAccess Author >=> fun next ctx ->
    adminPage "Upload a File" true next ctx Views.WebLog.uploadNew


/// Redirect to the upload list
let showUploads : HttpHandler =
    redirectToGet "admin/uploads"

// POST /admin/upload/save
let save : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    if ctx.Request.HasFormContentType && ctx.Request.Form.Files.Count > 0 then
        let upload    = Seq.head ctx.Request.Form.Files
        let fileName  = String.Concat (makeSlug (Path.GetFileNameWithoutExtension upload.FileName),
                                       Path.GetExtension(upload.FileName).ToLowerInvariant())
        let  now      = Noda.now ()
        let  localNow = ctx.WebLog.LocalTime now
        let  year     = localNow.ToString "yyyy"
        let  month    = localNow.ToString "MM"
        let! form     = ctx.BindFormAsync<UploadFileModel>()
        
        match UploadDestination.Parse form.Destination with
        | Database ->
            use stream = new MemoryStream()
            do! upload.CopyToAsync stream
            let file =
                {   Id        = UploadId.Create()
                    WebLogId  = ctx.WebLog.Id
                    Path      = Permalink $"{year}/{month}/{fileName}"
                    UpdatedOn = now
                    Data      = stream.ToArray() }
            do! ctx.Data.Upload.Add file
        | Disk ->
            let fullPath = Path.Combine(uploadDir, ctx.WebLog.Slug, year, month)
            let _        = Directory.CreateDirectory fullPath
            use stream   = new FileStream(Path.Combine(fullPath, fileName), FileMode.Create)
            do! upload.CopyToAsync stream
        
        do! addMessage ctx { UserMessage.Success with Message = $"File uploaded to {form.Destination} successfully" }
        return! showUploads next ctx
    else
        return! RequestErrors.BAD_REQUEST "Bad request; no file present" next ctx
}

// DELETE /admin/upload/{id}
let deleteFromDb upId : HttpHandler = fun next ctx -> task {
    match! ctx.Data.Upload.Delete (UploadId upId) ctx.WebLog.Id with
    | Ok fileName ->
        do! addMessage ctx { UserMessage.Success with Message = $"{fileName} deleted successfully" }
        return! showUploads next ctx
    | Error _ -> return! Error.notFound next ctx
}

/// Remove a directory tree if it is empty
let removeEmptyDirectories (webLog: WebLog) (filePath: string) =
    let mutable path     = Path.GetDirectoryName filePath
    let mutable finished = false
    while (not finished) && path > "" do
        let fullPath = Path.Combine(uploadDir, webLog.Slug, path)
        if Directory.EnumerateFileSystemEntries fullPath |> Seq.isEmpty then
            Directory.Delete fullPath
            path <- String.Join(slash, path.Split slash |> Array.rev |> Array.skip 1 |> Array.rev)
        else finished <- true
    
// DELETE /admin/upload/disk/{**path}
let deleteFromDisk urlParts : HttpHandler = fun next ctx -> task {
    let filePath = urlParts |> Seq.skip 1 |> Seq.head
    let path = Path.Combine(uploadDir, ctx.WebLog.Slug, filePath)
    if File.Exists path then
        File.Delete path
        removeEmptyDirectories ctx.WebLog filePath
        do! addMessage ctx { UserMessage.Success with Message = $"{filePath} deleted successfully" }
        return! showUploads next ctx
    else return! Error.notFound next ctx
}
