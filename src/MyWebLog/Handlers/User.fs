/// Handlers to manipulate users
module MyWebLog.Handlers.User

open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Identity
open MyWebLog

// ~~ LOG ON / LOG OFF ~~

/// Create a password hash a password for a given user
let createPasswordHash user password =
    PasswordHasher<WebLogUser>().HashPassword(user, password)

/// Verify whether a password is valid
let verifyPassword user password (ctx: HttpContext) = backgroundTask {
    match user with
    | Some usr ->
        let hasher = PasswordHasher<WebLogUser>()
        match hasher.VerifyHashedPassword(usr, usr.PasswordHash, password) with
        | PasswordVerificationResult.Success -> return Ok ()
        | PasswordVerificationResult.SuccessRehashNeeded ->
            do! ctx.Data.WebLogUser.Update { usr with PasswordHash = hasher.HashPassword(usr, password) }
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
    adminPage "Log On" true next ctx (Views.User.logOn { LogOnModel.Empty with ReturnTo = returnTo })


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
                { UserMessage.Success with
                    Message = "Log on successful"
                    Detail  = Some $"Welcome to {ctx.WebLog.Name}!" }
        return!
            match model.ReturnTo with
            | Some url -> redirectTo false url next ctx // TODO: change to redirectToGet?
            | None -> redirectToGet "admin/dashboard" next ctx
    | Error msg ->
        do! addMessage ctx { UserMessage.Error with Message = msg }
        return! logOn model.ReturnTo next ctx
}

// GET /user/log-off
let logOff : HttpHandler = fun next ctx -> task {
    do! ctx.SignOutAsync CookieAuthenticationDefaults.AuthenticationScheme
    do! addMessage ctx { UserMessage.Info with Message = "Log off successful" }
    return! redirectToGet "" next ctx
}

// ~~ ADMINISTRATION ~~

open Giraffe.Htmx

/// Got no time for URL/form manipulators...
let private goAway : HttpHandler = RequestErrors.BAD_REQUEST "really?"

// GET /admin/settings/users
let all : HttpHandler = fun next ctx -> task {
    let! users = ctx.Data.WebLogUser.FindByWebLog ctx.WebLog.Id
    return! adminBarePage "User Administration" true next ctx (Views.User.userList users)
}

/// Show the edit user page
let private showEdit (model: EditUserModel) : HttpHandler = fun next ctx ->
    adminBarePage (if model.IsNew then "Add a New User" else "Edit User") true next ctx (Views.User.edit model)
    
// GET /admin/settings/user/{id}/edit
let edit usrId : HttpHandler = fun next ctx -> task {
    let isNew   = usrId = "new"
    let userId  = WebLogUserId usrId
    let tryUser =
        if isNew then someTask { WebLogUser.Empty with Id = userId }
        else ctx.Data.WebLogUser.FindById userId ctx.WebLog.Id
    match! tryUser with
    | Some user -> return! showEdit (EditUserModel.FromUser user) next ctx
    | None -> return! Error.notFound next ctx
}

// DELETE /admin/settings/user/{id}
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
                        { UserMessage.Success with
                            Message = $"User {user.DisplayName} deleted successfully" }
                return! all next ctx
            | Error msg ->
                do! addMessage ctx
                        { UserMessage.Error with
                            Message = $"User {user.DisplayName} was not deleted"
                            Detail  = Some msg }
                return! all next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/my-info
let myInfo : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.WebLogUser.FindById ctx.UserId ctx.WebLog.Id with
    | Some user ->
        return!
            Views.User.myInfo (EditMyInfoModel.FromUser user) user
            |> adminPage "Edit Your Information" true next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/my-info
let saveMyInfo : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model = ctx.BindFormAsync<EditMyInfoModel>()
    let  data  = ctx.Data
    match! data.WebLogUser.FindById ctx.UserId ctx.WebLog.Id with
    | Some user when model.NewPassword = model.NewPasswordConfirm ->
        let pw = if model.NewPassword = "" then user.PasswordHash else createPasswordHash user model.NewPassword
        let user =
            { user with
                FirstName     = model.FirstName
                LastName      = model.LastName
                PreferredName = model.PreferredName
                PasswordHash  = pw }
        do! data.WebLogUser.Update user
        let pwMsg = if model.NewPassword = "" then "" else " and updated your password"
        do! addMessage ctx { UserMessage.Success with Message = $"Saved your information{pwMsg} successfully" }
        return! redirectToGet "admin/my-info" next ctx
    | Some user ->
        do! addMessage ctx { UserMessage.Error with Message = "Passwords did not match; no updates made" }
        return!
            Views.User.myInfo { model with NewPassword = ""; NewPasswordConfirm = "" } user
            |> adminPage "Edit Your Information" true next ctx
    | None -> return! Error.notFound next ctx
}

// User save is not statically compilable; not sure why, but we'll revisit it at some point
#nowarn "3511"

// POST /admin/settings/user/save
let save : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    let! model   = ctx.BindFormAsync<EditUserModel>()
    let  data    = ctx.Data
    let  tryUser =
        if model.IsNew then
            { WebLogUser.Empty with
                Id        = WebLogUserId.Create()
                WebLogId  = ctx.WebLog.Id
                CreatedOn = Noda.now () }
            |> someTask
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
                    { UserMessage.Success with
                        Message = $"""{if model.IsNew then "Add" else "Updat"}ed user successfully""" }
            return! all next ctx
    | Some _ ->
        do! addMessage ctx { UserMessage.Error with Message = "The passwords did not match; nothing saved" }
        return!
            (withHxRetarget $"#user_{model.Id}" >=> showEdit { model with Password = ""; PasswordConfirm = "" })
                next ctx
    | None -> return! Error.notFound next ctx
}
