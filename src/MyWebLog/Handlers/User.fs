/// Handlers to manipulate users
module MyWebLog.Handlers.User

open System
open System.Security.Cryptography
open System.Text

/// Hash a password for a given user
let hashedPassword (plainText : string) (email : string) (salt : Guid) =
    let allSalt = Array.concat [ salt.ToByteArray (); Encoding.UTF8.GetBytes email ] 
    use alg     = new Rfc2898DeriveBytes (plainText, allSalt, 2_048)
    Convert.ToBase64String (alg.GetBytes 64)

open DotLiquid
open Giraffe
open MyWebLog.ViewModels

// GET /user/log-on
let logOn returnUrl : HttpHandler = fun next ctx -> task {
    let returnTo =
        match returnUrl with
        | Some _ -> returnUrl
        | None ->
            match ctx.Request.Query.ContainsKey "returnUrl" with
            | true -> Some ctx.Request.Query["returnUrl"].[0]
            | false -> None
    return!
        Hash.FromAnonymousObject {|
            model      = { LogOnModel.empty with returnTo = returnTo }
            page_title = "Log On"
            csrf       = csrfToken ctx
        |}
        |> viewForTheme "admin" "log-on" next ctx
}

open System.Security.Claims
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open MyWebLog

// POST /user/log-on
let doLogOn : HttpHandler = fun next ctx -> task {
    let! model  = ctx.BindFormAsync<LogOnModel> ()
    let  webLog = ctx.WebLog
    match! Data.WebLogUser.findByEmail model.emailAddress webLog.id ctx.Conn with 
    | Some user when user.passwordHash = hashedPassword model.password user.userName user.salt ->
        let claims = seq {
            Claim (ClaimTypes.NameIdentifier, WebLogUserId.toString user.id)
            Claim (ClaimTypes.Name,           $"{user.firstName} {user.lastName}")
            Claim (ClaimTypes.GivenName,      user.preferredName)
            Claim (ClaimTypes.Role,           user.authorizationLevel.ToString ())
        }
        let identity = ClaimsIdentity (claims, CookieAuthenticationDefaults.AuthenticationScheme)

        do! ctx.SignInAsync (identity.AuthenticationType, ClaimsPrincipal identity,
            AuthenticationProperties (IssuedUtc = DateTimeOffset.UtcNow))
        do! addMessage ctx
                { UserMessage.success with message = $"Logged on successfully | Welcome to {webLog.name}!" }
        return! redirectToGet (defaultArg model.returnTo (WebLog.relativeUrl webLog (Permalink "admin/dashboard")))
                    next ctx
    | _ ->
        do! addMessage ctx { UserMessage.error with message = "Log on attempt unsuccessful" }
        return! logOn model.returnTo next ctx
}

// GET /user/log-off
let logOff : HttpHandler = fun next ctx -> task {
    do! ctx.SignOutAsync CookieAuthenticationDefaults.AuthenticationScheme
    do! addMessage ctx { UserMessage.info with message = "Log off successful" }
    return! redirectToGet (WebLog.relativeUrl ctx.WebLog Permalink.empty) next ctx
}

/// Display the user edit page, with information possibly filled in
let private showEdit (hash : Hash) : HttpHandler = fun next ctx -> task {
    hash.Add ("page_title", "Edit Your Information")
    hash.Add ("csrf", csrfToken ctx)
    return! viewForTheme "admin" "user-edit" next ctx hash
}

// GET /admin/user/edit
let edit : HttpHandler = fun next ctx -> task {
    match! Data.WebLogUser.findById (userId ctx) ctx.Conn with
    | Some user -> return! showEdit (Hash.FromAnonymousObject {| model = EditUserModel.fromUser user |}) next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/user/save
let save : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<EditUserModel> ()
    if model.newPassword = model.newPasswordConfirm then
        let conn = ctx.Conn
        match! Data.WebLogUser.findById (userId ctx) conn with
        | Some user ->
            let pw, salt =
                if model.newPassword = "" then
                    user.passwordHash, user.salt
                else
                    let newSalt = Guid.NewGuid ()
                    hashedPassword model.newPassword user.userName newSalt, newSalt
            let user =
                { user with
                    firstName     = model.firstName
                    lastName      = model.lastName
                    preferredName = model.preferredName
                    passwordHash  = pw
                    salt          = salt
                }
            do! Data.WebLogUser.update user conn
            let pwMsg = if model.newPassword = "" then "" else " and updated your password"
            do! addMessage ctx { UserMessage.success with message = $"Saved your information{pwMsg} successfully" }
            return! redirectToGet (WebLog.relativeUrl ctx.WebLog (Permalink "admin/user/edit")) next ctx
        | None -> return! Error.notFound next ctx
    else
        do! addMessage ctx { UserMessage.error with message = "Passwords did not match; no updates made" }
        return! showEdit (Hash.FromAnonymousObject {|
                model = { model with newPassword = ""; newPasswordConfirm = "" }
            |}) next ctx
}
