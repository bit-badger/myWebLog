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
let logOn returnUrl : HttpHandler = fun next ctx ->
    let returnTo =
        match returnUrl with
        | Some _ -> returnUrl
        | None -> if ctx.Request.Query.ContainsKey "returnUrl" then Some ctx.Request.Query["returnUrl"].[0] else None
    Hash.FromAnonymousObject {|
        page_title = "Log On"
        csrf       = ctx.CsrfTokenSet
        model      = { LogOnModel.empty with ReturnTo = returnTo }
    |}
    |> viewForTheme "admin" "log-on" next ctx


open System.Security.Claims
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies

// POST /user/log-on
let doLogOn : HttpHandler = fun next ctx -> task {
    let! model = ctx.BindFormAsync<LogOnModel> ()
    let  data  = ctx.Data
    match! data.WebLogUser.FindByEmail model.EmailAddress ctx.WebLog.id with 
    | Some user when user.passwordHash = hashedPassword model.Password user.userName user.salt ->
        let claims = seq {
            Claim (ClaimTypes.NameIdentifier, WebLogUserId.toString user.id)
            Claim (ClaimTypes.Name,           $"{user.firstName} {user.lastName}")
            Claim (ClaimTypes.GivenName,      user.preferredName)
            Claim (ClaimTypes.Role,           AccessLevel.toString user.accessLevel)
        }
        let identity = ClaimsIdentity (claims, CookieAuthenticationDefaults.AuthenticationScheme)

        do! ctx.SignInAsync (identity.AuthenticationType, ClaimsPrincipal identity,
            AuthenticationProperties (IssuedUtc = DateTimeOffset.UtcNow))
        do! data.WebLogUser.SetLastSeen user.id user.webLogId
        do! addMessage ctx
                { UserMessage.success with Message = $"Logged on successfully | Welcome to {ctx.WebLog.name}!" }
        return!
            match model.ReturnTo with
            | Some url -> redirectTo false url next ctx
            | None -> redirectToGet "admin/dashboard" next ctx
    | _ ->
        do! addMessage ctx { UserMessage.error with Message = "Log on attempt unsuccessful" }
        return! logOn model.ReturnTo next ctx
}

// GET /user/log-off
let logOff : HttpHandler = fun next ctx -> task {
    do! ctx.SignOutAsync CookieAuthenticationDefaults.AuthenticationScheme
    do! addMessage ctx { UserMessage.info with Message = "Log off successful" }
    return! redirectToGet "" next ctx
}

/// Display the user edit page, with information possibly filled in
let private showEdit (hash : Hash) : HttpHandler = fun next ctx ->
       addToHash "page_title" "Edit Your Information" hash
    |> addToHash "csrf"       ctx.CsrfTokenSet
    |> viewForTheme "admin" "user-edit" next ctx


// GET /admin/user/edit
let edit : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.WebLogUser.FindById ctx.UserId ctx.WebLog.id with
    | Some user -> return! showEdit (Hash.FromAnonymousObject {| model = EditUserModel.fromUser user |}) next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/user/save
let save : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<EditUserModel> ()
    if model.NewPassword = model.NewPasswordConfirm then
        let data = ctx.Data
        match! data.WebLogUser.FindById ctx.UserId ctx.WebLog.id with
        | Some user ->
            let pw, salt =
                if model.NewPassword = "" then
                    user.passwordHash, user.salt
                else
                    let newSalt = Guid.NewGuid ()
                    hashedPassword model.NewPassword user.userName newSalt, newSalt
            let user =
                { user with
                    firstName     = model.FirstName
                    lastName      = model.LastName
                    preferredName = model.PreferredName
                    passwordHash  = pw
                    salt          = salt
                }
            do! data.WebLogUser.Update user
            let pwMsg = if model.NewPassword = "" then "" else " and updated your password"
            do! addMessage ctx { UserMessage.success with Message = $"Saved your information{pwMsg} successfully" }
            return! redirectToGet "admin/user/edit" next ctx
        | None -> return! Error.notFound next ctx
    else
        do! addMessage ctx { UserMessage.error with Message = "Passwords did not match; no updates made" }
        return! showEdit (Hash.FromAnonymousObject {|
                model = { model with NewPassword = ""; NewPasswordConfirm = "" }
            |}) next ctx
}
