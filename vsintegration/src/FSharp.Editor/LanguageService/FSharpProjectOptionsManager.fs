﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.FSharp.Editor
 
open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.Collections.Immutable
open System.ComponentModel.Composition
open System.IO
open System.Linq
open Microsoft.CodeAnalysis
open FSharp.Compiler.SourceCodeServices
open Microsoft.VisualStudio
open Microsoft.VisualStudio.FSharp.Editor
open Microsoft.VisualStudio.LanguageServices
open System.Threading
open MonoDevelop.FSharp
open MonoDevelop.Ide
open MonoDevelop.Ide.TypeSystem

[<AutoOpen>]
module private FSharpProjectOptionsHelpers =
    let hasProjectVersionChanged (oldProject: Project) (newProject: Project) =
        oldProject.Version <> newProject.Version

    let hasDependentVersionChanged (oldProject: Project) (newProject: Project) =
        let oldProjectRefs = oldProject.ProjectReferences
        let newProjectRefs = newProject.ProjectReferences
        oldProjectRefs.Count() <> newProjectRefs.Count() ||
        (oldProjectRefs, newProjectRefs)
        ||> Seq.exists2 (fun p1 p2 ->
            let doesProjectIdDiffer = p1.ProjectId <> p2.ProjectId
            let p1 = oldProject.Solution.GetProject(p1.ProjectId)
            let p2 = newProject.Solution.GetProject(p2.ProjectId)
            doesProjectIdDiffer || p1.Version <> p2.Version
        )

    let isProjectInvalidated (oldProject: Project) (newProject: Project) (settings: EditorOptions) =
        let hasProjectVersionChanged = hasProjectVersionChanged oldProject newProject
        if settings.LanguageServicePerformance.EnableInMemoryCrossProjectReferences then
            hasProjectVersionChanged || hasDependentVersionChanged oldProject newProject
        else
            hasProjectVersionChanged

[<RequireQualifiedAccess>]
type private FSharpProjectOptionsMessage =
    | TryGetOptionsByDocument of Document * AsyncReplyChannel<(FSharpParsingOptions * FSharpProjectOptions) option> * CancellationToken * userOpName: string
    | TryGetOptionsByProject of Project * AsyncReplyChannel<(FSharpParsingOptions * FSharpProjectOptions) option> * CancellationToken
    | ClearOptions of ProjectId
    | ClearSingleFileOptionsCache of DocumentId

