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
open System.Linq
open System.Threading

open FSharp.Compiler.SourceCodeServices

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Classification
open Microsoft.CodeAnalysis.Editor
open Microsoft.CodeAnalysis.Host.Mef
open Microsoft.CodeAnalysis.Text
open Microsoft.CodeAnalysis.Text.Shared.Extensions
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Classification
open MonoDevelop.UnitTesting
open Microsoft.VisualStudio.FSharp.Editor
open Microsoft.VisualStudio.Text.Classification
open Microsoft.VisualStudio.Text.Tagging

open Mono.Addins
open MonoDevelop.Core
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Editor

// https://github.com/xamarin/vsmac/blob/dev/kirillo/fsharp/main/external/fsharpbinding/MonoDevelop.FSharpBinding/FSharpUnitTestTextEditorExtension.fs
type UnitTestLocation(offset: int) =
    member val UnitTestIdentifier = "" with get, set
    member val IsIgnored = false with get, set
    member val IsFixture  = false with get, set

module Logic =
    let hasAttributeNamed (att:FSharpAttribute) (unitTestMarkers: IUnitTestMarkers[]) (filter:  string -> IUnitTestMarkers -> bool) =
        let attributeName = att.AttributeType.TryFullName
        match attributeName with
        | Some name ->
            unitTestMarkers
            |> Seq.exists (filter name)
        | None -> false

    let gatherUnitTests (snapshot: ITextSnapshot, unitTestMarkers: IUnitTestMarkers[], allSymbols:FSharpSymbolUse []) =
        let hasAttribute a = hasAttributeNamed a unitTestMarkers
        let tests = ResizeArray<UnitTestLocation>()

        let testSymbols =
            allSymbols
            |> Array.filter
                (fun s -> match s.Symbol with
                          | :? FSharpMemberOrFunctionOrValue as fom -> 
                              fom.Attributes
                              |> Seq.exists (fun a -> hasAttribute a (fun attributeName m -> m.TestMethodAttributeMarker = attributeName || m.TestCaseMethodAttributeMarker = attributeName) )
                          | :? FSharpEntity as fse -> 
                                  fse.MembersFunctionsAndValues
                                  |> Seq.exists (fun fom -> fom.Attributes
                                                            |> Seq.exists (fun a -> hasAttribute a (fun attributeName m -> m.TestMethodAttributeMarker = attributeName || m.TestCaseMethodAttributeMarker = attributeName) ))
                          | _ -> false )
            |> Seq.distinctBy (fun su -> su.RangeAlternate)
            |> Seq.choose
                (fun symbolUse -> 
                    let range = symbolUse.RangeAlternate

                    let startOffset = RoslynHelpers. snapshot.GetPosition (range.StartLine, range.StartColumn+1)
                    let test = UnitTestLocation(startOffset)
                    match symbolUse.Symbol with
                    | :? FSharpMemberOrFunctionOrValue as func -> 
                        let typeName =
                            match func.DeclaringEntity with
                            | Some ent -> ent.QualifiedName
                            | None _ ->
                                MonoDevelop.Core.LoggingService.LogWarning(sprintf "F# GatherUnitTests: found a unit test method with no qualified name: %s" func.FullName)
                                func.CompiledName
                        let methName = func.CompiledName
                        let isIgnored =
                            func.Attributes
                            |> Seq.exists (fun a -> hasAttribute a (fun attributeName m -> m.IgnoreTestMethodAttributeMarker = attributeName))
                        //add test cases
                        let testCases =
                            func.Attributes
                            |> Seq.filter (fun a -> hasAttribute a (fun attributeName m -> m.TestCaseMethodAttributeMarker = attributeName))
                        testCases
                        |> Seq.map createTestCase
                        |> test.TestCases.AddRange
                        test.UnitTestIdentifier <- typeName + "." + methName
                        test.IsIgnored <- isIgnored
                        Some test
                    | :? FSharpEntity as entity ->
                        let typeName = entity.QualifiedName
                        let isIgnored =
                            entity.Attributes
                            |> Seq.exists (fun a -> hasAttribute a (fun attributeName m -> m.IgnoreTestMethodAttributeMarker = attributeName))
                        test.UnitTestIdentifier <- typeName
                        test.IsIgnored <- isIgnored
                        test.IsFixture <- true
                        Some test
                    | _ -> None)
            |> Some
        testSymbols
        |> Option.iter tests.AddRange 
        tests

type FSharpUnitTestTag (ids: seq<string>) =
    interface IUnitTestTag with
        member __.TestIds = ids

type FSharpUnitTestTagger(textView, checkerProvider: FSharpCheckerProvider, projectInfoManager: FSharpProjectOptionsManager) =
    let tagsChanged = Event<_,_>()

    interface ITagger<IUnitTestTag> with
        [<CLIEvent>]
        member this.TagsChanged = tagsChanged.Publish

        member __.GetTags(collection: NormalizedSnapshotSpanCollection) =
            let temp =
                asyncMaybe {
                    let snapshot = collection.[0].Snapshot
                    let document = snapshot.GetOpenDocumentInCurrentContextWithChanges()
                    let! sourceText = document.GetTextAsync(CancellationToken.None)
                    let! _, _, projectOptions = projectInfoManager.TryGetOptionsForDocumentOrProject(document, CancellationToken.None)

                    let! _, _, checkResults = checkerProvider.Checker.ParseAndCheckDocument(document, projectOptions, sourceText = sourceText, allowStaleResults = false, userOpName=userOpName)
                    let! symbols = checkResults.GetAllUsesOfAllSymbolsInFile() |> liftAsync

                    let unitTestMarkers = AddinManager.GetExtensionNodes("/MonoDevelop/UnitTesting/UnitTestMarkers").OfType<IUnitTestMarkers>().ToArray()

                    let tests = Logic.gatherUnitTests (snapshot, unitTestMarkers, symbols)

                    for test in tests do
                        test

                        let textSpan = RoslynHelpers.FSharpRangeToTextSpan (sourceText, range)
                        let snapshotSpan = SnapshotSpan(snapshot, textSpan.Start, textSpan.Length)
                        let tag = FSharpUnitTestTag(id)
                        let tagSpan = TagSpan<IUnitTestTag> (snapshotSpan, tag) 

                    return 1
                }
            temp |> ignore
            Seq.empty<_>

        



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
