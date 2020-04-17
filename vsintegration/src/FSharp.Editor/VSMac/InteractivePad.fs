
//
// InteractivePad.fs
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
namespace Microsoft.VisualStudio.FSharp.Editor

open System
open System.IO
open System.Threading.Tasks
open System.Collections.Generic
open System.Collections.Immutable

open Gdk
open MonoDevelop.Components
open MonoDevelop.Components.Docking
open MonoDevelop.Components.Commands
open MonoDevelop.Core
open MonoDevelop.Core.Execution
open MonoDevelop.FSharp
open MonoDevelop.Ide
open MonoDevelop.Ide.CodeCompletion
open MonoDevelop.Ide.Editor
open MonoDevelop.Ide.Editor.Extension
open MonoDevelop.Ide.Gui.Content
open MonoDevelop.Ide.TypeSystem
open MonoDevelop.Projects
open Microsoft.VisualStudio.Text.Editor
open MonoDevelop.Ide.Composition
open Microsoft.VisualStudio.Text
open Gtk
open Microsoft.VisualStudio.Text.Classification
open CoreGraphics
open Microsoft.VisualStudio.Core.Imaging
open Microsoft.VisualStudio.Text.Tagging
open System.ComponentModel.Composition
open Microsoft.VisualStudio.Imaging
open Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Commanding
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text.Editor.Commanding.Commands
open Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion
open Microsoft.VisualStudio.Language.Intellisense
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Text
open Microsoft.VisualStudio.FSharp.Editor
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Threading
open FSharp.Editor
open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Editor
[<AutoOpen>]
module ColorHelpers =
    let strToColor s =
        let c = ref (Color())
        match Color.Parse (s, c) with
        | true -> !c
        | false -> Color() // black is as good a guess as any here

    let colorToStr (c:Color) =
        sprintf "#%04X%04X%04X" c.Red c.Green c.Blue

    let cairoToGdk (c:Cairo.Color) = GtkUtil.ToGdkColor(c)

type FSharpCommands =
    | ShowFSharpInteractive = 0
    | SendSelection = 1
    | SendLine = 2
    | SendFile = 3

type KillIntent =
    | Restart
    | Kill
    | NoIntent // Unexpected kill, or from #q/#quit, so we prompt

//type ImageRendererMarker(line, image:Xwt.Drawing.Image) =
//    inherit TextLineMarker()
//    static let tag = obj()
//    override x.Draw(editor, cr, metrics) =
//        cr.DrawImage(editor, image, 30.0, metrics.LineYRenderStartPosition)

//    interface ITextLineMarker with
//        member x.Line with get() = line
//        member x.IsVisible with get() = true and set(_value) = ()
//        member x.Tag with get() = tag and set(_value) = ()

//    interface IExtendingTextLineMarker with
//        member x.GetLineHeight editor = editor.LineHeight + image.Height
//        member x.Draw(_editor, _g, _lineNr, _lineArea) = ()
//        member x.IsSpaceAbove with get() = false

//type FsiDocumentContext() =
//    inherit DocumentContext()
//    static let name = "__FSI__.fs"
//    let pd = new FSharpParsedDocument(name, None, None) :> ParsedDocument
//    let project = Services.ProjectService.CreateDotNetProject ("F#")

//    let mutable completionWidget:ICompletionWidget = null
//    let mutable editor:TextEditor = null

//    let contextChanged = DelegateEvent<_>()
//    let mutable workingFolder: string option = None
//    do 
//        project.FileName <- FilePath name

//    override x.ParsedDocument = pd
//    override x.AttachToProject(_) = ()
//    override x.ReparseDocument() = ()
//    override x.GetOptionSet() = IdeApp.TypeSystemService.Workspace.Options
//    override x.Project = project :> Project
//    override x.Name = name
//    override x.AnalysisDocument with get() = null
//    override x.UpdateParseDocument() = Task.FromResult pd
//    static member DocumentName = name
//    member x.CompletionWidget 
//        with set (value) = 
//            completionWidget <- value
//            completionWidget.CompletionContextChanged.Add
//                (fun _args -> let completion = editor.GetContent<CompletionTextEditorExtension>()
//                              ParameterInformationWindowManager.HideWindow(completion, value))
//    member x.Editor with set (value) = editor <- value
//    member x.WorkingFolder
//        with get() = workingFolder
//        and set(folder) = workingFolder <- folder
//    interface ICompletionWidget with
//        member x.CaretOffset
//            with get() = completionWidget.CaretOffset
//            and set(offset) = completionWidget.CaretOffset <- offset
//        member x.TextLength = editor.Length
//        member x.SelectedLength = completionWidget.SelectedLength
//        member x.GetText(startOffset, endOffset) =
//            completionWidget.GetText(startOffset, endOffset)
//        member x.GetChar offset = editor.GetCharAt offset
//        member x.Replace(offset, count, text) =
//            completionWidget.Replace(offset, count, text)
//        member x.GtkStyle = completionWidget.GtkStyle

//        member x.ZoomLevel = completionWidget.ZoomLevel
//        member x.CreateCodeCompletionContext triggerOffset =
//            completionWidget.CreateCodeCompletionContext triggerOffset
//        member x.CurrentCodeCompletionContext 
//            with get() = completionWidget.CurrentCodeCompletionContext

