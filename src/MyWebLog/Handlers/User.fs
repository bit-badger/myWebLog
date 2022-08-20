/// Handlers to manipulate users
module MyWebLog.Handlers.User

open System
open System.Security.Cryptography
open System.Text
open NodaTime

// ~~ LOG ON / LOG OFF ~~

/// Hash a password for a given user
let hashedPassword (plainText : string) (email : string) (salt : Guid) =
    let allSalt = Array.concat [ salt.ToByteArray (); Encoding.UTF8.GetBytes email ] 
    use alg     = new Rfc2898DeriveBytes (plainText, allSalt, 2_048)
    Convert.ToBase64String (alg.GetBytes 64)

open Giraffe
open MyWebLog
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
                { UserMessage.success with
                    Message = "Log on successful"
                    Detail  = Some $"Welcome to {ctx.WebLog.Name}!"
                }
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

// ~~ ADMINISTRATION ~~

open System.Collections.Generic
open Giraffe.Htmx

/// Got no time for URL/form manipulators...
let private goAway : HttpHandler = RequestErrors.BAD_REQUEST "really?"

// GET /admin/settings/users
let all : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
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
        KeyValuePair.Create (AccessLevel.toString Author, "Author")
        KeyValuePair.Create (AccessLevel.toString Editor, "Editor")
        KeyValuePair.Create (AccessLevel.toString WebLogAdmin, "Web Log Admin")
        if ctx.HasAccessLevel Administrator then
            KeyValuePair.Create (AccessLevel.toString Administrator, "Administrator")
    |]
    |> adminBareView "user-edit" next ctx
    
// GET /admin/settings/user/{id}/edit
let edit usrId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let isNew   = usrId = "new"
    let userId  = WebLogUserId usrId
    let tryUser =
        if isNew then someTask { WebLogUser.empty with Id = userId }
        else ctx.Data.WebLogUser.FindById userId ctx.WebLog.Id
    match! tryUser with
    | Some user -> return! showEdit (EditUserModel.fromUser user) next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/settings/user/{id}/delete
let delete userId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
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
                            Message = $"User {WebLogUser.displayName user} deleted successfully"
                        }
                return! all next ctx
            | Error msg ->
                do! addMessage ctx
                        { UserMessage.error with
                            Message = $"User {WebLogUser.displayName user} was not deleted"
                            Detail  = Some msg
                        }
                return! all next ctx
    | None -> return! Error.notFound next ctx
}

/// Display the user "my info" page, with information possibly filled in
let private showMyInfo (model : EditMyInfoModel) (user : WebLogUser) : HttpHandler = fun next ctx ->
    hashForPage "Edit Your Information"
    |> withAntiCsrf ctx
    |> addToHash ViewContext.Model model
    |> addToHash "access_level"    (AccessLevel.toString user.AccessLevel)
    |> addToHash "created_on"      (WebLog.localTime ctx.WebLog user.CreatedOn)
    |> addToHash "last_seen_on"    (WebLog.localTime ctx.WebLog
                                         (defaultArg user.LastSeenOn (Instant.FromUnixTimeSeconds 0)))
                                         
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
            { WebLogUser.empty with
                Id        = WebLogUserId.create ()
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
                else
                    let salt = Guid.NewGuid ()
                    { updatedUser with PasswordHash = hashedPassword model.Password model.Email salt; Salt = salt }
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
