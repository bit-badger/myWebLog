/// <summary>
/// Integration tests for <see cref="IWebLogUserData" /> implementations
/// </summary> 
module WebLogUserDataTests

open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// The ID of the root web log
let private rootId = CategoryDataTests.rootId

/// The ID of the admin user
let private adminId = WebLogUserId "5EM2rimH9kONpmd2zQkiVA"

/// The ID of the editor user
let private editorId = WebLogUserId "GPbJaSOwTkKt14ZKYyveKA"

/// The ID of the author user
let private authorId = WebLogUserId "iIRNLSeY0EanxRPyqGuwVg"

/// The ID of the user added during the run of these tests
let private newId = WebLogUserId "new-user"

let ``Add succeeds`` (data: IData) = task {
    do! data.WebLogUser.Add
            { Id            = newId
              WebLogId      = rootId
              Email         = "new@example.com"
              FirstName     = "New"
              LastName      = "User"
              PreferredName = "n00b"
              PasswordHash  = "hashed-password"
              Url           = Some "https://example.com/~new"
              AccessLevel   = Author
              CreatedOn     = Noda.epoch + Duration.FromDays 365
              LastSeenOn    = None }
    let! user = data.WebLogUser.FindById newId rootId
    Expect.isSome user "There should have been a user returned"
    let it = user.Value
    Expect.equal it.Id newId "ID is incorrect"
    Expect.equal it.WebLogId rootId "Web log ID is incorrect"
    Expect.equal it.Email "new@example.com" "E-mail address is incorrect"
    Expect.equal it.FirstName "New" "First name is incorrect"
    Expect.equal it.LastName "User" "Last name is incorrect"
    Expect.equal it.PreferredName "n00b" "Preferred name is incorrect"
    Expect.equal it.PasswordHash "hashed-password" "Password hash is incorrect"
    Expect.equal it.Url (Some "https://example.com/~new") "URL is incorrect"
    Expect.equal it.AccessLevel Author "Access level is incorrect"
    Expect.equal it.CreatedOn (Noda.epoch + Duration.FromDays 365) "Created on is incorrect"
    Expect.isNone it.LastSeenOn "Last seen on should not have had a value"
}

let ``FindByEmail succeeds when a user is found`` (data: IData) = task {
    let! user = data.WebLogUser.FindByEmail "root@example.com" rootId
    Expect.isSome user "There should have been a user returned"
    Expect.equal user.Value.Id adminId "The wrong user was returned"
}

let ``FindByEmail succeeds when a user is not found (incorrect weblog)`` (data: IData) = task {
    let! user = data.WebLogUser.FindByEmail "root@example.com" (WebLogId "other")
    Expect.isNone user "There should not have been a user returned"
}

let ``FindByEmail succeeds when a user is not found (bad email)`` (data: IData) = task {
    let! user = data.WebLogUser.FindByEmail "wwwdata@example.com" rootId
    Expect.isNone user "There should not have been a user returned"
}

let ``FindById succeeds when a user is found`` (data: IData) = task {
    let! user = data.WebLogUser.FindById adminId rootId
    Expect.isSome user "There should have been a user returned"
    Expect.equal user.Value.Id adminId "The wrong user was returned"
    // The remainder of field population is tested in the "Add succeeds" test above
}

let ``FindById succeeds when a user is not found (incorrect weblog)`` (data: IData) = task {
    let! user = data.WebLogUser.FindById adminId (WebLogId "not-admin")
    Expect.isNone user "There should not have been a user returned"
}

let ``FindById succeeds when a user is not found (bad ID)`` (data: IData) = task {
    let! user = data.WebLogUser.FindById (WebLogUserId "tom") rootId
    Expect.isNone user "There should not have been a user returned"
}

let ``FindByWebLog succeeds when users exist`` (data: IData) = task {
    let! users = data.WebLogUser.FindByWebLog rootId
    Expect.hasLength users 4 "There should have been 4 users returned"
    for user in users do
        Expect.contains [ adminId; editorId; authorId; newId ] user.Id $"Unexpected user returned ({user.Id})"
}

let ``FindByWebLog succeeds when no users exist`` (data: IData) = task {
    let! users = data.WebLogUser.FindByWebLog (WebLogId "no-users")
    Expect.isEmpty users "There should have been no users returned"
}

