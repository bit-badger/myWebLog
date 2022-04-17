[<RequireQualifiedAccess>]
module MyWebLog.Handlers

open Giraffe
open MyWebLog
open MyWebLog.ViewModels
open System

[<AutoOpen>]
module private Helpers =
    
    open DotLiquid
    open System.Collections.Concurrent
    open System.IO
    
    /// Cache for parsed templates
    let private themeViews = ConcurrentDictionary<string, Template> ()
    
    /// Return a view for a theme
    let themedView<'T> (template : string) (model : obj) : HttpHandler = fun next ctx -> task {
        let webLog       = WebLogCache.getByCtx ctx
        let templatePath = $"themes/{webLog.themePath}/{template}"
        match themeViews.ContainsKey templatePath with
        | true -> ()
        | false ->
            let! file = File.ReadAllTextAsync $"{templatePath}.liquid"
            themeViews[templatePath] <- Template.Parse file
        let view = themeViews[templatePath].Render (Hash.FromAnonymousObject model)
        return! htmlString view next ctx
    }

module User =
    
    open System.Security.Cryptography
    open System.Text
    
    /// Hash a password for a given user
    let hashedPassword (plainText : string) (email : string) (salt : Guid) =
        let allSalt = Array.concat [ salt.ToByteArray(); (Encoding.UTF8.GetBytes email) ] 
        use alg = new Rfc2898DeriveBytes (plainText, allSalt, 2_048)
        Convert.ToBase64String(alg.GetBytes(64))


module CatchAll =
    
    let catchAll : HttpHandler = fun next ctx -> task {
        let testPage = { Page.empty with text = "Howdy, folks!" }
        return! themedView "single-page" { page = testPage; webLog = WebLogCache.getByCtx ctx } next ctx
    }

open Giraffe.EndpointRouting

/// The endpoints defined in the above handlers
let endpoints = [
    GET [
        route "" CatchAll.catchAll
    ]
]
    