//        member x.GetCompletionText ctx = completionWidget.GetCompletionText ctx

//        member x.SetCompletionText (ctx, partialWord, completeWord) =
//            completionWidget.SetCompletionText (ctx, partialWord, completeWord)
//        member x.SetCompletionText (ctx, partialWord, completeWord, completeWordOffset) =
//            completionWidget.SetCompletionText (ctx, partialWord, completeWord, completeWordOffset)
//        [<CLIEvent>]
//        member x.CompletionContextChanged = contextChanged.Publish

//type FsiPrompt(icon: Xwt.Drawing.Image) =
//    inherit MarginMarker()

//    override x.CanDrawForeground margin = 
//        margin :? IconMargin

//    override x.DrawForeground (editor, cairoContext, metrics) =
//        let size = metrics.Margin.Width
//        let borderLineWidth = cairoContext.LineWidth

//        let x = Math.Floor (metrics.Margin.XOffset - borderLineWidth / 2.0)
//        let y = Math.Floor (metrics.Y + (metrics.Height - size) / 2.0)

//        let deltaX = size / 2.0 - icon.Width / 2.0 + 0.5
//        let deltaY = size / 2.0 - icon.Height / 2.0 + 0.5
//        cairoContext.DrawImage (editor, icon, Math.Round (x + deltaX), Math.Round (y + deltaY));

type ShellHistory() =
    let history = ResizeArray<string>()
    let mutable nextUp = 0
    let mutable nextDown = 0

    member x.Push command =
        history.Add command
        nextUp <- history.Count - 1
        nextDown <- history.Count - 1

    member x.Up() =
        match nextUp with
        | -1 -> None
        | index ->
            nextDown <- nextUp
            nextUp <- nextUp - 1
            Some history.[index]

    member x.Down() =
        if nextDown = history.Count then
            None
        else
            nextUp <- nextDown
            nextDown <- nextDown + 1
            if nextDown = history.Count then
                None
            else
                Some history.[nextDown]



type InteractivePromptGlyphTag() = interface IGlyphTag

type InteractiveGlyphFactory(imageId:ImageId, imageService:IImageService) =
    let mutable imageCache: AppKit.NSImage option = None
    
    interface IGlyphFactory with
        member x.GenerateGlyph(line, tag) =
            match tag with
            | :? InteractivePromptGlyphTag ->
                if imageCache.IsNone then
                    imageCache <- Some(imageService.GetImage (imageId) :?> AppKit.NSImage)
                let imageView = AppKit.NSImageView.FromImage imageCache.Value
                imageView.SetFrameSize (imageView.FittingSize)
                Some (box imageView)
            | _ -> None
            |> Option.toObj

[<Export(typeof<IGlyphFactoryProvider>)>]
[<Microsoft.VisualStudio.Utilities.Name("InteractivePromptGlyphTag")>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpInteractiveContentType)>]
[<TagType(typeof<InteractivePromptGlyphTag>)>]
//[<TextViewRole(PredefinedTextViewRoles.Debuggable)>]
type InteractiveGlyphFactoryProvider() as this =
    [<Import>]
    member val ImageService:IImageService = null with get, set

    interface IGlyphFactoryProvider with
        member x.GetGlyphFactory(view, margin) =
            let imageId = ImageId(Guid("{3404e281-57a6-4f3a-972b-185a683e0753}"), 1)
            upcast InteractiveGlyphFactory(imageId, x.ImageService)


type InteractivePromptGlyphTagger(textView: ITextView) as this =
    let tagsChanged = Event<_,_>()

    let promptSpans = HashSet<_>()
    let promptsChanged = Event<_>()

    do
    //    glyphManager.PromptsChanged.Add(fun (args) ->
    //        tagsChanged.Trigger(this, args))//  (this :> ITagger<InteractivePromptGlyphTag>).Ta
        textView.Properties.[typeof<InteractivePromptGlyphTagger>] <- this

    //member x.Controller = textView.Properties.[typeof<InteractivePadController>] :?> InteractivePadController
        
    member x.AddPrompt(pos:int) =
        if promptSpans.Add(pos) then
            tagsChanged.Trigger(this, SnapshotSpanEventArgs(SnapshotSpan(textView.TextSnapshot, pos, 1)))
    
    interface ITagger<InteractivePromptGlyphTag> with
        [<CLIEvent>]
        member this.TagsChanged = tagsChanged.Publish
        
        /// <summary>
        /// Occurs when tags are added to or removed from the provider.
        /// </summary>
        //event EventHandler<SnapshotSpanEventArgs> TagsChanged;
        //member this.add_TagsChanged(handler) = tagsChanged.Publish

        //member this.remove_TagsChanged(handler) = ()// tagsChanged.Publish.RemoveHandler(handler)
        //public IEnumerable<ITagSpan<BaseBreakpointGlyphTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        member x.GetTags(spans) =
            seq {
                for span in spans do
                    if promptSpans.Contains(span.Start.Position) then
                        yield TagSpan<InteractivePromptGlyphTag>(span, InteractivePromptGlyphTag())
            }

//type InteractiveGlyphManager(textView:ITextView) =
//    let promptSpans = HashSet<_>()
//    let promptsChanged = new Event<_>()

//    member x.PromptsChanged = promptsChanged.Publish

//    member x.PromptSpans = promptSpans

