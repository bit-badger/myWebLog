/// <summary>
/// Integration tests for <see cref="ITagMapData" /> implementations
/// </summary> 
module TagMapDataTests

open Expecto
open MyWebLog
open MyWebLog.Data

/// The ID of the root web log
let rootId = CategoryDataTests.rootId

/// The ID of the f# tag
let fSharpId = TagMapId "Icm027noqE-rPHKZA98vAw"

/// The ID of the ghoti tag
let fishId = TagMapId "GdryXh-S0kGsNBs2RIacGA"

let ``FindById succeeds when a tag mapping is found`` (data: IData) = task {
    let! tagMap = data.TagMap.FindById fSharpId rootId
    Expect.isSome tagMap "There should have been a tag mapping returned"
    let tag = tagMap.Value
    Expect.equal tag.Id fSharpId "ID is incorrect"
    Expect.equal tag.WebLogId rootId "Web log ID is incorrect"
    Expect.equal tag.Tag "f#" "Tag is incorrect"
    Expect.equal tag.UrlValue "f-sharp" "URL value is incorrect"
}

let ``FindById succeeds when a tag mapping is not found (incorrect weblog)`` (data: IData) = task {
    let! tagMap = data.TagMap.FindById fSharpId (WebLogId "wrong")
    Expect.isNone tagMap "There should not have been a tag mapping returned"
}

let ``FindById succeeds when a tag mapping is not found (bad tag map ID)`` (data: IData) = task {
    let! tagMap = data.TagMap.FindById (TagMapId "out") rootId
    Expect.isNone tagMap "There should not have been a tag mapping returned"
}

let ``FindByUrlValue succeeds when a tag mapping is found`` (data: IData) = task {
    let! tagMap = data.TagMap.FindByUrlValue "f-sharp" rootId
    Expect.isSome tagMap "There should have been a tag mapping returned"
    Expect.equal tagMap.Value.Id fSharpId "ID is incorrect"
}

let ``FindByUrlValue succeeds when a tag mapping is not found (incorrect weblog)`` (data: IData) = task {
    let! tagMap = data.TagMap.FindByUrlValue "f-sharp" (WebLogId "incorrect")
    Expect.isNone tagMap "There should not have been a tag mapping returned"
}

let ``FindByUrlValue succeeds when a tag mapping is not found (no such value)`` (data: IData) = task {
    let! tagMap = data.TagMap.FindByUrlValue "c-sharp" rootId
    Expect.isNone tagMap "There should not have been a tag mapping returned"
}

let ``FindByWebLog succeeds when tag mappings are found`` (data: IData) = task {
    let! mappings = data.TagMap.FindByWebLog rootId
    Expect.hasLength mappings 2 "There should have been 2 tag mappings returned"
    for mapping in mappings do
        Expect.contains [ fSharpId; fishId ] mapping.Id $"Unexpected mapping ID ({mapping.Id})"
        Expect.equal mapping.WebLogId rootId "Web log ID is incorrect"
        Expect.isNotEmpty mapping.Tag "Tag should not have been blank"
        Expect.isNotEmpty mapping.UrlValue "URL value should not have been blank"
}

let ``FindByWebLog succeeds when no tag mappings are found`` (data: IData) = task {
    let! mappings = data.TagMap.FindByWebLog (WebLogId "no-maps")
    Expect.isEmpty mappings "There should have been no tag mappings returned"
}

let ``FindMappingForTags succeeds when mappings exist`` (data: IData) = task {
    let! mappings = data.TagMap.FindMappingForTags [ "f#"; "testing"; "unit" ] rootId
    Expect.hasLength mappings 1 "There should have been one mapping returned"
    Expect.equal mappings[0].Id fSharpId "The wrong mapping was returned"
}

let ``FindMappingForTags succeeds when no mappings exist`` (data: IData) = task {
    let! mappings = data.TagMap.FindMappingForTags [ "c#"; "turkey"; "ham" ] rootId
    Expect.isEmpty mappings "There should have been no tag mappings returned"
}

let ``Save succeeds when adding a tag mapping`` (data: IData) = task {
    let mapId = TagMapId "test"
    do! data.TagMap.Save { Id = mapId; WebLogId = rootId; Tag = "c#"; UrlValue = "c-sharp" }
    let! mapping = data.TagMap.FindById mapId rootId
    Expect.isSome mapping "The mapping should have been retrieved"
    let tag = mapping.Value
    Expect.equal tag.Id mapId "ID is incorrect"
    Expect.equal tag.WebLogId rootId "Web log ID is incorrect"
    Expect.equal tag.Tag "c#" "Tag is incorrect"
    Expect.equal tag.UrlValue "c-sharp" "URL value is incorrect"
}

let ``Save succeeds when updating a tag mapping`` (data: IData) = task {
    do! data.TagMap.Save { Id = fishId; WebLogId = rootId; Tag = "halibut"; UrlValue = "mackerel" }
    let! mapping = data.TagMap.FindById fishId rootId
    Expect.isSome mapping "The mapping should have been retrieved"
    let tag = mapping.Value
    Expect.equal tag.Id fishId "ID is incorrect"
    Expect.equal tag.WebLogId rootId "Web log ID is incorrect"
    Expect.equal tag.Tag "halibut" "Tag is incorrect"
    Expect.equal tag.UrlValue "mackerel" "URL value is incorrect"
}

let ``Delete succeeds when a tag mapping is deleted`` (data: IData) = task {
    let! deleted = data.TagMap.Delete fSharpId rootId
    Expect.isTrue deleted "The tag mapping should have been deleted"
}

let ``Delete succeeds when a tag mapping is not deleted`` (data: IData) = task {
    let! deleted = data.TagMap.Delete fSharpId rootId // this was deleted above
    Expect.isFalse deleted "A tag mapping should not have been deleted"
}
