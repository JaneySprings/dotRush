﻿using System.Diagnostics;
using DotRush.Roslyn.Common.Logging;
using DotRush.Roslyn.Server.Extensions;
using DotRush.Roslyn.Server.Handlers.TextDocument;
using DotRush.Roslyn.Server.Handlers.Workspace;
using DotRush.Roslyn.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace DotRush.Roslyn.Server;

public class LanguageServer {
    public const string CodeAnalysisFeaturesAssembly = "Microsoft.CodeAnalysis.CSharp.Features";
    public static TextDocumentSelector SelectorForAllDocuments => TextDocumentSelector.ForPattern("**/*.cs", "**/*.xaml");
    public static TextDocumentSelector SelectorForSourceCodeDocuments => TextDocumentSelector.ForPattern("**/*.cs");

    public static async Task Main(string[] args) {
        var server = await OmniSharpLanguageServer.From(options => options
            .AddDefaultLoggingProvider()
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .WithServices(services => services
                .AddSingleton<ConfigurationService>()
                .AddSingleton<WorkspaceService>()
                .AddSingleton<CodeActionService>()
                .AddSingleton<CompilationService>()
                .AddSingleton<DecompilationService>()
                .AddSingleton<CommandsService>())
            .WithHandler<DidOpenTextDocumentHandler>()
            .WithHandler<DidChangeTextDocumentHandler>()
            .WithHandler<DidCloseTextDocumentHandler>()
            .WithHandler<DidChangeWatchedFilesHandler>()
            .WithHandler<DidChangeWorkspaceFoldersHandler>()
            .WithHandler<WorkspaceSymbolsHandler>()
            .WithHandler<DocumentSymbolHandler>()
            .WithHandler<HoverHandler>()
            .WithHandler<FoldingRangeHandler>()
            .WithHandler<SignatureHelpHandler>()
            .WithHandler<FormattingHandler>()
            .WithHandler<RangeFormattingHandler>()
            .WithHandler<RenameHandler>()
            .WithHandler<CompletionHandler>()
            .WithHandler<CodeActionHandler>()
            .WithHandler<ReferencesHandler>()
            .WithHandler<ImplementationHandler>()
            .WithHandler<DefinitionHandler>()
            .WithHandler<TypeDefinitionHandler>()
            .OnStarted((s, _) => StartedHandlerAsync(s, args))
        ).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }
    private static async Task StartedHandlerAsync(ILanguageServer server, IEnumerable<string> targets) {
        var clientSettings = server.Workspace.ClientSettings;
        var compilationService = server.Services.GetService<CompilationService>()!;
        var configurationService = server.Services.GetService<ConfigurationService>()!;
        var codeActionService = server.Services.GetService<CodeActionService>()!;
        var workspaceService = server.Services.GetService<WorkspaceService>()!;

        CurrentSessionLogger.Debug($"Server created with targets: {string.Join(", ", targets)}");
        ObserveClientProcess(clientSettings.ProcessId);

        await configurationService.InitializeAsync();
        if (!workspaceService.TryInitializeWorkspace(targets, _ => server.ShowError(Resources.DotNetRegistrationFailed)))
            return;

        codeActionService.InitializeEmbeddedProviders();
        if (configurationService.UseRoslynAnalyzers)
            compilationService.InitializeEmbeddedAnalyzers();

        if (!targets.Any()) {
            var workspaceFolders = server.ClientSettings.WorkspaceFolders?.Select(it => it.Uri.GetFileSystemPath());
            workspaceService.AddProjectFilesFromFolders(workspaceFolders);
        }

        _ = workspaceService.LoadSolutionAsync(CancellationToken.None);
    }
    private static void ObserveClientProcess(long? pid) {
        if (pid == null || pid <= 0)
            return;

        var ideProcess = Process.GetProcessById((int)pid.Value);
        ideProcess.EnableRaisingEvents = true;
        ideProcess.Exited += (_, _) => {
            CurrentSessionLogger.Debug($"Shutting down server because client process [{ideProcess.ProcessName}] has exited");
            Environment.Exit(0);
        };
        CurrentSessionLogger.Debug($"Server is observing client process {ideProcess.ProcessName} (PID: {pid})");
    }
}
