// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.FSharp.Editor.GRRRRR

open System.Composition
open Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion
open Microsoft.VisualStudio.Utilities
open Microsoft.VisualStudio.FSharp.Editor
open Microsoft.VisualStudio.Text.Editor

[<Export(typeof<IAsyncCompletionCommitManagerProvider>)>]
[<Name("FSharpAsyncCompletionCommitManagerProvider")>]
[<ContentType("code++.F#")>]
//[<TextViewRole(PredefinedTextViewRoles.Editable)>]
//[<Order>]
type FSharpAsyncCompletionCommitManagerProvider() =
    //[<ImportingConstructor>]
    //() =
    let x = 1
    interface IAsyncCompletionCommitManagerProvider with
        member __.GetOrCreate(textView) =
            System.Diagnostics.Trace.WriteLine("GetOrCreate FSharpAsyncCompletionCommitManager")
            FSharpAsyncCompletionCommitManager() :> _