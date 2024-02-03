/// <summary>
/// Integration tests for <see cref="IUploadData" /> implementations
/// </summary> 
module UploadDataTests

open System
open System.IO
open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// The ID of the root web log
let private rootId = CategoryDataTests.rootId

/// The ID of the favicon upload
let private faviconId = UploadId "XweKbWQiOkqqrjEdgP9wwg"

let ``Add succeeds`` (data: IData) = task {
    let file = File.ReadAllBytes "../admin-theme/wwwroot/logo-dark.png"
    do! data.Upload.Add
            { Id        = UploadId "new-upload"
              WebLogId  = rootId
              UpdatedOn = Noda.epoch + Duration.FromDays 30
              Path      = Permalink "1970/01/logo-dark.png"
              Data      = file }
    let! added = data.Upload.FindByPath "1970/01/logo-dark.png" rootId
    Expect.isSome added "There should have been an upload returned"
    let upload = added.Value
    Expect.equal upload.Id (UploadId "new-upload") "ID is incorrect"
    Expect.equal upload.WebLogId rootId "Web log ID is incorrect"
    Expect.equal upload.UpdatedOn (Noda.epoch + Duration.FromDays 30) "Updated on is incorrect"
    Expect.equal upload.Path (Permalink "1970/01/logo-dark.png") "Path is incorrect"
    Expect.equal upload.Data file "Data is incorrect"
}

let ``FindByPath succeeds when an upload is found`` (data: IData) = task {
    let! upload = data.Upload.FindByPath "2022/06/favicon.ico" rootId
    Expect.isSome upload "There should have been an upload returned"
    let it = upload.Value
    Expect.equal it.Id faviconId "ID is incorrect"
    Expect.equal it.WebLogId rootId "Web log ID is incorrect"
    Expect.equal
        it.UpdatedOn (Instant.FromDateTimeOffset(DateTimeOffset.Parse "2022-06-23T21:15:40Z")) "Updated on is incorrect"
    Expect.equal it.Path (Permalink "2022/06/favicon.ico") "Path is incorrect"
    Expect.isNonEmpty it.Data "Data should have been retrieved"
}

let ``FindByPath succeeds when an upload is not found (incorrect weblog)`` (data: IData) = task {
    let! upload = data.Upload.FindByPath "2022/06/favicon.ico" (WebLogId "wrong")
    Expect.isNone upload "There should not have been an upload returned"
}

let ``FindByPath succeeds when an upload is not found (bad path)`` (data: IData) = task {
    let! upload = data.Upload.FindByPath "2022/07/favicon.ico" rootId
    Expect.isNone upload "There should not have been an upload returned"
}

let ``FindByWebLog succeeds when uploads exist`` (data: IData) = task {
    let! uploads = data.Upload.FindByWebLog rootId
    Expect.hasLength uploads 2 "There should have been 2 uploads returned"
    for upload in uploads do
        Expect.contains [ faviconId; UploadId "new-upload" ] upload.Id $"Unexpected upload returned ({upload.Id})"
        Expect.isEmpty upload.Data $"Upload should not have had its data ({upload.Id})"
}

let ``FindByWebLog succeeds when no uploads exist`` (data: IData) = task {
    let! uploads = data.Upload.FindByWebLog (WebLogId "nothing")
    Expect.isEmpty uploads "There should have been no uploads returned"
}

let ``FindByWebLogWithData succeeds when uploads exist`` (data: IData) = task {
    let! uploads = data.Upload.FindByWebLogWithData rootId
    Expect.hasLength uploads 2 "There should have been 2 uploads returned"
    for upload in uploads do
        Expect.contains [ faviconId; UploadId "new-upload" ] upload.Id $"Unexpected upload returned ({upload.Id})"
        Expect.isNonEmpty upload.Data $"Upload should have had its data ({upload.Id})"
}

let ``FindByWebLogWithData succeeds when no uploads exist`` (data: IData) = task {
    let! uploads = data.Upload.FindByWebLogWithData (WebLogId "data-nope")
    Expect.isEmpty uploads "There should have been no uploads returned"
}

let ``Delete succeeds when an upload is deleted`` (data: IData) = task {
    match! data.Upload.Delete faviconId rootId with
    | Ok path -> Expect.equal path "2022/06/favicon.ico" "The path of the deleted upload was incorrect"
    | Error it -> Expect.isTrue false $"Upload deletion should have succeeded (message {it})"
}

let ``Delete succeeds when an upload is not deleted`` (data: IData) = task {
    match! data.Upload.Delete faviconId rootId with
    | Ok it -> Expect.isTrue false $"Upload deletion should not have succeeded (path {it})"
    | Error msg -> Expect.equal msg $"Upload ID {faviconId} not found" "Error message was incorrect"
}