//    member x.AddPrompt(pos:int) =
//        if promptSpans.Add(pos) then
//            promptsChanged.Trigger(new SnapshotSpanEventArgs(new SnapshotSpan(textView.TextSnapshot, pos, 1)))

module InteractiveGlyphManagerService =
    let getGlyphManager(textView: ITextView) =
        textView.Properties.GetOrCreateSingletonProperty(typeof<InteractivePromptGlyphTagger>, fun () -> InteractivePromptGlyphTagger textView)

[<Microsoft.VisualStudio.Utilities.BaseDefinition("text")>]
[<Microsoft.VisualStudio.Utilities.Name(FSharpContentTypeNames.FSharpInteractiveContentType)>]
[<System.ComponentModel.Composition.Export>]
type InteractivePadController(session: InteractiveSession) as this =
    let mutable view = null
    //let mutable textView = null
    let contentTypeRegistry = CompositionManager.Instance.GetExportedValue<Microsoft.VisualStudio.Utilities.IContentTypeRegistryService>()
    let textBufferFactory = CompositionManager.Instance.GetExportedValue<ITextBufferFactoryService>()
    let factory = CompositionManager.Instance.GetExportedValue<ICocoaTextEditorFactoryService>()
    let contentType = contentTypeRegistry.GetContentType(FSharpContentTypeNames.FSharpInteractiveContentType)
    //let editorFormatMapService = CompositionManager.Instance.GetExportedValue<IEditorFormatMapService>()

    //let appearanceCategory = Guid.NewGuid().ToString()
    //let editorFormat = editorFormatMapService.GetEditorFormatMap(appearanceCategory)
    //let resourceDictionary = editorFormat.GetProperties("Plain Text")

    let roles = factory.CreateTextViewRoleSet(PredefinedTextViewRoles.Editable, PredefinedTextViewRoles.Interactive, PredefinedTextViewRoles.Document)
    let textBuffer = textBufferFactory.CreateTextBuffer("", contentType)
    
    let textView = factory.CreateTextView(textBuffer, roles)
    let workspace = new InteractiveWorkspace()
    //let (workspace: MiscellaneousFilesWorkspace) = downcast IdeApp.TypeSystemService.GetWorkspaceInternal(null)
    let history = ShellHistory()
    //textView.Background <- CGColor.CreateSrgb(nfloat 0.0, nfloat 0.0, nfloat 0.0, nfloat 0.0)
    do
        //resourceDictionary.[ClassificationFormatDefinition.TypefaceId] <- TextField.Font
        //resourceDictionary.[ClassificationFormatDefinition.FontRenderingSizeId] <- 20
        //resourceDictionary.[ClassificationFormatDefinition.BackgroundBrushId] <- System.Windows.Media.Brushes.Black
        //resourceDictionary.[ClassificationFormatDefinition.ForegroundColorId] <- System.Windows.Media.Brushes.White
        //editorFormat.SetProperties("Plain Text", resourceDictionary)

        textView.Options.SetOptionValue(DefaultTextViewOptions.UseVisibleWhitespaceId, false)
        //textView.Options.SetOptionValue(DefaultCocoaViewOptions.AppearanceCategory, appearanceCategory)
        textView.Options.SetOptionValue(DefaultTextViewHostOptions.ChangeTrackingId, false)
        textView.Options.SetOptionValue(DefaultTextViewHostOptions.LineNumberMarginId, false)
        textView.Options.SetOptionValue(DefaultTextViewHostOptions.OutliningMarginId, false)
        textView.Options.SetOptionValue(DefaultTextViewHostOptions.GlyphMarginId, true)
        textView.VisualElement.TranslatesAutoresizingMaskIntoConstraints <- false
        textView.Properties.[typeof<InteractivePadController>] <- this
        textView.Properties.[typeof<InteractiveSession>] <- session
        //textView.Properties.[typeof<InteractivePadController>] <- this
        let host = factory.CreateTextViewHost(textView, true)
        view <- host.HostControl
        // Add the view to a workspace so that Roslyn can fetch LanguageServices
        // Note: this fake file name must end with .fs, not .fsx so that we don't get the MiscellaneousFilesWorkspace
        //workspace.OnDocumentOpened("interactive.fsx", textBuffer)
        workspace.CreateDocument(textBuffer)

    let getActiveDocumentFileName () =
        if IdeApp.Workbench.ActiveDocument <> null && FileService.isInsideFSharpFile() then
            let docFileName = IdeApp.Workbench.ActiveDocument.FileName.ToString()
            if docFileName <> null then
                let directoryName = Path.GetDirectoryName docFileName
                //ctx.WorkingFolder <- Some directoryName
                Some docFileName
            else None
        else None

    let inputLines = HashSet<int>()

    let getLastLine() =
         let snapshot = textBuffer.CurrentSnapshot
         let lineCount = snapshot.LineCount

         if lineCount > 0 then
             Some (snapshot.GetLineFromLineNumber(lineCount - 1))
         else
             None

    let setCaretLine text =
        getLastLine() |> Option.iter(fun line ->
             use edit = textBuffer.CreateEdit()

             if edit.Replace(new Span(line.Start.Position, line.Length), text) then
                edit.Apply() |> ignore)

    let scrollToLastLine() =
        getLastLine() |> Option.iter(fun line ->
             let snapshotSpan = new SnapshotSpan(line.Start, 0)
             textView.ViewScroller.EnsureSpanVisible(snapshotSpan))

    let mutable readOnlyRegion = None

    let updateReadOnlyRegion() =
        getLastLine() |> Option.iter(fun line ->
            use edit = textBuffer.CreateReadOnlyRegionEdit()

            readOnlyRegion |> Option.iter(fun region -> edit.RemoveReadOnlyRegion region)
            readOnlyRegion <- edit.CreateReadOnlyRegion(new Span(0, line.Start.Position - 1)) |> Some

            edit.Apply() |> ignore)

    member this.View = view

    member this.IsInputLine(line:int) =
        let buffer = textView.TextBuffer
        inputLines.Contains line

    member this.FsiInput text =
        let fileName = getActiveDocumentFileName()
        history.Push text
        let buffer = textView.TextBuffer
        session.SendInput (text + "\n") fileName

    member this.FsiOutput text =
        let buffer = textView.TextBuffer
        use edit = buffer.CreateEdit()
        let position = buffer.CurrentSnapshot.Length

        if edit.Insert(position, text) then
            edit.Apply() |> ignore
            scrollToLastLine()
            updateReadOnlyRegion()

    member this.SetPrompt() =
        this.FsiOutput "\n"
        let buffer = textView.TextBuffer
        let snapshot = buffer.CurrentSnapshot
        let lastLine = snapshot.GetLineFromLineNumber(snapshot.LineCount - 1)
        let glyphManager = InteractiveGlyphManagerService.getGlyphManager(textView)
        inputLines.Add(snapshot.LineCount - 1) |> ignore

        scrollToLastLine()
        updateReadOnlyRegion()
        glyphManager.AddPrompt lastLine.Start.Position

    member this.HistoryUp() =
        history.Up() |> Option.iter setCaretLine

    member this.HistoryDown() =
        history.Down()
        |> function Some c -> setCaretLine c | None -> setCaretLine ""

