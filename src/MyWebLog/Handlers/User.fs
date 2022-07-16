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
open MyWebLog
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
            page_title = "Log On"
            csrf       = ctx.CsrfTokenSet
            model      = { LogOnModel.empty with returnTo = returnTo }
        |}
        |> viewForTheme "admin" "log-on" next ctx
}

open System.Security.Claims
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies

// POST /user/log-on
let doLogOn : HttpHandler = fun next ctx -> task {
    let! model = ctx.BindFormAsync<LogOnModel> ()
    match! ctx.Data.WebLogUser.findByEmail model.emailAddress ctx.WebLog.id with 
    | Some user when user.passwordHash = hashedPassword model.password user.userName user.salt ->
        let claims = seq {
            Claim (ClaimTypes.NameIdentifier, WebLogUserId.toString user.id)
            Claim (ClaimTypes.Name,           $"{user.firstName} {user.lastName}")
            Claim (ClaimTypes.GivenName,      user.preferredName)
            Claim (ClaimTypes.Role,           AccessLevel.toString user.accessLevel)
        }
        let identity = ClaimsIdentity (claims, CookieAuthenticationDefaults.AuthenticationScheme)

        do! ctx.SignInAsync (identity.AuthenticationType, ClaimsPrincipal identity,
            AuthenticationProperties (IssuedUtc = DateTimeOffset.UtcNow))
        do! addMessage ctx
                { UserMessage.success with message = $"Logged on successfully | Welcome to {ctx.WebLog.name}!" }
        return! redirectToGet (defaultArg (model.returnTo |> Option.map (fun it -> it[1..])) "admin/dashboard") next ctx
    | _ ->
        do! addMessage ctx { UserMessage.error with message = "Log on attempt unsuccessful" }
        return! logOn model.returnTo next ctx
}

// GET /user/log-off
let logOff : HttpHandler = fun next ctx -> task {
    do! ctx.SignOutAsync CookieAuthenticationDefaults.AuthenticationScheme
    do! addMessage ctx { UserMessage.info with message = "Log off successful" }
    return! redirectToGet "" next ctx
}

/// Display the user edit page, with information possibly filled in
let private showEdit (hash : Hash) : HttpHandler = fun next ctx -> task {
    hash.Add ("page_title", "Edit Your Information")
    hash.Add ("csrf", ctx.CsrfTokenSet)
    return! viewForTheme "admin" "user-edit" next ctx hash
}

// GET /admin/user/edit
let edit : HttpHandler = fun next ctx -> task {
    match! ctx.Data.WebLogUser.findById ctx.UserId ctx.WebLog.id with
    | Some user -> return! showEdit (Hash.FromAnonymousObject {| model = EditUserModel.fromUser user |}) next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/user/save
let save : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<EditUserModel> ()
    if model.newPassword = model.newPasswordConfirm then
        let data = ctx.Data
        match! data.WebLogUser.findById ctx.UserId ctx.WebLog.id with
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
            do! data.WebLogUser.update user
            let pwMsg = if model.newPassword = "" then "" else " and updated your password"
            do! addMessage ctx { UserMessage.success with message = $"Saved your information{pwMsg} successfully" }
            return! redirectToGet "admin/user/edit" next ctx
        | None -> return! Error.notFound next ctx
    else
        do! addMessage ctx { UserMessage.error with message = "Passwords did not match; no updates made" }
        return! showEdit (Hash.FromAnonymousObject {|
                model = { model with newPassword = ""; newPasswordConfirm = "" }
            |}) next ctx
}
