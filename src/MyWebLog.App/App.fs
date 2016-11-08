module MyWebLog.App

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open MyWebLog
open MyWebLog.Data
open MyWebLog.Data.RethinkDB
open MyWebLog.Entities
open MyWebLog.Logic.WebLog
open MyWebLog.Resources
open Nancy
open Nancy.Authentication.Forms
open Nancy.Bootstrapper
open Nancy.Conventions
open Nancy.Cryptography
open Nancy.Owin
open Nancy.Security
open Nancy.Session.Persistable
//open Nancy.Session.Relational
open Nancy.Session.RethinkDB
open Nancy.TinyIoc
open Nancy.ViewEngines.SuperSimpleViewEngine
open NodaTime
open RethinkDb.Driver.Net
open System
open System.IO
open System.Reflection
open System.Security.Claims
open System.Text.RegularExpressions

/// Establish the configuration for this instance
let cfg = try AppConfig.FromJson (System.IO.File.ReadAllText "config.json")
          with ex -> raise <| Exception (Strings.get "ErrBadAppConfig", ex)

let data = lazy (RethinkMyWebLogData(cfg.DataConfig.Conn, cfg.DataConfig) :> IMyWebLogData)

/// Support RESX lookup via the @Translate SSVE alias
type TranslateTokenViewEngineMatcher() =
  static let regex = Regex("@Translate\.(?<TranslationKey>[a-zA-Z0-9-_]+);?", RegexOptions.Compiled)
  interface ISuperSimpleViewEngineMatcher with
    member this.Invoke (content, model, host) =
      let translate (m : Match) = Strings.get m.Groups.["TranslationKey"].Value
      regex.Replace(content, translate)


/// Handle forms authentication
type MyWebLogUser(name, claims) =
  inherit ClaimsPrincipal()
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

  override this.ConfigureApplicationContainer (container) =
    base.ConfigureApplicationContainer container
    container.Register<AppConfig>(cfg)
    |> ignore
    data.Force().SetUp ()
    container.Register<IMyWebLogData>(data.Force ())
    |> ignore
    // NodaTime
    container.Register<IClock>(SystemClock.Instance)
    |> ignore
    // I18N in SSVE
    container.Register<seq<ISuperSimpleViewEngineMatcher>>(fun _ _ -> 
      Seq.singleton (TranslateTokenViewEngineMatcher() :> ISuperSimpleViewEngineMatcher))
    |> ignore
  
  override this.ApplicationStartup (container, pipelines) =
    base.ApplicationStartup (container, pipelines)
    // Forms authentication configuration
    let auth =
      FormsAuthenticationConfiguration(
        CryptographyConfiguration =
          CryptographyConfiguration(
            AesEncryptionProvider(PassphraseKeyGenerator(cfg.AuthEncryptionPassphrase, cfg.AuthSalt)),
            DefaultHmacProvider(PassphraseKeyGenerator(cfg.AuthHmacPassphrase, cfg.AuthSalt))),
        RedirectUrl = "~/user/logon",
        UserMapper  = container.Resolve<IUserMapper>())
    FormsAuthentication.Enable (pipelines, auth)
    // CSRF
    Csrf.Enable pipelines
    // Sessions
    let sessions = RethinkDBSessionConfiguration(cfg.DataConfig.Conn)
    sessions.Database <- cfg.DataConfig.Database
    //let sessions = RelationalSessionConfiguration(ConfigurationManager.ConnectionStrings.["SessionStore"].ConnectionString)
    PersistableSessions.Enable (pipelines, sessions)
    ()

  override this.Configure (environment) =
    base.Configure environment
    environment.Tracing(true, true)


let version = 
  let v = typeof<AppConfig>.GetType().GetTypeInfo().Assembly.GetName().Version
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
        match tryFindWebLogByUrlBase (data.Force ()) ctx.Request.Url.HostName with
        | Some webLog -> ctx.Items.[Keys.WebLog] <- webLog
        | None -> // TODO: redirect to domain set up page
                  Exception (sprintf "%s %s" ctx.Request.Url.HostName (Strings.get "ErrNotConfigured"))
                  |> raise
        ctx.Items.[Keys.Version] <- version
        null
      pipelines.BeforeRequest.AddItemToStartOfPipeline establishEnv

      
type Startup() =
  member this.Configure (app : IApplicationBuilder) =
    let opt = NancyOptions()
    opt.Bootstrapper <- new MyWebLogBootstrapper()
    app.UseOwin(fun x -> x.UseNancy(opt) |> ignore) |> ignore


let Run () =
  use host = 
    WebHostBuilder()
      .UseContentRoot(System.IO.Directory.GetCurrentDirectory())
      .UseKestrel()
      .UseStartup<Startup>()
      .Build()
  host.Run()
