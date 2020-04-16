//
// InteractiveClassificationService.fs
//
// Author:
//       jasonimison <jaimison@microsoft.com>
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
namespace Microsoft.VisualStudio.FSharp.Editor

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
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Classification
open Microsoft.VisualStudio.Text.Classification
open System.Windows.Media
open MonoDevelop.Core
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Editor

//[<ExportLanguageService(typeof<IFSharpInteracClassificationService>, FSharpContentTypeNames.FSharpInteractiveContentType)>]
//[<Export>]
[<Export(typeof<IFSharpInteractiveClassificationService>)>]
type internal FSharpInteractiveClassificationService
    [<ImportingConstructor>]
    (
        service: IFSharpClassificationService
    ) =
    interface IFSharpInteractiveClassificationService with
       
        member __.AddLexicalClassifications(sourceText: SourceText, textSpan: TextSpan, result: List<ClassifiedSpan>, cancellationToken: CancellationToken) =
            //let line = sourceText.Lines.GetLineFromPosition(textSpan.Start).LineNumber

            //if FSharpInteractivePad.Fsi.Value.Controller.Value.IsInputLine(line) then
            //service.AddLexicalClassifications(sourceText, textSpan, result, cancellationToken)
            ()

        member __.AddSyntacticClassificationsAsync(document: Document, textSpan: TextSpan, result: List<ClassifiedSpan>, cancellationToken: CancellationToken) =
            //Tasks.Task.CompletedTask
            match document.TryGetText() with
            | true, sourceText ->
                let line = sourceText.Lines.GetLineFromPosition(textSpan.Start).LineNumber

                if FSharpInteractivePad.Fsi.Value.Controller.Value.IsInputLine(line) then
                    //let service = document.Project.LanguageServices.GetService<FSharpClassificationService>();

                    service.AddSyntacticClassificationsAsync(document, textSpan, result, cancellationToken)
                else
                    Tasks.Task.CompletedTask
            | false, _ -> Tasks.Task.CompletedTask

        //member __.AddSemanticClassificationsAsync(document: Document, textSpan: TextSpan, result: List<ClassifiedSpan>, cancellationToken: CancellationToken) =
        //    System.Threading.Tasks.Task.CompletedTask

        // Do not perform classification if we don't have project options (#defines matter)
        member __.AdjustStaleClassification(_: SourceText, classifiedSpan: ClassifiedSpan) : ClassifiedSpan = classifiedSpan

