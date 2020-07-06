//
// FSharpUnitTestSupport.fs
//
// Author:
//       Microsoft
//
// Copyright (c) 2020 Microsoft
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

namespace MonoDevelop.FSharp

open System
open System.Composition
open System.Collections.Generic
open System.Diagnostics
open System.Threading

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Classification
open Microsoft.CodeAnalysis.Editor
open Microsoft.CodeAnalysis.Host.Mef
open Microsoft.CodeAnalysis.Text
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Classification
open MonoDevelop.UnitTesting
open Microsoft.VisualStudio.FSharp.Editor
open Microsoft.VisualStudio.Text.Classification
open Microsoft.VisualStudio.Text.Tagging

open MonoDevelop.Core
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Editor

// https://github.com/xamarin/vsmac/blob/dev/kirillo/fsharp/main/external/fsharpbinding/MonoDevelop.FSharpBinding/FSharpUnitTestTextEditorExtension.fs

type FSharpUnitTestTagger(textView, checkerProvider, projectInfoManager) =
    let tagsChanged = Event<_,_>()

    interface ITagger<IUnitTestTag> with
        [<CLIEvent>]
        member this.TagsChanged = tagsChanged.Publish

        member __.GetTags(collection: NormalizedSnapshotSpanCollection) =
            seq
            {
                for span in collection do
                    let snapshot = span.Snapshot
                    let document = snapshot.GetOpenDocumentInCurrentContextWithChanges()
                    let sourceText = document.GetTextAsync(CancellationToken.None)
                    let _, _, projectOptions = projectInfoManager.TryGetOptionsForDocumentOrProject(document, CancellationToken.None)
                    let! _, _, checkResults = checkerProvider.Checker.ParseAndCheckDocument(document, projectOptions, sourceText = sourceText, allowStaleResults = false, userOpName=userOpName)
            }

        

type FSharpUnitTestTag (ids: seq<string>) =
    interface IUnitTestTag with
        member __.TestIds = ids

[<Export(typeof<IViewTaggerProvider>)>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpConstants.FSharpContentTypeName)>]
[<TagType(typeof<FSharpUnitTestTag>)>]
[<TextViewRole(PredefinedTextViewRoles.Analyzable)>]
type internal FSharpUnitTestTaggerProvider 
    [<ImportingConstructor>] 
    (
        checkerProvider: FSharpCheckerProvider,
        projectInfoManager: FSharpProjectOptionsManager
    ) =
    interface IViewTaggerProvider with
        member __.CreateTagger(textView: ITextView, buffer: ITextBuffer) = box(FSharpUnitTestTagger(textView, checkerProvider, projectInfoManager)) :?> _