[<Sealed>]
type private FSharpProjectOptionsReactor (settings: EditorOptions, checkerProvider: FSharpCheckerProvider) =
    let cancellationTokenSource = new CancellationTokenSource()

    // Hack to store command line options from HandleCommandLineChanges
    let cpsCommandLineOptions = ConcurrentDictionary<ProjectId, string[] * string[]>()

    //let legacyProjectSites = ConcurrentDictionary<ProjectId, IProjectSite>()

    let cache = ConcurrentDictionary<ProjectId, Project * FSharpParsingOptions * FSharpProjectOptions>()
    let singleFileCache = ConcurrentDictionary<DocumentId, VersionStamp * FSharpParsingOptions * FSharpProjectOptions>()

    let rec tryComputeOptionsByFile (document: Document) (ct: CancellationToken) userOpName =
        async {
            let! fileStamp = document.GetTextVersionAsync(ct) |> Async.AwaitTask
            match singleFileCache.TryGetValue(document.Id) with
            | false, _ ->
                let! sourceText = document.GetTextAsync(ct) |> Async.AwaitTask
                
                let! scriptProjectOptions, _ = checkerProvider.Checker.GetProjectOptionsFromScript(document.FilePath, sourceText.ToFSharpSourceText(), false (* SessionsProperties.fsiPreview *), userOpName=userOpName)

                let projectOptions =
                    if isScriptFile document.FilePath then

                        if scriptProjectOptions.OtherOptions |> Seq.exists (fun s -> s.Contains("FSharp.Core.dll")) then scriptProjectOptions
                        else
                          // Add assemblies that may be missing in the standard assembly resolution
                          LoggingService.logDebug "LanguageService: GetScriptCheckerOptions: Adding missing core assemblies."
                          let dirs = FSharpEnvironment.getDefaultDirectories (None, FSharpTargetFramework.NET_4_5 )
                          { scriptProjectOptions with 
                               OtherOptions =
                               [| yield! scriptProjectOptions.OtherOptions
                                  match FSharpEnvironment.resolveAssembly dirs "FSharp.Core" with
                                  | Some fn -> yield String.Format ("-r:{0}", fn)
                                  | None ->
                                        LoggingService.logDebug "LanguageService: Resolution: FSharp.Core assembly resolution failed!"
                                        match FSharpEnvironment.resolveAssembly dirs "FSharp.Compiler.Interactive.Settings" with
                                        | Some fn -> yield String.Format ("-r:{0}", fn)
                                        | None -> LoggingService.logDebug "LanguageService: Resolution: FSharp.Compiler.Interactive.Settings assembly resolution failed!" |]}
                    else
                        {
                            ProjectFileName = document.FilePath
                            ProjectId = None
                            SourceFiles = [|document.FilePath|]
                            OtherOptions = [||]
                            ReferencedProjects = [||]
                            IsIncompleteTypeCheckEnvironment = false
                            UseScriptResolutionRules = SourceFile.MustBeSingleFileProject (Path.GetFileName(document.FilePath))
                            LoadTime = DateTime.Now
                            UnresolvedReferences = None
                            OriginalLoadReferences = []
                            ExtraProjectInfo= None
                            Stamp = Some(int64 (fileStamp.GetHashCode()))
                        }
                        
                checkerProvider.Checker.CheckProjectInBackground(projectOptions, userOpName="checkOptions")

                let parsingOptions, _ = checkerProvider.Checker.GetParsingOptionsFromProjectOptions(projectOptions)

                singleFileCache.[document.Id] <- (fileStamp, parsingOptions, projectOptions)

                return Some(parsingOptions, projectOptions)

            | true, (fileStamp2, parsingOptions, projectOptions) ->
                if fileStamp <> fileStamp2 then
                    singleFileCache.TryRemove(document.Id) |> ignore
                    return! tryComputeOptionsByFile document ct userOpName
                else
                    return Some(parsingOptions, projectOptions)
        }

    let mdLanguageService = new MonoDevelop.FSharp.LanguageService(checkerProvider.Checker, (fun (changedfile, _) -> ()), None)

    let rec tryComputeOptions (project: Project) =
        async {
            let projectId = project.Id
            match cache.TryGetValue(projectId) with
            | false, _ ->

                // Because this code can be kicked off before the hack, HandleCommandLineChanges, occurs,
                //     the command line options will not be available and we should bail if one of the project references does not give us anything.
                let mutable canBail = false

                let fsharpProject = MonoDevelop.Ide.IdeApp.TypeSystemService.GetMonoProject(project) :?> MonoDevelop.FSharp.FSharpProject
                let! refs = fsharpProject.GetReferences(MonoDevelop.FSharp.CompilerArguments.getConfig()) |> Async.AwaitTask
                canBail <- refs.Count = 0
                if canBail then
                    return None
                else
                let projectOpts = mdLanguageService.GetProjectCheckerOptions(project.FilePath, [], refs)
                return projectOpts |> Option.bind(fun opts ->
                    // This can happen if we didn't receive the callback from HandleCommandLineChanges yet.
                    if Array.isEmpty opts.SourceFiles then
                        None
                    else
                        checkerProvider.Checker.InvalidateConfiguration(opts, startBackgroundCompileIfAlreadySeen = false, userOpName = "computeOptions")
                        let parsingOptions, _ = checkerProvider.Checker.GetParsingOptionsFromProjectOptions(opts)
                        cache.[projectId] <- (project, parsingOptions, opts)
                        Some(parsingOptions, opts))
  
            | true, (oldProject, parsingOptions, projectOptions) ->
                if isProjectInvalidated oldProject project settings then
                    cache.TryRemove(projectId) |> ignore
                    return! tryComputeOptions project
                else
                    return Some(parsingOptions, projectOptions)
        }

    let loop (agent: MailboxProcessor<FSharpProjectOptionsMessage>) =
        async {
            while true do
                match! agent.Receive() with
                | FSharpProjectOptionsMessage.TryGetOptionsByDocument(document, reply, ct, userOpName) ->
                    if ct.IsCancellationRequested then
                        reply.Reply None
                    else
                        try
                            if document.Project.Solution.Workspace.Kind = WorkspaceKind.MiscellaneousFiles then
                                let! options = tryComputeOptionsByFile document ct userOpName
                                reply.Reply options
                            elif document.Project.Name = FSharpConstants.FSharpMiscellaneousFilesName then
                                let! options = tryComputeOptionsByFile document ct userOpName
                                reply.Reply options
                            else
                                // We only care about the latest project in the workspace's solution.
                                // We do this to prevent any possible cache thrashing in FCS.
                                let project = document.Project.Solution.Workspace.CurrentSolution.GetProject(document.Project.Id)
                                if not (isNull project) then
                                    let! options = tryComputeOptions project
                                    reply.Reply options
                                else
                                    reply.Reply None
                        with
                        | _ ->
                            reply.Reply None

                | FSharpProjectOptionsMessage.TryGetOptionsByProject(project, reply, ct) ->
                    if ct.IsCancellationRequested then
                        reply.Reply None
                    else
                        try
                            if project.Solution.Workspace.Kind = WorkspaceKind.MiscellaneousFiles || project.Name = FSharpConstants.FSharpMiscellaneousFilesName then
                                reply.Reply None
                            else
                                // We only care about the latest project in the workspace's solution.
                                // We do this to prevent any possible cache thrashing in FCS.
                                let project = project.Solution.Workspace.CurrentSolution.GetProject(project.Id)
                                if not (isNull project) then
                                    let! options = tryComputeOptions project
                                    reply.Reply options
                                else
                                    reply.Reply None
                        with
                        | _ ->
                            reply.Reply None

                | FSharpProjectOptionsMessage.ClearOptions(projectId) ->
                    cache.TryRemove(projectId) |> ignore
                | FSharpProjectOptionsMessage.ClearSingleFileOptionsCache(documentId) ->
                    singleFileCache.TryRemove(documentId) |> ignore
        }

    let agent = MailboxProcessor.Start((fun agent -> loop agent), cancellationToken = cancellationTokenSource.Token)

    member _.TryGetOptionsByProjectAsync(project, ct) =
        agent.PostAndAsyncReply(fun reply -> FSharpProjectOptionsMessage.TryGetOptionsByProject(project, reply, ct))

    member _.TryGetOptionsByDocumentAsync(document, ct, userOpName) =
        agent.PostAndAsyncReply(fun reply -> FSharpProjectOptionsMessage.TryGetOptionsByDocument(document, reply, ct, userOpName))

    member _.ClearOptionsByProjectId(projectId) =
        agent.Post(FSharpProjectOptionsMessage.ClearOptions(projectId))

    member _.ClearSingleFileOptionsCache(documentId) =
        agent.Post(FSharpProjectOptionsMessage.ClearSingleFileOptionsCache(documentId))

    member _.SetCpsCommandLineOptions(projectId, sourcePaths, options) =
        cpsCommandLineOptions.[projectId] <- (sourcePaths, options)

    member __.SetLegacyProjectSite (projectId, projectSite) = ()
        //legacyProjectSites.[projectId] <- projectSite

    member _.TryGetCachedOptionsByProjectId(projectId) =
        match cache.TryGetValue(projectId) with
        | true, result -> Some(result)
        | _ -> None

    interface IDisposable with
        member _.Dispose() = 
            cancellationTokenSource.Cancel()
            cancellationTokenSource.Dispose() 
            (agent :> IDisposable).Dispose()

