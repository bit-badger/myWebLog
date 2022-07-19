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
    |> adminView "log-on" next ctx


open System.Security.Claims
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies

// POST /user/log-on
let doLogOn : HttpHandler = fun next ctx -> task {
    let! model = ctx.BindFormAsync<LogOnModel> ()
    let  data  = ctx.Data
    match! data.WebLogUser.FindByEmail model.EmailAddress ctx.WebLog.Id with 
    | Some user when user.PasswordHash = hashedPassword model.Password user.Email user.Salt ->
        let claims = seq {
            Claim (ClaimTypes.NameIdentifier, WebLogUserId.toString user.Id)
            Claim (ClaimTypes.Name,           $"{user.FirstName} {user.LastName}")
            Claim (ClaimTypes.GivenName,      user.PreferredName)
            Claim (ClaimTypes.Role,           AccessLevel.toString user.AccessLevel)
        }
        let identity = ClaimsIdentity (claims, CookieAuthenticationDefaults.AuthenticationScheme)

        do! ctx.SignInAsync (identity.AuthenticationType, ClaimsPrincipal identity,
            AuthenticationProperties (IssuedUtc = DateTimeOffset.UtcNow))
        do! data.WebLogUser.SetLastSeen user.Id user.WebLogId
        do! addMessage ctx
                { UserMessage.success with Message = $"Logged on successfully | Welcome to {ctx.WebLog.Name}!" }
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

/// Display the user "my info" page, with information possibly filled in
let private showMyInfo (user : WebLogUser) (hash : Hash) : HttpHandler = fun next ctx ->
       addToHash "page_title"   "Edit Your Information" hash
    |> addToHash "csrf"         ctx.CsrfTokenSet
    |> addToHash "access_level" (AccessLevel.toString user.AccessLevel)
    |> addToHash "created_on"   (WebLog.localTime ctx.WebLog user.CreatedOn)
    |> addToHash "last_seen_on" (WebLog.localTime ctx.WebLog (defaultArg user.LastSeenOn DateTime.UnixEpoch))
    |> adminView "my-info" next ctx


// GET /admin/user/my-info
let myInfo : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.WebLogUser.FindById ctx.UserId ctx.WebLog.Id with
    | Some user -> return! showMyInfo user (Hash.FromAnonymousObject {| model = EditMyInfoModel.fromUser user |}) next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/user/my-info
let saveMyInfo : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<EditMyInfoModel> ()
    let  data  = ctx.Data
    match! data.WebLogUser.FindById ctx.UserId ctx.WebLog.Id with
    | Some user ->
        if model.NewPassword = model.NewPasswordConfirm then
            let pw, salt =
                if model.NewPassword = "" then
                    user.PasswordHash, user.Salt
                else
                    let newSalt = Guid.NewGuid ()
                    hashedPassword model.NewPassword user.Email newSalt, newSalt
            let user =
                { user with
                    FirstName     = model.FirstName
                    LastName      = model.LastName
                    PreferredName = model.PreferredName
                    PasswordHash  = pw
                    Salt          = salt
                }
            do! data.WebLogUser.Update user
            let pwMsg = if model.NewPassword = "" then "" else " and updated your password"
            do! addMessage ctx { UserMessage.success with Message = $"Saved your information{pwMsg} successfully" }
            return! redirectToGet "admin/user/my-info" next ctx
        else
            do! addMessage ctx { UserMessage.error with Message = "Passwords did not match; no updates made" }
            return! showMyInfo user (Hash.FromAnonymousObject {|
                    model = { model with NewPassword = ""; NewPasswordConfirm = "" }
                |}) next ctx
    | None -> return! Error.notFound next ctx
}
