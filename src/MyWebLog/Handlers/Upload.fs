/// Handlers to manipulate uploaded files
module MyWebLog.Handlers.Upload

open System
open Giraffe
open Microsoft.AspNetCore.Http
open MyWebLog

/// Helper functions for this module
[<AutoOpen>]
module private Helpers =
    
    open Microsoft.AspNetCore.StaticFiles
    
    /// A MIME type mapper instance to use when serving files from the database
    let mimeMap = FileExtensionContentTypeProvider ()


/// Determine if the file has been modified since the date/time specified by the If-Modified-Since header
let checkModified since (ctx : HttpContext) : HttpHandler option =
    match ctx.Request.Headers.IfModifiedSince with
    | it when it.Count < 1 -> None
    | it when since > DateTime.Parse it[0] -> None
    | _ -> Some (setStatusCode 304 >=> setBodyFromString "Not Modified")


open Microsoft.AspNetCore.Http.Headers
open Microsoft.Net.Http.Headers

/// Derive a MIME type based on the extension of the file
let deriveMimeType path =
    match mimeMap.TryGetContentType path with true, typ -> typ | false, _ -> "application/octet-stream"

/// Send a file, caching the response for 30 days
let sendFile updatedOn path data : HttpHandler = fun next ctx -> task {
    let headers = ResponseHeaders ctx.Response.Headers
    headers.LastModified <- Some (DateTimeOffset updatedOn) |> Option.toNullable
    headers.ContentType  <- (deriveMimeType >> MediaTypeHeaderValue) path
    headers.CacheControl <-
        let hdr = CacheControlHeaderValue()
        hdr.MaxAge <- Some (TimeSpan.FromDays 30) |> Option.toNullable
        hdr
    return! setBody data next ctx
}

// GET /upload/{web-log-slug}/{**path}
let serve (urlParts : string seq) : HttpHandler = fun next ctx -> task {
    let parts  = (urlParts |> Seq.skip 1 |> Seq.head).Split '/'
    let slug   = Array.head parts
    let path   = String.Join ('/', parts |> Array.skip 1)
    let webLog = ctx.WebLog
    if slug = webLog.slug then
        match! ctx.Data.Upload.findByPath path webLog.id with
        | Some upload ->
            match checkModified upload.updatedOn ctx with
            | Some threeOhFour -> return! threeOhFour next ctx
            | None -> return! sendFile upload.updatedOn path upload.data next ctx
        | None -> return! Error.notFound next ctx
    else
        return! Error.notFound next ctx
}

// ADMIN

open System.IO
open DotLiquid
open MyWebLog.ViewModels

// GET /admin/uploads
let list : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let! dbUploads = ctx.Data.Upload.findByWebLog webLog.id
    let diskUploads =
        let path = Path.Combine ("wwwroot", "upload", webLog.slug)
        printfn $"Files in %s{path}"
        try
            Directory.EnumerateFiles (path, "*", SearchOption.AllDirectories)
            |> Seq.map (fun file ->
                let name = Path.GetFileName file
                let create =
                    match File.GetCreationTime (Path.Combine (path, file)) with
                    | dt when dt > DateTime.UnixEpoch -> Some dt
                    | _ -> None
                { DisplayUpload.id = ""
                  name             = name
                  path             = file.Substring(8).Replace (name, "")
                  updatedOn        = create
                  source           = UploadDestination.toString Disk
                })
            |> List.ofSeq
        with
        | :? DirectoryNotFoundException -> [] // This is fine
        | ex ->
            warn "Upload" ctx $"Encountered {ex.GetType().Name} listing uploads for {path}:\n{ex.Message}"
            []
    printfn "done"
    let allFiles =
        dbUploads
        |> List.map (DisplayUpload.fromUpload Database)
        |> List.append diskUploads
        |> List.sortByDescending (fun file -> file.updatedOn, file.path)

    return!
        Hash.FromAnonymousObject {|
            csrf       = csrfToken ctx
            page_title = "Uploaded Files"
            files      = allFiles
        |}
        |> viewForTheme "admin" "upload-list" next ctx
    }