[<Export(typeof<IViewTaggerProvider>)>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpInteractiveContentType)>]
[<TagType(typeof<InteractivePromptGlyphTag>)>]
type InteractivePromptGlyphTaggerProvider() =
    interface IViewTaggerProvider with
        member x.CreateTagger(textView, buffer) =
            box(InteractivePromptGlyphTagger textView) :?> _

type FSharpInteractivePad() as this =
    inherit MonoDevelop.Ide.Gui.PadContent()
   
    //let ctx = editor.DocumentContext :?> FsiDocumentContext
    //do
    //    let options = new CustomEditorOptions (editor.Options)
    //    editor.MimeType <- "text/x-fsharp"
    //    editor.ContextMenuPath <- "/MonoDevelop/SourceEditor2/ContextMenu/Fsi"
    //    options.ShowLineNumberMargin <- false
    //    options.TabsToSpaces <- true
    //    options.ShowWhitespaces <- ShowWhitespaces.Never
    //    ctx.CompletionWidget <- editor.GetContent<ICompletionWidget>()
    //    ctx.Editor <- editor
    //    editor.Options <- options

    let mutable killIntent = NoIntent
    let mutable promptReceived = false
    let mutable activeDoc : IDisposable option = None
    let mutable lastLineOutput = None

    let promptIcon = ImageService.GetIcon("md-breadcrumb-next")
    let newLineIcon = ImageService.GetIcon("md-template")

    let getActiveDocumentFileName () =
        if IdeApp.Workbench.ActiveDocument <> null && FileService.isInsideFSharpFile() then
            let docFileName = IdeApp.Workbench.ActiveDocument.FileName.ToString()
            if docFileName <> null then
                let directoryName = Path.GetDirectoryName docFileName
                //ctx.WorkingFolder <- Some directoryName
                Some docFileName
            else None
        else None

    let nonBreakingSpace = "\u00A0" // used to disable editor syntax highlighting for output

    //let addMarker image =
    //    let data = editor.GetContent<ITextEditorDataProvider>().GetTextEditorData()
    //    let textDocument = data.Document

    //    let line = data.GetLineByOffset editor.Length
    //    let prompt = FsiPrompt image

    //    textDocument.AddMarker(line, prompt)

    //    textDocument.CommitUpdateAll()

    let setPrompt() =
        ()
        //editor.InsertText(editor.Length, "\n")
        //editor.ScrollTo editor.Length
        //addMarker promptIcon

    //let renderImage image =
    //    let data = editor.GetContent<ITextEditorDataProvider>().GetTextEditorData()
    //    let textDocument = data.Document
    //    let line = editor.GetLine editor.CaretLine
    //    let imageMarker = ImageRendererMarker(line, image)
    //    textDocument.AddMarker(editor.CaretLine, imageMarker)
    //    textDocument.CommitUpdateAll()
    //    editor.InsertAtCaret "\n"

    let input = new ResizeArray<_>()

    let setupSession() =
        try
            let pathToExe =
                Path.Combine(Reflection.Assembly.GetExecutingAssembly().Location |> Path.GetDirectoryName, "MonoDevelop.FSharpInteractive.Service.exe")
                |> ProcessArgumentBuilder.Quote
            let ses = InteractiveSession(pathToExe)
            let controller = new InteractivePadController(ses)
            this.Controller <- Some controller
            this.Host <- new GtkNSViewHost(controller.View)
            this.Host.ShowAll()

            input.Clear()
            promptReceived <- false
            let textReceived = ses.TextReceived.Subscribe(fun t -> Runtime.RunInMainThread(fun () -> controller.FsiOutput t) |> ignore)
            //let imageReceived = ses.ImageReceived.Subscribe(fun image -> Runtime.RunInMainThread(fun () -> renderImage image) |> Async.AwaitTask |> Async.RunSynchronously)
            let promptReady = ses.PromptReady.Subscribe(fun () -> Runtime.RunInMainThread(fun () -> promptReceived <- true; controller.SetPrompt() ) |> ignore)

            ses.Exited.Add(fun _ ->
                textReceived.Dispose()
                promptReady.Dispose()
                //imageReceived.Dispose()
                if killIntent = NoIntent then
                    Runtime.RunInMainThread(fun () ->
                        LoggingService.LogDebug ("Interactive: process stopped")
                        (*this.FsiOutput "\nSession termination detected. Press Enter to restart." *))|> ignore
                elif killIntent = Restart then
                    Runtime.RunInMainThread (fun () -> ()(*editor.Text <- ""*)) |> ignore
                killIntent <- NoIntent)

            ses.StartReceiving()
            //editor.GrabFocus()
            Some(ses)
        with 
        | :? Exception as e ->
            None

    let mutable session = None

    let setCaretLine (s: string) = ()
        //let line = editor.GetLineByOffset editor.CaretOffset
        //editor.ReplaceText(line.Offset, line.EndOffset - line.Offset, s)

    let resetFsi intent =
        if promptReceived then
            killIntent <- intent
            //session |> Option.iter (fun (ses: InteractiveSession) -> ses.Kill())
            //if intent = Restart then session <- setupSession()

    let history = ShellHistory()
    //new() =
    //    let ctx = FsiDocumentContext()
    //    let doc = TextEditorFactory.CreateNewDocument()
    //    do
    //        doc.FileName <- FilePath ctx.Name

    //    let editor = TextEditorFactory.CreateNewEditor(ctx, doc, TextEditorType.Default)
    //    new FSharpInteractivePad(editor)

    //member x.FsiOutput t : unit =
    //    if editor.CaretColumn <> 1 then
    //        editor.InsertAtCaret ("\n")
    //    editor.InsertAtCaret (nonBreakingSpace + t)
    //    editor.CaretOffset <- editor.Text.Length
    //    editor.ScrollTo editor.CaretLocation
    //    lastLineOutput <- Some editor.CaretLine

    //member x.Text =
    //    editor.Text

    //member x.SetPrompt() =
    //    editor.InsertText(editor.Length, "\n")
    //    editor.ScrollTo editor.Length
    //    addMarker promptIcon

    //member x.AddMorePrompt() =
    //    addMarker newLineIcon

    member x.Session = session

    member x.Shutdown()  =
        do LoggingService.LogDebug ("Interactive: Shutdown()!")
        resetFsi Kill

    //member x.SendCommandAndStore command =
    //    let fileName = getActiveDocumentFileName()
    //    input.Add command
    //    session
    //    |> Option.iter(fun ses ->
    //        history.Push command
    //        ses.SendInput (command + "\n") fileName)

    //member x.SendCommand command =
    //    let fileName = getActiveDocumentFileName()

    //    input.Add command
    //    session
    //    |> Option.iter(fun ses ->
    //        ses.SendInput (command + ";;") fileName)

    //member x.RequestCompletions lineStr column =
    //    session 
    //    |> Option.iter(fun ses ->
    //        ses.SendCompletionRequest lineStr (column + 1))

    //member x.RequestTooltip symbol =
    //    session 
    //    |> Option.iter(fun ses -> ses.SendTooltipRequest symbol)

    //member x.RequestParameterHint lineStr column =
    //    session 
    //    |> Option.iter(fun ses ->
    //        ses.SendParameterHintRequest lineStr (column + 1))

    member x.ProcessCommandHistoryUp () =
        history.Up()
        |> Option.iter setCaretLine

    member x.ProcessCommandHistoryDown () =
        history.Down()
        |> function Some c -> setCaretLine c | None -> setCaretLine ""

    override x.Dispose() =
        LoggingService.LogDebug ("Interactive: disposing pad...")
        activeDoc |> Option.iter (fun ad -> ad.Dispose())
        x.Shutdown()
        //editor.Dispose()

    override x.Control = Control.op_Implicit x.Host
 
    static member Pad =
        try let pad = IdeApp.Workbench.GetPad<FSharpInteractivePad>()
            
            if pad <> null then Some(pad)
            else
                //*attempt* to add the pad manually this seems to fail sporadically on updates and reinstalls, returning null
                let pad = IdeApp.Workbench.AddPad(new FSharpInteractivePad(),
                                                  "FSharp.MonoDevelop.FSharpInteractivePad",
                                                  "F# Interactive",
                                                  "Center Bottom",
                                                  IconId("md-fs-project"))
                if pad <> null then Some(pad)
                else None
        with exn -> None

    static member BringToFront(grabfocus) =
        FSharpInteractivePad.Pad |> Option.iter (fun pad -> pad.BringToFront(grabfocus))

    static member Fsi =
        FSharpInteractivePad.Pad |> Option.bind (fun pad -> Some(pad.Content :?> FSharpInteractivePad))

    member x.LastOutputLine
        with get() = lastLineOutput
        and set value = lastLineOutput <- value

    member x.SendSelection() =
        if x.IsSelectionNonEmpty then
            let textView = IdeApp.Workbench.ActiveDocument.GetContent<ITextView>()
            ()
            //for span in textView.Selection.VirtualSelectedSpans do
            //    x.SendCommand (span.GetText())
        else
          //if nothing is selected send the whole line
            x.SendLine()

    member x.SendLine() =
        if isNull IdeApp.Workbench.ActiveDocument then ()
        else
            let view = IdeApp.Workbench.ActiveDocument.GetContent<ITextView>();
            let line = view.Caret.Position.BufferPosition.GetContainingLine();
            let text = line.GetText()
            //x.SendCommand text
            ()

    member x.SendFile() =
        let text = IdeApp.Workbench.ActiveDocument.TextBuffer.CurrentSnapshot.GetText()
        //x.SendCommand text
        ()

    member x.IsSelectionNonEmpty =
        if isNull IdeApp.Workbench.ActiveDocument ||
            isNull IdeApp.Workbench.ActiveDocument.FileName.FileName then false
        else
            let textView = IdeApp.Workbench.ActiveDocument.GetContent<ITextView>()
            not(textView.Selection.IsEmpty)

    member x.LoadReferences(project:FSharpProject) =
        LoggingService.LogDebug ("FSI:  #LoadReferences")
        async {
            let! orderedReferences = project.GetOrderedReferences (CompilerArguments.getConfig())
            orderedReferences |> List.iter (fun a -> (*x.SendCommand (sprintf  @"#r ""%s""" a.Path)) *) ())
        } |> Async.StartImmediate

    member val Host:GtkNSViewHost = null with get, set
    member val Controller:InteractivePadController option = None with get, set

    override x.Initialize(container:MonoDevelop.Ide.Gui.IPadWindow) =
        //this.Controller <- Some controller
        //controller.ConsoleInput += OnViewConsoleInput;
        //controller.Editable <- true;

        LoggingService.LogDebug ("InteractivePad: created!")
        //editor.MimeType <- "text/x-fsharp"
        //ctx.CompletionWidget <- editor.GetContent<ICompletionWidget>()
        //ctx.Editor <- editor
        let toolbar = container.GetToolbar(DockPositionType.Right)

        let addButton(icon, action, tooltip) =
            let button = new DockToolButton(icon)
            button.Clicked.Add(action)
            button.TooltipText <- tooltip
            toolbar.Add(button)

        addButton ("gtk-save", (fun _ -> x.Save()), GettextCatalog.GetString ("Save as script"))
        addButton ("gtk-open", (fun _ -> x.OpenScript()), GettextCatalog.GetString ("Open"))
        //addButton ("gtk-clear", (fun _ -> editor.Text <- ""), GettextCatalog.GetString ("Clear"))
        addButton ("gtk-refresh", (fun _ -> x.RestartFsi()), GettextCatalog.GetString ("Reset"))
        toolbar.ShowAll()
        session <- setupSession()
        //editor.RunWhenRealized(fun () -> session <- setupSession())

    member x.RestartFsi() = resetFsi Restart

    //member x.ClearFsi() = editor.Text <- ""

    member x.Save() =
        let dlg = new MonoDevelop.Ide.Gui.Dialogs.OpenFileDialog(GettextCatalog.GetString ("Save as .fsx"), MonoDevelop.Components.FileChooserAction.Save)

        dlg.DefaultFilter <- dlg.AddFilter (GettextCatalog.GetString ("F# script files"), "*.fsx")
        if dlg.Run () then
            let file = 
                if dlg.SelectedFile.Extension = ".fsx" then
                    dlg.SelectedFile
                else
                    dlg.SelectedFile.ChangeExtension(".fsx")

            let lines = []// input |> Seq.map (fun line -> line.TrimEnd(';'))
            let fileContent = String.concat "\n" lines
            File.WriteAllText(file.FullPath.ToString(), fileContent)

    member x.OpenScript() =
        let dlg = MonoDevelop.Ide.Gui.Dialogs.OpenFileDialog(GettextCatalog.GetString ("Open script"), MonoDevelop.Components.FileChooserAction.Open)
        dlg.AddFilter (GettextCatalog.GetString ("F# script files"), [|".fs"; "*.fsi"; "*.fsx"; "*.fsscript"; "*.ml"; "*.mli" |]) |> ignore
        if dlg.Run () then
            let file = dlg.SelectedFile
            //x.SendCommand ("#load @\"" + file.FullPath.ToString() + "\"")
            ()
