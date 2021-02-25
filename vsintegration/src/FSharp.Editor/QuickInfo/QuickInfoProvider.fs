﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.FSharp.Editor

open System
open System.Threading
open System.Threading.Tasks
open System.ComponentModel.Composition
open System.Text

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Text

open Microsoft.VisualStudio.Language.Intellisense
open Microsoft.VisualStudio.Text

open FSharp.Compiler.Text
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.Range
open FSharp.Compiler

open Internal.Utilities.StructuredFormat
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Completion
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Editor

type internal QuickInfo =
    { StructuredText: FSharpStructuredToolTipText
      Span: TextSpan
      Symbol: FSharpSymbol option
      SymbolKind: LexerSymbolKind }

module internal FSharpQuickInfo =

    let userOpName = "QuickInfo"

    // when a construct has been declared in a signature file the documentation comments that are
    // written in that file are the ones that go into the generated xml when the project is compiled
    // therefore we should include these doccoms in our design time quick info
    let getQuickInfoFromRange
        (
            checker: FSharpChecker,
            projectInfoManager: FSharpProjectOptionsManager,
            document: Document,
            declRange: range,
            cancellationToken: CancellationToken
        )
        : Async<QuickInfo option> =

        asyncMaybe {
            let solution = document.Project.Solution
            // ascertain the location of the target declaration in the signature file
            let! extDocId = solution.GetDocumentIdsWithFilePath declRange.FileName |> Seq.tryHead
            let extDocument = solution.GetProject(extDocId.ProjectId).GetDocument extDocId
            let! extSourceText = extDocument.GetTextAsync cancellationToken
            let! extSpan = RoslynHelpers.TryFSharpRangeToTextSpan (extSourceText, declRange)
            let extLineText = (extSourceText.Lines.GetLineFromPosition extSpan.Start).ToString()

            // project options need to be retrieved because the signature file could be in another project
            let! extParsingOptions, extProjectOptions = projectInfoManager.TryGetOptionsByProject(document.Project, cancellationToken)
            let extDefines = CompilerEnvironment.GetCompilationDefinesForEditing extParsingOptions
            let! extLexerSymbol = Tokenizer.getSymbolAtPosition(extDocId, extSourceText, extSpan.Start, declRange.FileName, extDefines, SymbolLookupKind.Greedy, true, true)
            let! _, _, extCheckFileResults = checker.ParseAndCheckDocument(extDocument, extProjectOptions, allowStaleResults=true, sourceText=extSourceText, userOpName = userOpName)

            let! extQuickInfoText = 
                extCheckFileResults.GetStructuredToolTipText
                    (declRange.StartLine, extLexerSymbol.Ident.idRange.EndColumn, extLineText, extLexerSymbol.FullIsland, FSharpTokenTag.IDENT) |> liftAsync

            match extQuickInfoText with
            | FSharpToolTipText []
            | FSharpToolTipText [FSharpStructuredToolTipElement.None] -> return! None
            | extQuickInfoText  ->
                let! extSymbolUse =
                    extCheckFileResults.GetSymbolUseAtLocation(declRange.StartLine, extLexerSymbol.Ident.idRange.EndColumn, extLineText, extLexerSymbol.FullIsland)
                let! span = RoslynHelpers.TryFSharpRangeToTextSpan (extSourceText, extLexerSymbol.Range)

                return { StructuredText = extQuickInfoText
                         Span = span
                         Symbol = Some extSymbolUse.Symbol
                         SymbolKind = extLexerSymbol.Kind }
        }

    /// Get QuickInfo combined from doccom of Signature and definition
    let getQuickInfo
        (
            checker: FSharpChecker,
            projectInfoManager: FSharpProjectOptionsManager,
            document: Document,
            position: int,
            cancellationToken: CancellationToken
        )
        : Async<(range * QuickInfo option * QuickInfo option) option> =

        asyncMaybe {
            let! sourceText = document.GetTextAsync cancellationToken |> liftTaskAsync
            let! parsingOptions, projectOptions = projectInfoManager.TryGetOptionsForEditingDocumentOrProject(document, cancellationToken, userOpName)
            let defines = CompilerEnvironment.GetCompilationDefinesForEditing parsingOptions
            let! lexerSymbol = Tokenizer.getSymbolAtPosition(document.Id, sourceText, position, document.FilePath, defines, SymbolLookupKind.Greedy, true, true)
            let idRange = lexerSymbol.Ident.idRange  
            let! _, _, checkFileResults = checker.ParseAndCheckDocument(document, projectOptions, allowStaleResults = true, sourceText=sourceText, userOpName = userOpName)
            let textLinePos = sourceText.Lines.GetLinePosition position
            let fcsTextLineNumber = Line.fromZ textLinePos.Line
            let lineText = (sourceText.Lines.GetLineFromPosition position).ToString()

            /// Gets the QuickInfo information for the orignal target
            let getTargetSymbolQuickInfo (symbol, tag) =
                asyncMaybe {
                    let! targetQuickInfo =
                        checkFileResults.GetStructuredToolTipText
                            (fcsTextLineNumber, idRange.EndColumn, lineText, lexerSymbol.FullIsland,tag) |> liftAsync

                    match targetQuickInfo with
                    | FSharpToolTipText []
                    | FSharpToolTipText [FSharpStructuredToolTipElement.None] -> return! None
                    | _ ->
                        let! targetTextSpan = RoslynHelpers.TryFSharpRangeToTextSpan (sourceText, lexerSymbol.Range)
                        return { StructuredText = targetQuickInfo
                                 Span = targetTextSpan
                                 Symbol = symbol
                                 SymbolKind = lexerSymbol.Kind }
                }

            match lexerSymbol.Kind with 
            | LexerSymbolKind.String ->
                let! targetQuickInfo = getTargetSymbolQuickInfo (None, FSharpTokenTag.STRING)
                return lexerSymbol.Range, None, Some targetQuickInfo
            
            | _ -> 
            let! symbolUse = checkFileResults.GetSymbolUseAtLocation (fcsTextLineNumber, idRange.EndColumn, lineText, lexerSymbol.FullIsland)

            // if the target is in a signature file, adjusting the quick info is unnecessary
            if isSignatureFile document.FilePath then
                let! targetQuickInfo = getTargetSymbolQuickInfo (Some symbolUse.Symbol, FSharpTokenTag.IDENT)
                return symbolUse.RangeAlternate, None, Some targetQuickInfo
            else
                // find the declaration location of the target symbol, with a preference for signature files
                let! findSigDeclarationResult = checkFileResults.GetDeclarationLocation (idRange.StartLine, idRange.EndColumn, lineText, lexerSymbol.FullIsland, preferFlag=true) |> liftAsync

                // it is necessary to retrieve the backup quick info because this acquires
                // the textSpan designating where we want the quick info to appear.
                let! targetQuickInfo = getTargetSymbolQuickInfo (Some symbolUse.Symbol, FSharpTokenTag.IDENT)

                let! result =
                    match findSigDeclarationResult with 
                    | FSharpFindDeclResult.DeclFound declRange when isSignatureFile declRange.FileName ->
                        asyncMaybe {
                            let! sigQuickInfo = getQuickInfoFromRange(checker, projectInfoManager, document, declRange, cancellationToken)

                            // if the target was declared in a signature file, and the current file
                            // is not the corresponding module implementation file for that signature,
                            // the doccoms from the signature will overwrite any doccoms that might be
                            // present on the definition/implementation
                            let! findImplDefinitionResult = checkFileResults.GetDeclarationLocation (idRange.StartLine, idRange.EndColumn, lineText, lexerSymbol.FullIsland, preferFlag=false) |> liftAsync

                            match findImplDefinitionResult  with
                            | FSharpFindDeclResult.DeclNotFound _
                            | FSharpFindDeclResult.ExternalDecl _ ->
                                return symbolUse.RangeAlternate, Some sigQuickInfo, None
                            | FSharpFindDeclResult.DeclFound declRange ->
                                let! implQuickInfo = getQuickInfoFromRange(checker, projectInfoManager, document, declRange, cancellationToken)
                                return symbolUse.RangeAlternate, Some sigQuickInfo, Some { implQuickInfo with Span = targetQuickInfo.Span }
                        }
                    | _ -> async.Return None
                    |> liftAsync

                return result |> Option.defaultValue (symbolUse.RangeAlternate, None, Some targetQuickInfo)
        }

