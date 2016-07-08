module myWebLog.App

open myWebLog
open myWebLog.Data
open myWebLog.Data.SetUp
open myWebLog.Data.WebLog
open myWebLog.Entities
open Nancy
open Nancy.Authentication.Forms
open Nancy.Bootstrapper
open Nancy.Cryptography
open Nancy.Owin
open Nancy.Security
open Nancy.Session
open Nancy.Session.Persistable
open Nancy.Session.RethinkDb
open Nancy.TinyIoc
open Nancy.ViewEngines.SuperSimpleViewEngine
open RethinkDb.Driver
open RethinkDb.Driver.Net
open Suave
open Suave.Owin
open System.Text.RegularExpressions

/// Set up a database connection
let cfg =
  { database = "myWebLog"
    conn = RethinkDB.R.Connection()
             .Hostname(RethinkDBConstants.DefaultHostname)
             .Port(RethinkDBConstants.DefaultPort)
             .AuthKey(RethinkDBConstants.DefaultAuthkey)
             .Db("myWebLog")
             .Timeout(RethinkDBConstants.DefaultTimeout)
             .Connect() }

do
  startUpCheck cfg

type TranslateTokenViewEngineMatcher() =
  static let regex = Regex("@Translate\.(?<TranslationKey>[a-zA-Z0-9-_]+);?", RegexOptions.Compiled)
  interface ISuperSimpleViewEngineMatcher with
    member this.Invoke (content, model, host) =
      regex.Replace(content, fun m -> let key = m.Groups.["TranslationKey"].Value
                                      match Resources.ResourceManager.GetString key with
                                      | null -> key
                                      | xlat -> xlat)


/// Handle forms authentication
type MyWebLogUser(name, claims) =
  interface IUserIdentity with
    member this.UserName with get() = name
    member this.Claims   with get() = claims
  member this.UserName with get() = (this :> IUserIdentity).UserName
  member this.Claims   with get() = (this :> IUserIdentity).Claims
 
type MyWebLogUserMapper(container : TinyIoCContainer) =
  
  interface IUserMapper with
    member this.GetUserFromIdentifier (identifier, context) =
      match context.Request.PersistableSession.GetOrDefault(Keys.User, User.empty) with
      | user when user.id = string identifier -> upcast MyWebLogUser(user.preferredName, user.claims)
      | _ -> null


/// Set up the RethinkDB connection instance to be used by the IoC container
type ApplicationBootstrapper() =
  inherit DefaultNancyBootstrapper()
  override this.ConfigureRequestContainer (container, context) =
    base.ConfigureRequestContainer (container, context)
    container.Register<IUserMapper, MyWebLogUserMapper>()
    |> ignore
  override this.ApplicationStartup (container, pipelines) =
    base.ApplicationStartup (container, pipelines)
    // Data configuration
    container.Register<DataConfig>(cfg)
    |> ignore
    // I18N in SSVE
    container.Register<seq<ISuperSimpleViewEngineMatcher>>(fun _ _ -> 
      Seq.singleton (TranslateTokenViewEngineMatcher() :> ISuperSimpleViewEngineMatcher))
    |> ignore
    // Forms authentication configuration
    let salt = (System.Text.ASCIIEncoding()).GetBytes "NoneOfYourBeesWax"
    let auth  =
      FormsAuthenticationConfiguration(
        CryptographyConfiguration = CryptographyConfiguration
                                      (RijndaelEncryptionProvider(PassphraseKeyGenerator("Secrets",     salt)),
                                              DefaultHmacProvider(PassphraseKeyGenerator("Clandestine", salt))),
        RedirectUrl               = "~/user/logon",
        UserMapper                = container.Resolve<IUserMapper>())
    FormsAuthentication.Enable (pipelines, auth)
    // CSRF
    Csrf.Enable pipelines
    // Sessions
    let sessions = RethinkDbSessionConfiguration(cfg.conn)
    sessions.Database <- cfg.database
    PersistableSessions.Enable (pipelines, sessions)
    ()


let version = 
  let v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
  match v.Build with
  | 0 -> match v.Minor with
         | 0 -> string v.Major
         | _ -> sprintf "%d.%d" v.Major v.Minor
  | _ -> sprintf "%d.%d.%d" v.Major v.Minor v.Build
  |> sprintf "v%s"

/// Set up the request environment
type RequestEnvironment() =
  interface IRequestStartup with
    member this.Initialize (pipelines, context) =
      pipelines.BeforeRequest.AddItemToStartOfPipeline
        (fun ctx -> ctx.Items.["requestStart"] <- System.DateTime.Now.Ticks
                    match tryFindWebLogByUrlBase cfg ctx.Request.Url.HostName with
                    | Some webLog -> ctx.Items.["webLog"] <- webLog
                    | None        -> System.ApplicationException
                                       (sprintf "%s is not properly configured for myWebLog" ctx.Request.Url.HostName)
                                     |> raise
                    ctx.Items.["version"] <- version
                    null)

      
let app = OwinApp.ofMidFunc "/" (NancyMiddleware.UseNancy (NancyOptions()))

let run () = startWebServer defaultConfig app // webPart