[<Microsoft.VisualStudio.Utilities.Name("InteractivePadCompletionTypeCharHandler")>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpInteractiveContentType)>]
//[<TextViewRole(PredefinedTextViewRoles.Interactive)>]
[<Export(typeof<ICommandHandler>)>]
[<Microsoft.VisualStudio.Utilities.Order(After = PredefinedCompletionNames.CompletionCommandHandler)>]
type InteractivePadCompletionTypeCharHandler() =
    interface ICommandHandler<TypeCharCommandArgs> with
        member x.DisplayName = "InteractivePadCompletionTypeCharHandler"
        member x.GetCommandState _args =
            CommandState.Available

        member x.ExecuteCommand(args, context) =
            false

[<Microsoft.VisualStudio.Utilities.Name("InteractivePadCompletionReturn")>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpInteractiveContentType)>]
//[<TextViewRole(PredefinedTextViewRoles.Interactive)>]
[<Export(typeof<ICommandHandler>)>]
//[<Microsoft.VisualStudio.Utilities.Order(After = PredefinedCompletionNames.CompletionCommandHandler)>]
type InteractivePadCompletionReturnHandler
    [<ImportingConstructor>]
    ( completionBroker:ICompletionBroker ) =

    interface ICommandHandler<ReturnKeyCommandArgs> with
        member x.DisplayName = "InteractivePadKeyReturnHandler"
        member x.GetCommandState _args =
            CommandState.Available

        member x.ExecuteCommand(args, context) =
            if completionBroker.IsCompletionActive(args.TextView) then
                false
            else
            let textView = args.TextView
            let (controller: InteractivePadController) = downcast textView.Properties.[typeof<InteractivePadController>]

            let textBuffer = textView.TextBuffer
            let snapshot = textBuffer.CurrentSnapshot
            let position = textView.Caret.Position.BufferPosition.Position
            let line = snapshot.GetLineFromPosition(position)
            let start = line.Start.Position
            let finish = line.End.Position

            let start = Math.Min(start, finish);

            let span = new Span(start, finish - start)
            let text = snapshot.GetText(span)
            controller.FsiOutput "\n"
            controller.FsiInput text
            true