type internal FSharpAsyncQuickInfoSource
    (
        statusBar: StatusBar,
        checkerProvider:FSharpCheckerProvider,
        projectInfoManager:FSharpProjectOptionsManager,
        textBuffer:ITextBuffer,
        _settings: EditorOptions
    ) =

    // test helper
    static member ProvideQuickInfo(checker:FSharpChecker, documentId:DocumentId, sourceText:SourceText, filePath:string, position:int, parsingOptions:FSharpParsingOptions, options:FSharpProjectOptions, textVersionHash:int, languageServicePerformanceOptions: LanguageServicePerformanceOptions) =
        asyncMaybe {
            let! _, _, checkFileResults = checker.ParseAndCheckDocument(filePath, textVersionHash, sourceText, options, languageServicePerformanceOptions, userOpName=FSharpQuickInfo.userOpName)
            let textLine = sourceText.Lines.GetLineFromPosition position
            let textLineNumber = textLine.LineNumber + 1 // Roslyn line numbers are zero-based
            let defines = CompilerEnvironment.GetCompilationDefinesForEditing parsingOptions
            let! symbol = Tokenizer.getSymbolAtPosition (documentId, sourceText, position, filePath, defines, SymbolLookupKind.Precise, true, true)
            let! res = checkFileResults.GetStructuredToolTipText (textLineNumber, symbol.Ident.idRange.EndColumn, textLine.ToString(), symbol.FullIsland, FSharpTokenTag.IDENT) |> liftAsync
            match res with
            | FSharpToolTipText []
            | FSharpToolTipText [FSharpStructuredToolTipElement.None] -> return! None
            | _ ->
                let! symbolUse = checkFileResults.GetSymbolUseAtLocation (textLineNumber, symbol.Ident.idRange.EndColumn, textLine.ToString(), symbol.FullIsland)
                let! symbolSpan = RoslynHelpers.TryFSharpRangeToTextSpan (sourceText, symbol.Range)
                return { StructuredText = res
                         Span = symbolSpan
                         Symbol = Some symbolUse.Symbol
                         SymbolKind = symbol.Kind }
        }

    static member BuildSingleQuickInfoItem (documentationBuilder:IDocumentationBuilder) (quickInfo:QuickInfo) =
        let mainDescription, documentation, typeParameterMap, usage, exceptions = ResizeArray(), ResizeArray(), ResizeArray(), ResizeArray(), ResizeArray()
        XmlDocumentation.BuildDataTipText(documentationBuilder, mainDescription.Add, documentation.Add, typeParameterMap.Add, usage.Add, exceptions.Add, quickInfo.StructuredText)
        let docs = RoslynHelpers.joinWithLineBreaks [documentation; typeParameterMap; usage; exceptions]
        (mainDescription, docs)

    interface IAsyncQuickInfoSource with
        override _.Dispose() = () // no cleanup necessary

        // This method can be called from the background thread.
        // Do not call IServiceProvider.GetService here.
        override _.GetQuickInfoItemAsync(session:IAsyncQuickInfoSession, cancellationToken:CancellationToken) : Task<QuickInfoItem> =
            let triggerPoint = session.GetTriggerPoint(textBuffer.CurrentSnapshot)
            match triggerPoint.HasValue with
            | false -> Task.FromResult<QuickInfoItem>(null)
            | true ->
                let triggerPoint = triggerPoint.GetValueOrDefault()
                asyncMaybe {
                    let document = textBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges()
                    let! symbolUseRange, sigQuickInfo, targetQuickInfo = FSharpQuickInfo.getQuickInfo(checkerProvider.Checker, projectInfoManager, document, triggerPoint.Position, cancellationToken)
                    let getTrackingSpan (span:TextSpan) =
                        textBuffer.CurrentSnapshot.CreateTrackingSpan(span.Start, span.Length, SpanTrackingMode.EdgeInclusive)

                    let documentationBuilder = XmlDocumentation.CreateDocumentationBuilder((*xmlMemberIndexService*))
                    match sigQuickInfo, targetQuickInfo with
                    | None, None -> return null
                    | Some quickInfo, None
                    | None, Some quickInfo ->
                        let mainDescription, docs = FSharpAsyncQuickInfoSource.BuildSingleQuickInfoItem documentationBuilder quickInfo
                        let imageId = Tokenizer.GetImageIdForSymbol(quickInfo.Symbol, quickInfo.SymbolKind)
                        let navigation = QuickInfoNavigation(statusBar, checkerProvider.Checker, projectInfoManager, document, symbolUseRange)
                        let content = QuickInfoViewProvider.provideContent(imageId, mainDescription, docs, navigation)
                        let span = getTrackingSpan quickInfo.Span
                        return QuickInfoItem(span, content)

                    | Some _sigQuickInfo, Some targetQuickInfo ->
                        let mainDescription, targetDocumentation, sigDocumentation, typeParameterMap, exceptions, usage = ResizeArray(), ResizeArray(), ResizeArray(), ResizeArray(), ResizeArray(), ResizeArray()
                        XmlDocumentation.BuildDataTipText(documentationBuilder, mainDescription.Add, targetDocumentation.Add, typeParameterMap.Add, exceptions.Add, usage.Add, targetQuickInfo.StructuredText)
                        // get whitespace nomalized documentation text
                        let getText (tts: seq<TaggedText>) =
                            let text =
                                (StringBuilder(), tts)
                                ||> Seq.fold (fun sb tt ->
                                    if String.IsNullOrWhiteSpace tt.Text then sb else sb.Append tt.Text)
                                |> string
                            if String.IsNullOrWhiteSpace text then None else Some text

                        let documentation =
                            [ match getText targetDocumentation, getText sigDocumentation with
                              | None, None -> ()
                              | None, Some _ -> yield! sigDocumentation
                              | Some _, None -> yield! targetDocumentation
                              | Some implText, Some sigText when implText.Equals (sigText, StringComparison.OrdinalIgnoreCase) ->
                                    yield! sigDocumentation
                              | Some _  , Some _ ->
                                    yield! RoslynHelpers.joinWithLineBreaks [ sigDocumentation; [ TaggedTextOps.tagText "-------------" ]; targetDocumentation ]
                            ] |> ResizeArray
                        let docs = RoslynHelpers.joinWithLineBreaks [documentation; typeParameterMap; usage; exceptions]
                        let imageId = Tokenizer.GetImageIdForSymbol(targetQuickInfo.Symbol, targetQuickInfo.SymbolKind)
                        let navigation = QuickInfoNavigation(statusBar, checkerProvider.Checker, projectInfoManager, document, symbolUseRange)
                        let content = QuickInfoViewProvider.provideContent(imageId, mainDescription, docs, navigation)
                        let span = getTrackingSpan targetQuickInfo.Span
                        return QuickInfoItem(span, content)
                }   |> Async.map Option.toObj
                    |> RoslynHelpers.StartAsyncAsTask cancellationToken

[<Export(typeof<IAsyncQuickInfoSourceProvider>)>]
[<Microsoft.VisualStudio.Utilities.Name("F# Quick Info Provider")>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpContentType)>]
[<Microsoft.VisualStudio.Utilities.Order>]
type internal FSharpAsyncQuickInfoSourceProvider
    [<ImportingConstructor>]
    (
        checkerProvider:FSharpCheckerProvider,
        projectInfoManager:FSharpProjectOptionsManager,
        settings: EditorOptions
    ) =

    interface IAsyncQuickInfoSourceProvider with
        override _.TryCreateQuickInfoSource(textBuffer:ITextBuffer) : IAsyncQuickInfoSource =
            // GetService calls must be made on the UI thread
            // It is safe to do it here (see #4713)
            let statusBar = StatusBar()
            new FSharpAsyncQuickInfoSource(statusBar, checkerProvider, projectInfoManager, textBuffer, settings) :> _
