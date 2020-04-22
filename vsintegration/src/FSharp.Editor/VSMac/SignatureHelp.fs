// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.FSharp.Editor

open System
open System.Composition
open System.Collections.Generic

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.SignatureHelp
open Microsoft.CodeAnalysis.Text
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.SignatureHelp

open Microsoft.VisualStudio.Text
////open Microsoft.VisualStudio.Shell
//open Microsoft.VisualStudio.Shell.Interop

open FSharp.Compiler.Layout
open FSharp.Compiler.Range
open FSharp.Compiler.SourceCodeServices
open MonoDevelop.FSharp

[<Shared>]
[<Export(typeof<IFSharpInteractiveSignatureHelpProvider>)>]
type internal FSharpInteractiveSignatureHelpProvider 
    [<ImportingConstructor>]
    (
        //serviceProvider: SVsServiceProvider,
        checkerProvider: FSharpCheckerProvider,
        projectInfoManager: FSharpProjectOptionsManager
    ) =

    static let userOpName = "SignatureHelpProvider"
    let documentationBuilder = XmlDocumentation.CreateDocumentationBuilder((*serviceProvider.XMLMemberIndexService*))

    static let oneColAfter (lp: LinePosition) = LinePosition(lp.Line,lp.Character+1)
    static let oneColBefore (lp: LinePosition) = LinePosition(lp.Line,max 0 (lp.Character-1))

    // Unit-testable core routine
    static member internal ProvideMethodsAsyncAux(nwpl:FSharpNoteworthyParamInfoLocations, methodGroup:FSharpMethodGroup, documentationBuilder: IDocumentationBuilder, sourceText: SourceText, caretPosition: int, triggerIsTypedChar: char option) = async {
        ////let (interactiveSession: InteractiveSession) = downcast textView.Properties.[typeof<InteractiveSession>]

        //let! parseResults, checkFileAnswer = checker.ParseAndCheckFileInProject(filePath, textVersionHash, sourceText.ToFSharpSourceText(), options, userOpName = userOpName)
        //match checkFileAnswer with
        //| FSharpCheckFileAnswer.Aborted -> return None
        //| FSharpCheckFileAnswer.Succeeded(checkFileResults) -> 

        let textLines = sourceText.Lines
        let caretLinePos = textLines.GetLinePosition(caretPosition)
        let caretLineColumn = caretLinePos.Character

        //// Get the parameter locations
        //let paramLocations = parseResults.FindNoteworthyParamInfoLocations(Pos.fromZ 0 caretLineColumn)

        //match paramLocations with
        //| None -> return None // no locations = no help
        //| Some nwpl -> 
        //let names = nwpl.LongId
        //let lidEnd = nwpl.LongIdEndLocation

        // Get the methods
        //let! methodGroup = checkFileResults.GetMethods(lidEnd.Line, lidEnd.Column, "", Some names)

        let methods = methodGroup.Methods

        if (methods.Length = 0 || methodGroup.MethodName.EndsWith("> )")) then return None else                    

        let isStaticArgTip =
            let parenLine, parenCol = Pos.toZ nwpl.OpenParenLocation 
            assert (parenLine < textLines.Count)
            let parenLineText = textLines.[parenLine].ToString()
            parenCol < parenLineText.Length && parenLineText.[parenCol] = '<'

        let filteredMethods =
            [| for m in methods do 
                  if (isStaticArgTip && m.StaticParameters.Length > 0) ||
                      (not isStaticArgTip && m.HasParameters) then   // need to distinguish TP<...>(...)  angle brackets tip from parens tip
                      yield m |]

        if filteredMethods.Length = 0 then return None else

        let posToLinePosition pos = 
            let (l,c) = Pos.toZ  pos
            // FSROSLYNTODO: FCS gives back line counts that are too large. Really, this shouldn't happen
            let result =LinePosition(l,c)
            let lastPosInDocument = textLines.GetLinePosition(textLines.[textLines.Count-1].End)
            if lastPosInDocument.CompareTo(result) > 0 then result else lastPosInDocument

        // Compute the start position
        let startPos = nwpl.LongIdStartLocation |> posToLinePosition

        // Compute the end position
        let endPos = 
            let last = nwpl.TupleEndLocations.[nwpl.TupleEndLocations.Length-1] |> posToLinePosition
            (if nwpl.IsThereACloseParen then oneColBefore last else last)  

        assert (startPos.CompareTo(endPos) <= 0)

        // Compute the applicable span between the parentheses
        let applicableSpan = 
            textLines.GetTextSpan(LinePositionSpan(startPos, endPos))

        let startOfArgs = nwpl.OpenParenLocation |> posToLinePosition |> oneColAfter 

        let tupleEnds = 
            [| yield startOfArgs
               for i in 0..nwpl.TupleEndLocations.Length-2 do
                   yield nwpl.TupleEndLocations.[i] |> posToLinePosition
               yield endPos  |]

        // If we are pressing "(" or "<" or ",", then only pop up the info if this is one of the actual, real detected positions in the detected promptable call
        //
        // For example the last "(" in 
        //    List.map (fun a -> (
        // should not result in a prompt.
        //
        // Likewise the last "," in 
        //    Console.WriteLine( [(1, 
        // should not result in a prompt, whereas this one will:
        //    Console.WriteLine( [(1,2)],

        //match triggerIsTypedChar with 
        //| Some ('<' | '(' | ',') when not (tupleEnds |> Array.exists (fun lp -> lp.Character = caretLineColumn))  -> 
        //    return None // comma or paren at wrong location = remove help display
        //| _ -> 

        // Compute the argument index by working out where the caret is between the various commas.
        let argumentIndex = 
            let computedTextSpans =
                tupleEnds 
                |> Array.pairwise 
                |> Array.map (fun (lp1, lp2) -> textLines.GetTextSpan(LinePositionSpan(lp1, lp2)))
                
            match (computedTextSpans|> Array.tryFindIndex (fun t -> t.Contains(caretPosition))) with 
            | None -> 
                // Because 'TextSpan.Contains' only succeeds if 'TextSpan.Start <= caretPosition < TextSpan.End' is true,
                // we need to check if the caret is at the very last position in the TextSpan.
                //
                // We default to 0, which is the first argument, if the caret position was nowhere to be found.
                if computedTextSpans.[computedTextSpans.Length-1].End = caretPosition then
                    computedTextSpans.Length-1 
                else 0
            | Some n -> n
         
        // Compute the overall argument count
        let argumentCount = 
            match nwpl.TupleEndLocations.Length with 
            | 1 when caretLinePos.Character = startOfArgs.Character -> 0  // count "WriteLine(" as zero arguments
            | n -> n

        // Compute the current argument name, if any
        let argumentName = 
            if argumentIndex < nwpl.NamedParamNames.Length then 
                nwpl.NamedParamNames.[argumentIndex] 
            else 
                None  // not a named argument

        // Prepare the results
        let results = ResizeArray()

        for method in methods do
            // Create the documentation. Note, do this on the background thread, since doing it in the documentationBuild fails to build the XML index
            let mainDescription = ResizeArray()
            let documentation = ResizeArray()
            XmlDocumentation.BuildMethodOverloadTipText(documentationBuilder, RoslynHelpers.CollectTaggedText mainDescription, RoslynHelpers.CollectTaggedText documentation, method.StructuredDescription, false)

            let parameters = 
                let parameters = if isStaticArgTip then method.StaticParameters else method.Parameters
                [| for p in parameters do 
                      let doc = List()
                      // FSROSLYNTODO: compute the proper help text for parameters, c.f. AppendParameter in XmlDocumentation.fs
                      XmlDocumentation.BuildMethodParamText(documentationBuilder, RoslynHelpers.CollectTaggedText doc, method.XmlDoc, p.ParameterName) 
                      let parts = List()
                      renderL (taggedTextListR (RoslynHelpers.CollectTaggedText parts)) p.StructuredDisplay |> ignore
                      yield (p.ParameterName, p.IsOptional, p.CanonicalTypeTextForSorting, doc, parts) 
                |]

            let prefixParts = 
                [| TaggedText(TextTags.Method, methodGroup.MethodName);  
                   TaggedText(TextTags.Punctuation, (if isStaticArgTip then "<" else "(")) |]
            let separatorParts = [| TaggedText(TextTags.Punctuation, ","); TaggedText(TextTags.Space, " ") |]
            let suffixParts = [| TaggedText(TextTags.Punctuation, (if isStaticArgTip then ">" else ")")) |]

            let completionItem = (method.HasParamArrayArg, documentation, prefixParts, separatorParts, suffixParts, parameters, mainDescription)
            // FSROSLYNTODO: Do we need a cache like for completion?
            //declarationItemsCache.Remove(completionItem.DisplayText) |> ignore // clear out stale entries if they exist
            //declarationItemsCache.Add(completionItem.DisplayText, declarationItem)
            results.Add(completionItem)


        let items = (results.ToArray(),applicableSpan,argumentIndex,argumentCount,argumentName)
        return Some items
    }

    interface IFSharpInteractiveSignatureHelpProvider with
        //member this.IsTriggerCharacter(c) = c ='(' || c = '<' || c = ',' 
        //member this.IsRetriggerCharacter(c) = c = ')' || c = '>' || c = '='

        member this.GetItemsAsync(document, position, triggerInfo, cancellationToken) = 
            asyncMaybe {
              try
                let! fsi = FSharpInteractivePad.Fsi
                let! controller = fsi.Controller
                //let (interactiveSession: InteractiveSession) = downcast controller.View.Properties.[typeof<InteractiveSession>]

                //if FSharpInteractivePad.Fsi.Value.Controller.Value.IsInputLine(line) then

                //let! parsingOptions, projectOptions = projectInfoManager.TryGetOptionsForEditingDocumentOrProject(document, cancellationToken)

                let! sourceText = document.GetTextAsync(cancellationToken)
                let line = sourceText.Lines.GetLineFromPosition(position)
                let column = position - line.Start
                let snapshot = sourceText.FindCorrespondingEditorTextSnapshot()
                let lineText = snapshot.GetText(Span(line.Start, line.End - line.Start))
                //let fssourceText = SourceText.From(lineText).ToFSharpSourceText()
                MonoDevelop.Core.LoggingService.LogDebug("parameter-hints " + column.ToString() + " " + lineText)
                controller.Session.SendParameterHintRequest lineText column

                let! paramInfo, methodGroups = controller.Session.ParameterHintReceived |> Async.AwaitEvent
                //let! textVersion = document.GetTextVersionAsync(cancellationToken)
                ////let! parseResult, parsedInput, checkResults = checkerProvider.Checker.ParseAndCheckDocument(document, projectOptions, lineText)
                //let! projectOptions, errors = checkerProvider.Checker.GetProjectOptionsFromScript(document.FilePath, fssourceText) |> liftAsync
                //let parsingOptions, errors = checkerProvider.Checker.GetParsingOptionsFromProjectOptions(projectOptions)
                //let! parseResult = checkerProvider.Checker.ParseFileNoCache(document.FilePath, fssourceText, parsingOptions) |> liftAsync
                //let ino = parseResult.FindNoteworthyParamInfoLocations(Pos.fromZ 0 column)
                //printfn "%A" ino
                let triggerTypedChar = 
                    if triggerInfo.TriggerCharacter.HasValue && triggerInfo.TriggerReason = FSharpSignatureHelpTriggerReason.TypeCharCommand then
                        Some triggerInfo.TriggerCharacter.Value
                    else None

                let! (results,applicableSpan,argumentIndex,argumentCount,argumentName) = 
                    FSharpInteractiveSignatureHelpProvider.ProvideMethodsAsyncAux(paramInfo, methodGroups, documentationBuilder, sourceText, column, triggerTypedChar)
                let items = 
                    results 
                    |> Array.map (fun (hasParamArrayArg, doc, prefixParts, separatorParts, suffixParts, parameters, descriptionParts) ->
                            let parameters = parameters
                                                |> Array.map (fun (paramName, isOptional, _typeText, paramDoc, displayParts) -> 
                                                FSharpSignatureHelpParameter(paramName,isOptional,documentationFactory=(fun _ -> paramDoc :> seq<_>),displayParts=displayParts))
                            FSharpSignatureHelpItem(isVariadic=hasParamArrayArg, documentationFactory=(fun _ -> doc :> seq<_>),prefixParts=prefixParts,separatorParts=separatorParts,suffixParts=suffixParts,parameters=parameters,descriptionParts=descriptionParts))

                // The text span that comes back from FCS always has line number 1. We need to map this back to the
                // actual line number in the editor
                let offset = position - column
                let applicableAdjustedSpan = 
                    new TextSpan(applicableSpan.Start + offset, applicableSpan.End - applicableSpan.Start - 1)
                return FSharpSignatureHelpItems(items,applicableAdjustedSpan,argumentIndex,argumentCount,Option.toObj argumentName)
              with ex -> 
                Assert.Exception(ex)
                return! None
            } 
            |> Async.map Option.toObj
            |> RoslynHelpers.StartAsyncAsTask cancellationToken