[<Microsoft.VisualStudio.Utilities.Name("InteractivePadCompletionUp")>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpInteractiveContentType)>]
[<Export(typeof<ICommandHandler>)>]
type InteractivePadCompletionUpHandler
    [<ImportingConstructor>]
    ( completionBroker:ICompletionBroker ) =
    interface ICommandHandler<UpKeyCommandArgs> with
        member x.DisplayName = "InteractivePadKeyUpHandler"
        member x.GetCommandState _args = CommandState.Available

        member x.ExecuteCommand(args, context) =
            if completionBroker.IsCompletionActive(args.TextView) then
                false
            else
            let textView = args.TextView
            let (controller: InteractivePadController) = downcast textView.Properties.[typeof<InteractivePadController>]
            controller.HistoryUp()
            true

[<Microsoft.VisualStudio.Utilities.Name("InteractivePadCompletionDown")>]
[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpInteractiveContentType)>]
[<Export(typeof<ICommandHandler>)>]
type InteractivePadCompletionDownHandler
    [<ImportingConstructor>]
    ( completionBroker:ICompletionBroker ) =
    interface ICommandHandler<DownKeyCommandArgs> with
        member x.DisplayName = "InteractivePadKeyDownHandler"
        member x.GetCommandState _args = CommandState.Available

        member x.ExecuteCommand(args, context) =
            if completionBroker.IsCompletionActive(args.TextView) then
                false
            else
            let textView = args.TextView
            let (controller: InteractivePadController) = downcast textView.Properties.[typeof<InteractivePadController>]
            controller.HistoryDown()
            true
