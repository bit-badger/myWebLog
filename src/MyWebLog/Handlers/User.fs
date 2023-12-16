/// Handlers to manipulate users
module MyWebLog.Handlers.User

open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Identity
open MyWebLog
open NodaTime

// ~~ LOG ON / LOG OFF ~~

/// Create a password hash a password for a given user
let createPasswordHash user password =
    PasswordHasher<WebLogUser>().HashPassword (user, password)

/// Verify whether a password is valid
let verifyPassword user password (ctx : HttpContext) = backgroundTask {
    match user with
    | Some usr ->
        let hasher = PasswordHasher<WebLogUser> ()
        match hasher.VerifyHashedPassword (usr, usr.PasswordHash, password) with
        | PasswordVerificationResult.Success -> return Ok ()
        | PasswordVerificationResult.SuccessRehashNeeded ->
            do! ctx.Data.WebLogUser.Update { usr with PasswordHash = hasher.HashPassword (usr, password) }
            return Ok ()
        | _ -> return Error "Log on attempt unsuccessful"
    | None -> return Error "Log on attempt unsuccessful"
}

open Giraffe
open MyWebLog.ViewModels

// GET /user/log-on
let logOn returnUrl : HttpHandler = fun next ctx ->
    let returnTo =
        match returnUrl with
        | Some _ -> returnUrl
        | None -> if ctx.Request.Query.ContainsKey "returnUrl" then Some ctx.Request.Query["returnUrl"].[0] else None
    hashForPage "Log On"
    |> withAntiCsrf ctx
    |> addToHash ViewContext.Model { LogOnModel.empty with ReturnTo = returnTo }
    |> adminView "log-on" next ctx


open System.Security.Claims
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies

// POST /user/log-on
let doLogOn : HttpHandler = fun next ctx -> task {
    let! model   = ctx.BindFormAsync<LogOnModel>()
    let  data    = ctx.Data
    let! tryUser = data.WebLogUser.FindByEmail model.EmailAddress ctx.WebLog.Id
    match! verifyPassword tryUser model.Password ctx with 
    | Ok _ ->
        let user = tryUser.Value
        let claims = seq {
            Claim(ClaimTypes.NameIdentifier, string user.Id)
            Claim(ClaimTypes.Name,           $"{user.FirstName} {user.LastName}")
            Claim(ClaimTypes.GivenName,      user.PreferredName)
            Claim(ClaimTypes.Role,           string user.AccessLevel)
        }
        let identity = ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)

        do! ctx.SignInAsync(identity.AuthenticationType, ClaimsPrincipal identity,
            AuthenticationProperties(IssuedUtc = DateTimeOffset.UtcNow))
        do! data.WebLogUser.SetLastSeen user.Id user.WebLogId
        do! addMessage ctx
                { UserMessage.success with
                    Message = "Log on successful"
                    Detail  = Some $"Welcome to {ctx.WebLog.Name}!"
                }
        return!
            match model.ReturnTo with
            | Some url -> redirectTo false url next ctx
            | None -> redirectToGet "admin/dashboard" next ctx
    | Error msg ->
        do! addMessage ctx { UserMessage.error with Message = msg }
        return! logOn model.ReturnTo next ctx
}

// GET /user/log-off
let logOff : HttpHandler = fun next ctx -> task {
    do! ctx.SignOutAsync CookieAuthenticationDefaults.AuthenticationScheme
    do! addMessage ctx { UserMessage.info with Message = "Log off successful" }
    return! redirectToGet "" next ctx
}

// ~~ ADMINISTRATION ~~

open System.Collections.Generic
open Giraffe.Htmx

/// Got no time for URL/form manipulators...
let private goAway : HttpHandler = RequestErrors.BAD_REQUEST "really?"

// GET /admin/settings/users
let all : HttpHandler = fun next ctx -> task {
    let! users = ctx.Data.WebLogUser.FindByWebLog ctx.WebLog.Id
    return!
        hashForPage "User Administration"
        |> withAntiCsrf ctx
        |> addToHash "users" (users |> List.map (DisplayUser.fromUser ctx.WebLog) |> Array.ofList)
        |> adminBareView "user-list-body" next ctx
}

/// Show the edit user page
let private showEdit (model : EditUserModel) : HttpHandler = fun next ctx ->
    hashForPage (if model.IsNew then "Add a New User" else "Edit User")
    |> withAntiCsrf ctx
    |> addToHash ViewContext.Model model
    |> addToHash "access_levels" [|
        KeyValuePair.Create(string Author, "Author")
        KeyValuePair.Create(string Editor, "Editor")
        KeyValuePair.Create(string WebLogAdmin, "Web Log Admin")
        if ctx.HasAccessLevel Administrator then KeyValuePair.Create(string Administrator, "Administrator")
    |]
    |> adminBareView "user-edit" next ctx
    
