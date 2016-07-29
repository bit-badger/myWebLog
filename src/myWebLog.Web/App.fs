module MyWebLog.App

open MyWebLog
open MyWebLog.Data
open MyWebLog.Data.SetUp
open MyWebLog.Data.WebLog
open MyWebLog.Entities
open Nancy
open Nancy.Authentication.Forms
open Nancy.Bootstrapper
open Nancy.Conventions
open Nancy.Cryptography
open Nancy.Owin
open Nancy.Security
open Nancy.Session.Persistable
open Nancy.Session.RethinkDb
open Nancy.TinyIoc
open Nancy.ViewEngines.SuperSimpleViewEngine
open NodaTime
open RethinkDb.Driver.Net
open Suave
open Suave.Owin
open System
open System.IO
open System.Text.RegularExpressions

/// Establish the configuration for this instance
let cfg = try AppConfig.FromJson (System.IO.File.ReadAllText "config.json")
          with ex -> raise <| ApplicationException(Resources.ErrBadAppConfig, ex)

do
  startUpCheck cfg.DataConfig
  
/// Support RESX lookup via the @Translate SSVE alias
type TranslateTokenViewEngineMatcher() =
  static let regex = Regex("@Translate\.(?<TranslationKey>[a-zA-Z0-9-_]+);?", RegexOptions.Compiled)
  interface ISuperSimpleViewEngineMatcher with
    member this.Invoke (content, model, host) =
      let translate (m : Match) =
        let key = m.Groups.["TranslationKey"].Value
        match MyWebLog.Resources.ResourceManager.GetString key with null -> key | xlat -> xlat
      regex.Replace(content, translate)


/// Handle forms authentication
type MyWebLogUser(name, claims) =
  interface IUserIdentity with
    member this.UserName with get() = name
    member this.Claims   with get() = claims
 
type MyWebLogUserMapper(container : TinyIoCContainer) =
  
  interface IUserMapper with
    member this.GetUserFromIdentifier (identifier, context) =
      match context.Request.PersistableSession.GetOrDefault(Keys.User, User.Empty) with
      | user when user.Id = string identifier -> upcast MyWebLogUser(user.PreferredName, user.Claims)
      | _ -> null


/// Set up the application environment
type MyWebLogBootstrapper() =
  inherit DefaultNancyBootstrapper()
  
  override this.ConfigureRequestContainer (container, context) =
    base.ConfigureRequestContainer (container, context)
    /// User mapper for forms authentication
    container.Register<IUserMapper, MyWebLogUserMapper>()
    |> ignore

  override this.ConfigureConventions (conventions) =
    base.ConfigureConventions conventions
    // Make theme content available at [theme-name]/
    let addContentDir dir =
      let contentDir = Path.Combine [| dir; "content" |]
      match Directory.Exists contentDir with
      | true -> conventions.StaticContentsConventions.Add
                  (StaticContentConventionBuilder.AddDirectory ((Path.GetFileName dir), contentDir))
      | _ -> ()
    conventions.StaticContentsConventions.Add
      (StaticContentConventionBuilder.AddDirectory("admin/content", "views/admin/content"))
    Directory.EnumerateDirectories (Path.Combine [| "views"; "themes" |])
    |> Seq.iter addContentDir

  override this.ApplicationStartup (container, pipelines) =
    base.ApplicationStartup (container, pipelines)
    // Data configuration (both config and the connection; Nancy modules just need the connection)
    container.Register<AppConfig>(cfg)
    |> ignore
    container.Register<IConnection>(cfg.DataConfig.Conn)
    |> ignore
    // NodaTime
    container.Register<IClock>(SystemClock.Instance)
    |> ignore
    // I18N in SSVE
    container.Register<seq<ISuperSimpleViewEngineMatcher>>(fun _ _ -> 
      Seq.singleton (TranslateTokenViewEngineMatcher() :> ISuperSimpleViewEngineMatcher))
    |> ignore
    // Forms authentication configuration
    let auth =
      FormsAuthenticationConfiguration(
        CryptographyConfiguration =
          CryptographyConfiguration(
            RijndaelEncryptionProvider(PassphraseKeyGenerator(cfg.AuthEncryptionPassphrase, cfg.AuthSalt)),
            DefaultHmacProvider(PassphraseKeyGenerator(cfg.AuthHmacPassphrase, cfg.AuthSalt))),
        RedirectUrl = "~/user/logon",
        UserMapper  = container.Resolve<IUserMapper>())
    FormsAuthentication.Enable (pipelines, auth)
    // CSRF
    Csrf.Enable pipelines
    // Sessions
    let sessions = RethinkDbSessionConfiguration(cfg.DataConfig.Conn)
    sessions.Database <- cfg.DataConfig.Database
    PersistableSessions.Enable (pipelines, sessions)
    ()


let version = 
  let v = Reflection.Assembly.GetExecutingAssembly().GetName().Version
  match v.Build with
  | 0 -> match v.Minor with 0 -> string v.Major | _ -> sprintf "%d.%d" v.Major v.Minor
  | _ -> sprintf "%d.%d.%d" v.Major v.Minor v.Build
  |> sprintf "v%s"

/// Set up the request environment
type RequestEnvironment() =
  interface IRequestStartup with
    member this.Initialize (pipelines, context) =
      let establishEnv (ctx : NancyContext) =
        ctx.Items.[Keys.RequestStart] <- DateTime.Now.Ticks
        match tryFindWebLogByUrlBase cfg.DataConfig.Conn ctx.Request.Url.HostName with
        | Some webLog -> ctx.Items.[Keys.WebLog] <- webLog
        | None -> // TODO: redirect to domain set up page
                  ApplicationException (sprintf "%s %s" ctx.Request.Url.HostName Resources.ErrNotConfigured)
                  |> raise
        ctx.Items.[Keys.Version] <- version
        null
      pipelines.BeforeRequest.AddItemToStartOfPipeline establishEnv

      
let app = OwinApp.ofMidFunc "/" (NancyMiddleware.UseNancy (NancyOptions()))

let Run () = startWebServer defaultConfig app // webPart
