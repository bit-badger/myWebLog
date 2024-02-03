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
