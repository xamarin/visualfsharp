namespace Microsoft.CodeAnalysis.ExternalAccess.FSharp.Internal
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

open System.ComponentModel.Composition
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Editor
open Microsoft.VisualStudio.Utilities

type FSharpContentTypeDefinitions() =
    let _x = 1
    [<Export>]
    [<Name(FSharpContentTypeNames.FSharpContentType)>]
    [<BaseDefinition(FSharpContentTypeNames.RoslynContentType)>]
    member val FSharpContentTypeDefinition: ContentTypeDefinition = null with get, set

    [<Export>]
    [<Name(FSharpContentTypeNames.FSharpSignatureHelpContentType)>]
    [<BaseDefinition("sighelp")>]
    member val FSharpSignatureHelpContentTypeDefinition: ContentTypeDefinition = null with get, set
