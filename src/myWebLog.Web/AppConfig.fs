namespace MyWebLog

open MyWebLog.Data
open Newtonsoft.Json
open System.Text

/// Configuration for this myWebLog instance
type AppConfig =
  { /// The text from which to derive salt to use for passwords
    [<JsonProperty("password-salt")>]
    PasswordSaltString : string
    /// The text from which to derive salt to use for forms authentication
    [<JsonProperty("auth-salt")>]
    AuthSaltString : string
    /// The encryption passphrase to use for forms authentication
    [<JsonProperty("encryption-passphrase")>]
    AuthEncryptionPassphrase : string
    /// The HMAC passphrase to use for forms authentication
    [<JsonProperty("hmac-passphrase")>]
    AuthHmacPassphrase : string
    /// The data configuration
    [<JsonProperty("data")>]
    DataConfig : DataConfig }
 with
  /// The salt to use for passwords
  member this.PasswordSalt = Encoding.UTF8.GetBytes this.PasswordSaltString
  /// The salt to use for forms authentication
  member this.AuthSalt = Encoding.UTF8.GetBytes this.AuthSaltString

  /// Deserialize the configuration from the JSON file
  static member FromJson json =
    let cfg = JsonConvert.DeserializeObject<AppConfig> json
    { cfg with DataConfig = DataConfig.Connect cfg.DataConfig }