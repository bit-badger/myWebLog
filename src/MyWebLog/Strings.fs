module MyWebLog.Strings

open System.Collections.Generic
open System.Globalization
open System.IO
open System.Reflection
open System.Text.Json

/// The locales we'll try to load
let private supportedLocales = [ "en-US" ]

/// The fallback locale, if a key is not found in a non-default locale
let private fallbackLocale = "en-US"

/// Get an embedded JSON file as a string
let private getEmbedded locale =
  let str = sprintf "MyWebLog.Resources.%s.json" locale |> Assembly.GetExecutingAssembly().GetManifestResourceStream 
  use rdr = new StreamReader (str)
  rdr.ReadToEnd()

/// The dictionary of localized strings
let private strings =
  supportedLocales
  |> List.map (fun loc -> loc, getEmbedded loc |> JsonSerializer.Deserialize<Dictionary<string, string>>)
  |> dict

/// Get a key from the resources file for the given locale
let getForLocale locale key =
  let getString thisLocale = 
    match strings.ContainsKey thisLocale && strings.[thisLocale].ContainsKey key with
    | true -> Some strings.[thisLocale].[key]
    | false -> None
  match getString locale with
  | Some xlat -> Some xlat
  | None when locale <> fallbackLocale -> getString fallbackLocale
  | None -> None
  |> function Some xlat -> xlat | None -> sprintf "%s.%s" locale key

/// Translate the key for the current locale
let get key = getForLocale CultureInfo.CurrentCulture.Name key
