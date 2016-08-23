namespace MyWebLog.Resources.AssemblyInfo

open System.Resources
open System.Reflection
open System.Runtime.InteropServices

[<assembly: AssemblyTitle("MyWebLog.Resources")>]
[<assembly: AssemblyDescription("Resources for the myWebLog package")>]
[<assembly: AssemblyConfiguration("")>]
[<assembly: AssemblyCompany("")>]
[<assembly: AssemblyProduct("MyWebLog.Resources")>]
[<assembly: AssemblyCopyright("Copyright ©  2016")>]
[<assembly: AssemblyTrademark("")>]
[<assembly: AssemblyCulture("")>]
[<assembly: ComVisible(false)>]
[<assembly: Guid("a12ea8da-88bc-4447-90cb-a0e2dcc37523")>]
[<assembly: AssemblyVersion("0.9.2.0")>]
[<assembly: AssemblyFileVersion("1.0.0.0")>]
[<assembly: NeutralResourcesLanguage("en-US")>]

do
  ()

type HorribleHack() =
  member this.Assembly = this.GetType().GetTypeInfo().Assembly