/// Exposes FCS FSharpProjectOptions information management as MEF component.
//
// This service allows analyzers to get an appropriate FSharpProjectOptions value for a project or single file.
// It also allows a 'cheaper' route to get the project options relevant to parsing (e.g. the #define values).
// The main entrypoints are TryGetOptionsForDocumentOrProject and TryGetOptionsForEditingDocumentOrProject.
[<Export(typeof<FSharpProjectOptionsManager>); Composition.Shared>]
type internal FSharpProjectOptionsManager
    [<ImportingConstructor>]
    (
        checkerProvider: FSharpCheckerProvider,
        settings: EditorOptions
    ) =

    let reactor = new FSharpProjectOptionsReactor(settings, checkerProvider)

    do
        let workspace = IdeApp.TypeSystemService.Workspace

        // We need to listen to this event for lifecycle purposes.
        workspace.WorkspaceChanged.Add(fun args ->
            match args.Kind with
            | WorkspaceChangeKind.ProjectRemoved -> reactor.ClearOptionsByProjectId(args.ProjectId)
            | _ -> ()
        )

    member _.SetLegacyProjectSite (projectId, projectSite) =
        reactor.SetLegacyProjectSite (projectId, projectSite)

    /// Clear a project from the project table
    member this.ClearInfoForProject(projectId:ProjectId) = 
        reactor.ClearOptionsByProjectId(projectId)

    member this.ClearSingleFileOptionsCache(documentId) =
        reactor.ClearSingleFileOptionsCache(documentId)

    /// Get compilation defines relevant for syntax processing.  
    /// Quicker then TryGetOptionsForDocumentOrProject as it doesn't need to recompute the exact project 
    /// options for a script.
    member this.GetCompilationDefinesForEditingDocument(document:Document) = 
        let parsingOptions =
            match reactor.TryGetCachedOptionsByProjectId(document.Project.Id) with
            | Some (_, parsingOptions, _) -> parsingOptions
            | _ -> { FSharpParsingOptions.Default with IsInteractive = FSharpFileUtilities.isScriptFile document.Name }
        CompilerEnvironment.GetCompilationDefinesForEditing parsingOptions     

    member this.TryGetOptionsByProject(project) =
        reactor.TryGetOptionsByProjectAsync(project)

    /// Get the exact options for a document or project
    member this.TryGetOptionsForDocumentOrProject(document: Document, cancellationToken, userOpName) =
        async { 
            match! reactor.TryGetOptionsByDocumentAsync(document, cancellationToken, userOpName) with
            | Some(parsingOptions, projectOptions) ->
                return Some(parsingOptions, None, projectOptions)
            | _ ->
                return None
        }

    /// Get the exact options for a document or project relevant for syntax processing.
    member this.TryGetOptionsForEditingDocumentOrProject(document:Document, cancellationToken, userOpName) = 
        async {
            let! result = this.TryGetOptionsForDocumentOrProject(document, cancellationToken, userOpName) 
            return result |> Option.map(fun (parsingOptions, _, projectOptions) -> parsingOptions, projectOptions)
        }

    /// Get the options for a document or project relevant for syntax processing.
    /// Quicker it doesn't need to recompute the exact project options for a script.
    member this.TryGetQuickParsingOptionsForEditingDocumentOrProject(document:Document) = 
        match reactor.TryGetCachedOptionsByProjectId(document.Project.Id) with
        | Some (_, parsingOptions, _) -> parsingOptions
        | _ -> { FSharpParsingOptions.Default with IsInteractive = FSharpFileUtilities.isScriptFile document.Name }

    [<Export>]
    /// This handles commandline change notifications from the Dotnet Project-system
    /// Prior to VS 15.7 path contained path to project file, post 15.7 contains target binpath
    /// binpath is more accurate because a project file can have multiple in memory projects based on configuration
    member _.HandleCommandLineChanges(path:string, sources:ImmutableArray<CommandLineSourceFile>, _references:ImmutableArray<CommandLineReference>, options:ImmutableArray<string>) =
        ()
        //use _logBlock = Logger.LogBlock(LogEditorFunctionId.LanguageService_HandleCommandLineArgs)

        //let projectId =
        //    match Microsoft.CodeAnalysis.ExternalAccess.FSharp.LanguageServices.FSharpVisualStudioWorkspaceExtensions.TryGetProjectIdByBinPath(workspace, path) with
        //    | true, projectId -> projectId
        //    | false, _ -> Microsoft.CodeAnalysis.ExternalAccess.FSharp.LanguageServices.FSharpVisualStudioWorkspaceExtensions.GetOrCreateProjectIdForPath(workspace, path, projectDisplayNameOf path)
        //let path = Microsoft.CodeAnalysis.ExternalAccess.FSharp.LanguageServices.FSharpVisualStudioWorkspaceExtensions.GetProjectFilePath(workspace, projectId)

        //let getFullPath p =
        //    let p' =
        //        if Path.IsPathRooted(p) || path = null then p
        //        else Path.Combine(Path.GetDirectoryName(path), p)
        //    Path.GetFullPathSafe(p')

        //let sourcePaths = sources |> Seq.map(fun s -> getFullPath s.Path) |> Seq.toArray

        //reactor.SetCpsCommandLineOptions(projectId, sourcePaths, options.ToArray())

    member _.Checker = checkerProvider.Checker