let ``FindNames succeeds when users exist`` (data: IData) = task {
    let! names = data.WebLogUser.FindNames rootId [ editorId; authorId ]
    let expected =
        [ { Name = string editorId; Value = "Edits It-Or" }; { Name = string authorId; Value = "Mister Dude" } ]
    Expect.hasLength names 2 "There should have been 2 names returned"
    for name in names do Expect.contains expected name $"Unexpected name returned ({name.Name}|{name.Value})"
}

let ``FindNames succeeds when users do not exist`` (data: IData) = task {
    let! names = data.WebLogUser.FindNames rootId [ WebLogUserId "nope"; WebLogUserId "no" ]
    Expect.isEmpty names "There should have been no names returned"
}

let ``SetLastSeen succeeds when the user exists`` (data: IData) = task {
    let now = Noda.now ()
    do! data.WebLogUser.SetLastSeen newId rootId
    let! user = data.WebLogUser.FindById newId rootId
    Expect.isSome user "The user should have been returned"
    let it = user.Value
    Expect.isSome it.LastSeenOn "Last seen on should have been set"
    Expect.isGreaterThanOrEqual it.LastSeenOn.Value now "The last seen on date/time was not set correctly"
}

let ``SetLastSeen succeeds when the user does not exist`` (data: IData) = task {
    do! data.WebLogUser.SetLastSeen (WebLogUserId "matt") rootId
    Expect.isTrue true "This not raising an exception is the test"
}

let ``Update succeeds when the user exists`` (data: IData) = task {
    let! currentUser = data.WebLogUser.FindById newId rootId
    Expect.isSome currentUser "The current user should have been found"
    do! data.WebLogUser.Update
            { currentUser.Value with
                Email         = "newish@example.com"
                FirstName     = "New-ish"
                LastName      = "User-ish"
                PreferredName = "n00b-ish"
                PasswordHash  = "hashed-ish-password"
                Url           = None
                AccessLevel   = Editor }
    let! updated = data.WebLogUser.FindById newId rootId
    Expect.isSome updated "The updated user should have been returned"
    let it = updated.Value
    Expect.equal it.Id newId "ID is incorrect"
    Expect.equal it.WebLogId rootId "Web log ID is incorrect"
    Expect.equal it.Email "newish@example.com" "E-mail address is incorrect"
    Expect.equal it.FirstName "New-ish" "First name is incorrect"
    Expect.equal it.LastName "User-ish" "Last name is incorrect"
    Expect.equal it.PreferredName "n00b-ish" "Preferred name is incorrect"
    Expect.equal it.PasswordHash "hashed-ish-password" "Password hash is incorrect"
    Expect.isNone it.Url "URL is incorrect"
    Expect.equal it.AccessLevel Editor "Access level is incorrect"
    Expect.equal it.CreatedOn (Noda.epoch + Duration.FromDays 365) "Created on is incorrect"
    Expect.isSome it.LastSeenOn "Last seen on should have had a value"
}

let ``Update succeeds when the user does not exist`` (data: IData) = task {
    do! data.WebLogUser.Update { WebLogUser.Empty with Id = WebLogUserId "nothing"; WebLogId = rootId }
    let! updated = data.WebLogUser.FindById (WebLogUserId "nothing") rootId
    Expect.isNone updated "The update of a missing user should not have created the user"
}

let ``Delete fails when the user is the author of a page`` (data: IData) = task {
    match! data.WebLogUser.Delete adminId rootId with
    | Ok _ -> Expect.isTrue false "Deletion should have failed because the user is a page author"
    | Error msg -> Expect.equal msg "User has pages or posts; cannot delete" "Error message is incorrect"
}

let ``Delete fails when the user is the author of a post`` (data: IData) = task {
    match! data.WebLogUser.Delete authorId rootId with
    | Ok _ -> Expect.isTrue false "Deletion should have failed because the user is a post author"
    | Error msg -> Expect.equal msg "User has pages or posts; cannot delete" "Error message is incorrect"
}

let ``Delete succeeds when the user is not an author`` (data: IData) = task {
    match! data.WebLogUser.Delete newId rootId with
    | Ok _ -> Expect.isTrue true "This is the expected outcome"
    | Error msg -> Expect.isTrue false $"Deletion unexpectedly failed (message {msg})"
}

let ``Delete succeeds when the user does not exist`` (data: IData) = task {
    match! data.WebLogUser.Delete newId rootId with // already deleted above
    | Ok _ -> Expect.isTrue false "Deletion should have failed because the user does not exist"
    | Error msg -> Expect.equal msg "User does not exist" "Error message is incorrect"
}
