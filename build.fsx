#r "paket:
nuget Fake.DotNet.Cli
nuget Fake.IO.FileSystem
nuget Fake.IO.Zip
nuget Fake.Core.Target //"
#load ".fake/build.fsx/intellisense.fsx"
open System.IO
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

Target.initEnvironment ()

/// The output directory for release ZIPs
let releasePath = "releases"

/// The path to the main project
let projectPath = "src/MyWebLog"

/// The path and name of the main project
let projName = $"{projectPath}/MyWebLog.fsproj"

/// The version being packaged (extracted from appsettings.json)
let version =
    let settings   = File.ReadAllText $"{projectPath}/appsettings.json"
    let generator  = settings.Substring (settings.IndexOf "\"Generator\":")
    let appVersion = generator.Replace("\"Generator\": \"", "")
    let appVersion = appVersion.Substring (0, appVersion.IndexOf "\"")
    appVersion.Split ' ' |> Array.last
    
/// Zip a theme distributed with myWebLog
let zipTheme (name : string) (_ : TargetParameter) =
    let path = $"src/{name}-theme"
    Trace.log $"Path = {path}"
    !! $"{path}/**/*"
    |> Zip.filesAsSpecs path //$"src/{name}-theme"
    |> Seq.filter (fun (_, name) -> not (name.EndsWith ".zip"))
    |> Zip.zipSpec $"{releasePath}/{name}.zip"

/// Publish the project for the given runtime ID    
let publishFor rid (_ : TargetParameter) =
    DotNet.publish (fun opts -> { opts with Runtime = Some rid; SelfContained = Some false; NoLogo = true }) projName

/// Package published output for the given runtime ID
let packageFor (rid : string) (_ : TargetParameter) =
    let path = $"{projectPath}/bin/Release/net6.0/{rid}/publish"
    [ !! $"{path}/**/*"
        |> Zip.filesAsSpecs path
        |> Zip.moveToFolder "app"
      Seq.singleton ($"{releasePath}/admin.zip", "admin.zip")
      Seq.singleton ($"{releasePath}/default.zip", "default.zip")
    ]
    |> Seq.concat
    |> Zip.zipSpec $"{releasePath}/myWebLog-{version}.{rid}.zip"


Target.create "Clean" (fun _ ->
    !! "src/**/bin"
    ++ "src/**/obj"
    |> Shell.cleanDirs 
    Shell.cleanDir releasePath
)

Target.create "Build" (fun _ ->
    DotNet.build (fun opts -> { opts with NoLogo = true }) projName
)

Target.create "ZipAdminTheme"   (zipTheme "admin")
Target.create "ZipDefaultTheme" (zipTheme "default")

Target.create "PublishWindows" (publishFor "win-x64")
Target.create "PackageWindows" (packageFor "win-x64")

Target.create "PublishLinux" (publishFor "linux-x64")
Target.create "PackageLinux" (packageFor "linux-x64")

Target.create "RepackageLinux" (fun _ ->
    let workDir = $"{releasePath}/linux"
    let zipArchive = $"{releasePath}/myWebLog-{version}.linux-x64.zip"
    Shell.mkdir workDir
    Zip.unzip workDir zipArchive
    Shell.cd workDir
    [ "cfj"; $"../myWebLog-{version}.linux-x64.tar.bz2"; "." ]
    |> CreateProcess.fromRawCommand "tar"
    |> CreateProcess.redirectOutput
    |> Proc.run
    |> ignore
    Shell.cd "../.."
    Shell.rm zipArchive
    Shell.rm_rf workDir
)

Target.create "All" ignore

"Clean"
  ==> "All"

"Clean"
  ?=> "Build"
  ==> "All"

"Clean"
  ?=> "ZipDefaultTheme"
  ==> "All"

"Clean"
  ?=> "ZipAdminTheme"
  ==> "All"

"Build"
  ==> "PublishWindows"
  ==> "All"

"Build"
  ==> "PublishLinux"
  ==> "All"

"PublishWindows"
  ==> "PackageWindows"
  ==> "All"

"PublishLinux"
  ==> "PackageLinux"
  ==> "All"

"PackageLinux"
  ==> "RepackageLinux"
  ==> "All"

Target.runOrDefault "All"