// GET /admin/settings/user/{id}/edit
let edit usrId : HttpHandler = fun next ctx -> task {
    let isNew   = usrId = "new"
    let userId  = WebLogUserId usrId
    let tryUser =
        if isNew then someTask { WebLogUser.Empty with Id = userId }
        else ctx.Data.WebLogUser.FindById userId ctx.WebLog.Id
    match! tryUser with
    | Some user -> return! showEdit (EditUserModel.fromUser user) next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/settings/user/{id}/delete
let delete userId : HttpHandler = fun next ctx -> task {
    let data = ctx.Data
    match! data.WebLogUser.FindById (WebLogUserId userId) ctx.WebLog.Id with
    | Some user ->
        if user.AccessLevel = Administrator && not (ctx.HasAccessLevel Administrator) then
            return! goAway next ctx
        else
            match! data.WebLogUser.Delete user.Id user.WebLogId with
            | Ok _ ->
                do! addMessage ctx
                        { UserMessage.success with
                            Message = $"User {user.DisplayName} deleted successfully"
                        }
                return! all next ctx
            | Error msg ->
                do! addMessage ctx
                        { UserMessage.error with
                            Message = $"User {user.DisplayName} was not deleted"
                            Detail  = Some msg
                        }
                return! all next ctx
    | None -> return! Error.notFound next ctx
}

/// Display the user "my info" page, with information possibly filled in
let private showMyInfo (model: EditMyInfoModel) (user: WebLogUser) : HttpHandler = fun next ctx ->
    hashForPage "Edit Your Information"
    |> withAntiCsrf ctx
    |> addToHash ViewContext.Model model
    |> addToHash "access_level"    (string user.AccessLevel)
    |> addToHash "created_on"      (ctx.WebLog.LocalTime user.CreatedOn)
    |> addToHash "last_seen_on"    (ctx.WebLog.LocalTime (defaultArg user.LastSeenOn (Instant.FromUnixTimeSeconds 0)))
    |> adminView "my-info" next ctx


// GET /admin/my-info
let myInfo : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.WebLogUser.FindById ctx.UserId ctx.WebLog.Id with
    | Some user -> return! showMyInfo (EditMyInfoModel.fromUser user) user next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/my-info
let saveMyInfo : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<EditMyInfoModel> ()
    let  data  = ctx.Data
    match! data.WebLogUser.FindById ctx.UserId ctx.WebLog.Id with
    | Some user when model.NewPassword = model.NewPasswordConfirm ->
        let pw = if model.NewPassword = "" then user.PasswordHash else createPasswordHash user model.NewPassword
        let user =
            { user with
                FirstName     = model.FirstName
                LastName      = model.LastName
                PreferredName = model.PreferredName
                PasswordHash  = pw
            }
        do! data.WebLogUser.Update user
        let pwMsg = if model.NewPassword = "" then "" else " and updated your password"
        do! addMessage ctx { UserMessage.success with Message = $"Saved your information{pwMsg} successfully" }
        return! redirectToGet "admin/my-info" next ctx
    | Some user ->
        do! addMessage ctx { UserMessage.error with Message = "Passwords did not match; no updates made" }
        return! showMyInfo { model with NewPassword = ""; NewPasswordConfirm = "" } user next ctx
    | None -> return! Error.notFound next ctx
}

// User save is not statically compilable; not sure why, but we'll revisit it at some point
#nowarn "3511"

// POST /admin/settings/user/save
let save : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let! model   = ctx.BindFormAsync<EditUserModel> ()
    let  data    = ctx.Data
    let  tryUser =
        if model.IsNew then
            { WebLogUser.Empty with
                Id        = WebLogUserId.Create()
                WebLogId  = ctx.WebLog.Id
                CreatedOn = Noda.now ()
            } |> someTask
        else data.WebLogUser.FindById (WebLogUserId model.Id) ctx.WebLog.Id
    match! tryUser with
    | Some user when model.Password = model.PasswordConfirm ->
        let updatedUser = model.UpdateUser user
        if updatedUser.AccessLevel = Administrator && not (ctx.HasAccessLevel Administrator) then
            return! goAway next ctx
        else
            let toUpdate =
                if model.Password = "" then updatedUser
                else { updatedUser with PasswordHash = createPasswordHash updatedUser model.Password }
            do! (if model.IsNew then data.WebLogUser.Add else data.WebLogUser.Update) toUpdate
            do! addMessage ctx
                    { UserMessage.success with
                        Message = $"""{if model.IsNew then "Add" else "Updat"}ed user successfully"""
                    }
            return! all next ctx
    | Some _ ->
        do! addMessage ctx { UserMessage.error with Message = "The passwords did not match; nothing saved" }
        return!
            (withHxRetarget $"#user_{model.Id}" >=> showEdit { model with Password = ""; PasswordConfirm = "" })
                next ctx
    | None -> return! Error.notFound next ctx
}
