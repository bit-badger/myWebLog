﻿namespace MyWebLog

open MyWebLog.Data.User
open MyWebLog.Entities
open Nancy
open Nancy.Authentication.Forms
open Nancy.Cryptography
open Nancy.ModelBinding
open Nancy.Security
open Nancy.Session.Persistable
open RethinkDb.Driver.Net
open System.Text

/// Handle /user URLs
type UserModule(conn : IConnection) as this =
  inherit NancyModule("/user")

  /// Hash the user's password
  let pbkdf2 (pw : string) =
    PassphraseKeyGenerator(pw, UTF8Encoding().GetBytes("// TODO: make this salt part of the config"), 4096).GetBytes 512
    |> Seq.fold (fun acc byt -> sprintf "%s%s" acc (byt.ToString "x2")) ""
  
  do
    this.Get .["/logon" ] <- fun _     -> this.ShowLogOn ()
    this.Post.["/logon" ] <- fun parms -> this.DoLogOn   (downcast parms)
    this.Get .["/logoff"] <- fun _     -> this.LogOff ()

  /// Show the log on page
  member this.ShowLogOn () =
    let model = LogOnModel(this.Context, this.WebLog)
    let query = this.Request.Query :?> DynamicDictionary
    model.Form.ReturnUrl <- match query.ContainsKey "returnUrl" with
                            | true -> query.["returnUrl"].ToString ()
                            | _    -> ""
    upcast this.View.["admin/user/logon", model]

  /// Process a user log on
  member this.DoLogOn (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    let form  = this.Bind<LogOnForm> ()
    let model = MyWebLogModel(this.Context, this.WebLog)
    match tryUserLogOn conn form.Email (pbkdf2 form.Password) with
    | Some user -> this.Session.[Keys.User] <- user
                   { UserMessage.Empty with Level   = Level.Info
                                            Message = Resources.MsgLogOnSuccess }
                   |> model.AddMessage
                   this.Redirect "" model |> ignore // Save the messages in the session before the Nancy redirect
                   // TODO: investigate if addMessage should update the session when it's called
                   upcast this.LoginAndRedirect (System.Guid.Parse user.Id,
                                                 fallbackRedirectUrl = defaultArg (Option.ofObj form.ReturnUrl) "/")
    | None      -> { UserMessage.Empty with Level   = Level.Error
                                            Message = Resources.ErrBadLogOnAttempt }
                   |> model.AddMessage
                   this.Redirect (sprintf "/user/logon?returnUrl=%s" form.ReturnUrl) model

  /// Log a user off
  member this.LogOff () =
    // FIXME: why are we getting the user here if we don't do anything with it?
    let user = this.Request.PersistableSession.GetOrDefault<User> (Keys.User, User.Empty)
    this.Session.DeleteAll ()
    let model = MyWebLogModel(this.Context, this.WebLog)
    { UserMessage.Empty with Level   = Level.Info
                             Message = Resources.MsgLogOffSuccess }
    |> model.AddMessage
    this.Redirect "" model |> ignore
    upcast this.LogoutAndRedirect "/"
