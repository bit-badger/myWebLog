module myWebLog.App

open myWebLog
open myWebLog.Data
open myWebLog.Data.SetUp
open myWebLog.Data.WebLog
open myWebLog.Entities
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

/// Set up a database connection
let cfg = try DataConfig.fromJson (System.IO.File.ReadAllText "data-config.json")
          with ex -> raise <| ApplicationException(Resources.ErrDataConfig, ex)

do
  startUpCheck cfg


/// Support RESX lookup via the @Translate SSVE alias
type TranslateTokenViewEngineMatcher() =
  static let regex = Regex("@Translate\.(?<TranslationKey>[a-zA-Z0-9-_]+);?", RegexOptions.Compiled)
  interface ISuperSimpleViewEngineMatcher with
    member this.Invoke (content, model, host) =
      regex.Replace(content, fun m -> let key = m.Groups.["TranslationKey"].Value
                                      match myWebLog.Resources.ResourceManager.GetString key with
                                      | null -> key
                                      | xlat -> xlat)


/// Handle forms authentication
type MyWebLogUser(name, claims) =
  interface IUserIdentity with
    member this.UserName with get() = name
    member this.Claims   with get() = claims
(*member this.UserName with get() = (this :> IUserIdentity).UserName
  member this.Claims   with get() = (this :> IUserIdentity).Claims -- do we need these? *)
 
type MyWebLogUserMapper(container : TinyIoCContainer) =
  
  interface IUserMapper with
    member this.GetUserFromIdentifier (identifier, context) =
      match context.Request.PersistableSession.GetOrDefault(Keys.User, User.empty) with
      | user when user.id = string identifier -> upcast MyWebLogUser(user.preferredName, user.claims)
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
    Directory.EnumerateDirectories "views/themes"
    |> Seq.iter (fun dir -> let contentDir = sprintf "views/themes/%s/content" dir
                            match Directory.Exists contentDir with
                            | true -> conventions.StaticContentsConventions.Add
                                        (StaticContentConventionBuilder.AddDirectory (dir, contentDir))
                            | _    -> ())

  override this.ApplicationStartup (container, pipelines) =
    base.ApplicationStartup (container, pipelines)
    // Data configuration (both config and the connection; Nancy modules just need the connection)
    container.Register<DataConfig>(cfg)
    |> ignore
    container.Register<IConnection>(cfg.conn)
    |> ignore
    // NodaTime
    container.Register<IClock>(SystemClock.Instance)
    |> ignore
    // I18N in SSVE
    container.Register<seq<ISuperSimpleViewEngineMatcher>>(fun _ _ -> 
      Seq.singleton (TranslateTokenViewEngineMatcher() :> ISuperSimpleViewEngineMatcher))
    |> ignore
    // Forms authentication configuration
    let salt = (System.Text.ASCIIEncoding()).GetBytes "NoneOfYourBeesWax"
    let auth =
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
  let v = Reflection.Assembly.GetExecutingAssembly().GetName().Version
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
        (fun ctx -> ctx.Items.[Keys.RequestStart] <- DateTime.Now.Ticks
                    match tryFindWebLogByUrlBase cfg.conn ctx.Request.Url.HostName with
                    | Some webLog -> ctx.Items.[Keys.WebLog] <- webLog
                    | None        -> ApplicationException
                                       (sprintf "%s %s" ctx.Request.Url.HostName Resources.ErrNotConfigured)
                                     |> raise
                    ctx.Items.[Keys.Version] <- version
                    null)

      
let app = OwinApp.ofMidFunc "/" (NancyMiddleware.UseNancy (NancyOptions()))

let run () = startWebServer defaultConfig app // webPart