/// handles keypresses for F# Interactive
//type FSharpFsiEditorCompletion() =
//    inherit TextEditorExtension()
//    override x.IsValidInContext(context) =
//        context :? FsiDocumentContext

//    override x.KeyPress (descriptor:KeyDescriptor) =
//        match FSharpInteractivePad.Fsi with
//        | Some fsi -> 
//            let getLineText (editor:TextEditor) (line:IDocumentLine) =
//                if line.Length > 0 then
//                    editor.GetLineText line
//                else
//                    ""

//            let getInputLines (editor:TextEditor) =
//                let lineNumbers =
//                    match fsi.LastOutputLine with
//                    | Some lineNumber ->
//                        [ lineNumber+1 .. editor.CaretLine ]
//                    | None -> [ editor.CaretLine ]
//                lineNumbers 
//                |> List.map editor.GetLine
//                |> List.map (getLineText editor)

//            let result =
//                match descriptor.SpecialKey with
//                | SpecialKey.Return ->
//                    if x.Editor.CaretLine = x.Editor.LineCount then
//                        let lines = getInputLines x.Editor
//                        lines
//                        |> List.iter(fun (lineStr) ->
//                            fsi.SendCommandAndStore lineStr)

//                        let line = x.Editor.GetLine x.Editor.CaretLine
//                        let lineStr = getLineText x.Editor line
//                        x.Editor.CaretOffset <- line.EndOffset
//                        x.Editor.InsertAtCaret "\n"

//                        if not (lineStr.TrimEnd().EndsWith(";;")) then
//                            fsi.AddMorePrompt()
//                        fsi.LastOutputLine <- Some line.LineNumber
//                    false
//                | SpecialKey.Up -> 
//                    if x.Editor.CaretLine = x.Editor.LineCount then
//                        fsi.ProcessCommandHistoryUp()
//                        false
//                    else
//                        base.KeyPress (descriptor)
//                | SpecialKey.Down -> 
//                    if x.Editor.CaretLine = x.Editor.LineCount then
//                        fsi.ProcessCommandHistoryDown()
//                        false
//                    else
//                        base.KeyPress (descriptor)
//                | SpecialKey.Left ->
//                    if (x.Editor.CaretLine <> x.Editor.LineCount) || x.Editor.CaretColumn > 1 then
//                        base.KeyPress (descriptor)
//                    else
//                        false
//                | SpecialKey.BackSpace ->
//                    if x.Editor.CaretLine = x.Editor.LineCount && x.Editor.CaretColumn > 1 then
//                        base.KeyPress (descriptor)
//                    else
//                        false
//                | _ -> 
//                    if x.Editor.CaretLine <> x.Editor.LineCount then
//                        x.Editor.CaretOffset <- x.Editor.Length
//                    base.KeyPress (descriptor)

