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
    |> Seq.fold (fun acc bit -> System.String.Format("{0}{1:x2}", acc, bit)) ""
  
  do
    this.Get .["/logon" ] <- fun parms -> upcast this.ShowLogOn (downcast parms)
    this.Post.["/logon" ] <- fun parms -> upcast this.DoLogOn   (downcast parms)
    this.Get .["/logoff"] <- fun parms -> upcast this.LogOff ()

  /// Show the log on page
  member this.ShowLogOn (parameters : DynamicDictionary) =
    let model = LogOnModel(this.Context, this.WebLog)
    model.returnUrl <- defaultArg (Option.ofObj(downcast parameters.["returnUrl"])) ""
    this.View.["admin/user/logon", model]

  /// Process a user log on
  member this.DoLogOn (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    let model = this.Bind<LogOnModel> ()
    match tryUserLogOn conn model.email (pbkdf2 model.password) with
    | Some user -> this.Session.[Keys.User] <- user
                   { level   = Level.Info
                     message = Resources.MsgLogOnSuccess
                     details = None }
                   |> model.addMessage
                   this.Redirect "" model |> ignore // Save the messages in the session before the Nancy redirect
                   // TODO: investigate if addMessage should update the session when it's called
                   this.LoginAndRedirect
                     (System.Guid.Parse user.id, fallbackRedirectUrl = defaultArg (Option.ofObj(model.returnUrl)) "/")
    | None      -> { level   = Level.Error
                     message = Resources.ErrBadLogOnAttempt
                     details = None }
                   |> model.addMessage
                   this.Redirect "" model |> ignore // Save the messages in the session before the Nancy redirect
                   // Can't redirect with a negotiator when the other leg uses a straight response... :/
                   this.Response.AsRedirect((sprintf "/user/logon?returnUrl=%s" model.returnUrl),
                                            Responses.RedirectResponse.RedirectType.SeeOther)

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
    this.LogoutAndRedirect "/"
