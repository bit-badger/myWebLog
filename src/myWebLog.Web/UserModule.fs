namespace myWebLog

open myWebLog.Data.User
open myWebLog.Entities
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
    this.Get .["/logon" ] <- fun parms -> this.ShowLogOn (downcast parms)
    this.Post.["/logon" ] <- fun parms -> this.DoLogOn   (downcast parms)
    this.Get .["/logoff"] <- fun parms -> this.LogOff ()

  /// Show the log on page
  member this.ShowLogOn (parameters : DynamicDictionary) =
    let model = LogOnModel(this.Context, this.WebLog)
    model.form.returnUrl <- match parameters.ContainsKey "returnUrl" with
                            | true -> parameters.["returnUrl"].ToString ()
                            | _    -> ""
    upcast this.View.["admin/user/logon", model]

  /// Process a user log on
  member this.DoLogOn (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    let form  = this.Bind<LogOnForm> ()
    let model = MyWebLogModel(this.Context, this.WebLog)
    match tryUserLogOn conn form.email (pbkdf2 form.password) with
    | Some user -> this.Session.[Keys.User] <- user
                   { level   = Level.Info
                     message = Resources.MsgLogOnSuccess
                     details = None }
                   |> model.addMessage
                   this.Redirect "" model |> ignore // Save the messages in the session before the Nancy redirect
                   // TODO: investigate if addMessage should update the session when it's called
                   upcast this.LoginAndRedirect (System.Guid.Parse user.id,
                                                 fallbackRedirectUrl = defaultArg (Option.ofObj(form.returnUrl)) "/")
    | None      -> { level   = Level.Error
                     message = Resources.ErrBadLogOnAttempt
                     details = None }
                   |> model.addMessage
                   this.Redirect (sprintf "/user/logon?returnUrl=%s" form.returnUrl) model

  /// Log a user off
  member this.LogOff () =
    let user = this.Request.PersistableSession.GetOrDefault<User> (Keys.User, User.empty)
    this.Session.DeleteAll ()
    let model = MyWebLogModel(this.Context, this.WebLog)
    { level   = Level.Info
      message = Resources.MsgLogOffSuccess
      details = None }
    |> model.addMessage
    this.Redirect "" model |> ignore
    upcast this.LogoutAndRedirect "/"
