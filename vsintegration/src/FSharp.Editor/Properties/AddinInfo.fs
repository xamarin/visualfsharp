namespace Microsoft.VisualStudio.FSharp.Editor
open Mono.Addins

[<Addin ("VisualFSharp", 
  Namespace = "Microsoft.VisualStudio.FSharp.Editor",
  Version = MonoDevelop.BuildInfo.Version,
  Url = "http://github.com/mono/fsharp",
  Category = "Language bindings")>]

[<AddinName ("VisualFSharp")>]
[<AddinDescription ("F# Language Binding (for VSMac " + MonoDevelop.BuildInfo.Version + ").")>]
[<AddinAuthor ("F# Software Foundation (fsharp.org)")>]

//[<AddinDependency ("Core", MonoDevelop.BuildInfo.Version)>]
//[<AddinDependency ("Ide", MonoDevelop.BuildInfo.Version)>]

()