//            result
//        | _ -> base.KeyPress (descriptor)

//    member x.clipboardHandler = x.Editor.GetContent<IClipboardHandler>()

//    [<CommandHandler ("MonoDevelop.Ide.Commands.EditCommands.Cut")>]
//    member x.Cut() = x.clipboardHandler.Cut()

//    [<CommandUpdateHandler ("MonoDevelop.Ide.Commands.EditCommands.Cut")>]
//    member x.CanCut(ci:CommandInfo) =
//        ci.Enabled <- x.clipboardHandler.EnableCut

//    [<CommandHandler ("MonoDevelop.Ide.Commands.EditCommands.Copy")>]
//    member x.Copy() = x.clipboardHandler.Copy()

//    [<CommandUpdateHandler ("MonoDevelop.Ide.Commands.EditCommands.Copy")>]
//    member x.CanCopy(ci:CommandInfo) =
//        ci.Enabled <- x.clipboardHandler.EnableCopy

//    [<CommandHandler ("MonoDevelop.Ide.Commands.EditCommands.Paste")>]
//    member x.Paste() = x.clipboardHandler.Paste()

//    [<CommandUpdateHandler ("MonoDevelop.Ide.Commands.EditCommands.Paste")>]
//    member x.CanPaste(ci:CommandInfo) =
//        ci.Enabled <- x.clipboardHandler.EnablePaste

//    [<CommandHandler ("MonoDevelop.Ide.Commands.ViewCommands.ZoomIn")>]
//    member x.ZoomIn() = x.Editor.GetContent<IZoomable>().ZoomIn()

//    [<CommandHandler ("MonoDevelop.Ide.Commands.ViewCommands.ZoomOut")>]
//    member x.ZoomOut() = x.Editor.GetContent<IZoomable>().ZoomOut()

//    [<CommandHandler ("MonoDevelop.Ide.Commands.ViewCommands.ZoomReset")>]
//    member x.ZoomReset() = x.Editor.GetContent<IZoomable>().ZoomReset()

type InteractiveCommand(command) =
    inherit CommandHandler()

    override x.Run() =
        FSharpInteractivePad.Fsi
        |> Option.iter (fun fsi -> command fsi
                                   FSharpInteractivePad.BringToFront(false))

type FSharpFileInteractiveCommand(command) =
    inherit InteractiveCommand(command)

    override x.Update(info:CommandInfo) =
        info.Enabled <- true
        info.Visible <- FileService.isInsideFSharpFile()

type ShowFSharpInteractive() =
    inherit InteractiveCommand(ignore)
    override x.Update(info:CommandInfo) =
        info.Enabled <- true
        info.Visible <- true

type SendSelection() =
    inherit FSharpFileInteractiveCommand(fun fsi -> fsi.SendSelection())

type SendLine() =
    inherit FSharpFileInteractiveCommand(fun fsi -> fsi.SendLine())

type SendFile() =
    inherit FSharpFileInteractiveCommand(fun fsi -> fsi.SendFile())

type SendReferences() =
    inherit CommandHandler()
    override x.Run() =
        async {
            let project = IdeApp.Workbench.ActiveDocument.Owner :?> FSharpProject
            FSharpInteractivePad.Fsi
            |> Option.iter (fun fsi -> fsi.LoadReferences(project)
                                       FSharpInteractivePad.BringToFront(false))
        } |> Async.StartImmediate

type RestartFsi() =
    inherit InteractiveCommand(fun fsi -> fsi.RestartFsi())

//type ClearFsi() =
//    inherit InteractiveCommand(fun fsi -> fsi.ClearFsi())

//open System.ComponentModel.Composition
//open Microsoft.VisualStudio.Text.Editor
//open MonoDevelop.TextEditor
////open Microsoft.CodeAnalysis.ExternalAccess.FSharp.Editor
////open Microsoft.VisualStudio.FSharp.Editor
//open Microsoft.VisualStudio.Threading
  
//[<Export(typeof<IEditorContentProvider>)>]
//[<Microsoft.VisualStudio.Utilities.ContentType(FSharpContentTypeNames.FSharpContentType)>]
//[<TextViewRole(PredefinedTextViewRoles.PrimaryDocument)>]
//[<Microsoft.VisualStudio.Utilities.Order(Before = "Default")>]
//type internal FSharpPathedDocumentExtensionProvider
//    [<ImportingConstructor>]
//    (
//        //fsharpCheckerProvider: FSharpCheckerProvider,
//        //optionsManager: FSharpProjectOptionsManagerk,
//        joinableTaskContext: JoinableTaskContext
//    ) as x =
//    inherit EditorContentInstanceProvider<FSharpPathedDocumentExtension>()
  
//    override x.CreateInstance(view) = new FSharpPathedDocumentExtension(optionsManager, fsharpCheckerProvider.Checker, view, joinableTaskContext)
