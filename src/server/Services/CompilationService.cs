using DotRush.Server.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using DotRush.Server.Containers;
using System.Reflection;

namespace DotRush.Server.Services;

public class CompilationService {
    private readonly HashSet<string> documents;
    private readonly SolutionService solutionService;
    private IEnumerable<DiagnosticAnalyzer> embeddedAnalyzers;

    public Dictionary<string, FileDiagnostics> Diagnostics { get; }

    private CancellationTokenSource? analyzerDiagnosticsTokenSource;
    private CancellationToken AnalyzerDiagnosticsCancellationToken {
        get {
            CancelAnalyzerDiagnostics();
            this.analyzerDiagnosticsTokenSource = new CancellationTokenSource();
            return this.analyzerDiagnosticsTokenSource.Token;
        }
    }
    private CancellationTokenSource? diagnosticsTokenSource;
    private CancellationToken DiagnosticsCancellationToken {
        get {
            CancelDiagnostics();
            this.diagnosticsTokenSource = new CancellationTokenSource();
            return this.diagnosticsTokenSource.Token;
        }
    }

    public CompilationService(SolutionService solutionService) {
        this.embeddedAnalyzers = Enumerable.Empty<DiagnosticAnalyzer>();
        this.solutionService = solutionService;
        this.documents = new HashSet<string>();
        Diagnostics = new Dictionary<string, FileDiagnostics>();
    }

    public void InitializeEmbeddedAnalyzers() {
        this.embeddedAnalyzers = Assembly.Load(LanguageServer.EmbeddedCodeFixAssembly).DefinedTypes
            .Where(x => !x.IsAbstract && x.IsSubclassOf(typeof(DiagnosticAnalyzer)))
            .Select(x => {
                try {
                    return Activator.CreateInstance(x.AsType()) as DiagnosticAnalyzer;
                } catch (Exception ex) {
                    LoggingService.Instance.LogError($"Creating instance of analyzer '{x.AsType()}' failed, error: {ex}");
                    return null;
                }
            })
            .Where(x => x != null)!;
    }

    public void StartDiagnostic(string currentDocumentPath, ITextDocumentLanguageServer proxy) {
        var cancellationToken = DiagnosticsCancellationToken;
        ServerExtensions.StartOperationWithSafeCancellation(async () => {
            foreach (var documentPath in this.documents) {
                Diagnostics[documentPath].ClearSyntaxDiagnostics();
                
                var documentIds = this.solutionService.Solution?.GetDocumentIdsWithFilePath(documentPath);
                if (documentIds == null)
                    return;

                foreach (var documentId in documentIds) {
                    var document = this.solutionService.Solution?.GetDocument(documentId);
                    if (document == null)
                        continue;

                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                    var diagnostics = semanticModel?
                        .GetDiagnostics(cancellationToken: cancellationToken)
                        .Where(d => File.Exists(d.Location.SourceTree?.FilePath));

                    if (diagnostics == null)
                        continue;

                    Diagnostics[documentPath].AddSyntaxDiagnostics(diagnostics, document.Project);
                }

                var totalDiagnostics = (documentPath == currentDocumentPath)
                    ? Diagnostics[documentPath].SyntaxDiagnostics.ToServerDiagnostics()
                    : Diagnostics[documentPath].GetTotalServerDiagnostics();

                proxy.PublishDiagnostics(new PublishDiagnosticsParams() {
                    Uri = DocumentUri.From(documentPath),
                    Diagnostics = new Container<Diagnostic>(totalDiagnostics),
                });
            }
        });
    }

    public void StartAnalyzerDiagnostic(string documentPath, ITextDocumentLanguageServer proxy) {
        var cancellationToken = AnalyzerDiagnosticsCancellationToken;
        ServerExtensions.StartOperationWithSafeCancellation(async () => {
            await Task.Delay(750, cancellationToken); //TODO: Wait for completionHandler. Maybe there is a better way to do this?

            var projectId = this.solutionService.Solution?.GetProjectIdsWithDocumentFilePath(documentPath).FirstOrDefault();
            var project = this.solutionService.Solution?.GetProject(projectId);
            if (project == null)
                return;

            var diagnosticAnalyzers = project.AnalyzerReferences
                .SelectMany(x => x.GetAnalyzers(project.Language))
                .Concat(this.embeddedAnalyzers)
                .ToImmutableArray();
            
            if (diagnosticAnalyzers.IsEmpty)
                return;

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation == null)
                return;

            var compilationWithAnalyzers = compilation.WithAnalyzers(diagnosticAnalyzers, project.AnalyzerOptions, cancellationToken);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
            var fileDiagnostics = diagnostics.Where(d => d.Location.SourceTree?.FilePath != null && d.Location.SourceTree?.FilePath == documentPath);

            Diagnostics[documentPath].SetAnalyzerDiagnostics(fileDiagnostics, project);
            proxy.PublishDiagnostics(new PublishDiagnosticsParams() {
                Uri = DocumentUri.From(documentPath),
                Diagnostics = new Container<Diagnostic>(Diagnostics[documentPath].GetTotalServerDiagnostics()),
            });
        });
    }

    public void AddDocument(string documentPath) {
        if (!this.documents.Add(documentPath))
            return;

        Diagnostics.Add(documentPath, new FileDiagnostics());
    }
    public void RemoveDocument(string documentPath, ITextDocumentLanguageServer proxy) {
        if (!this.documents.Remove(documentPath))
            return;

        Diagnostics.Remove(documentPath);
        proxy.PublishDiagnostics(new PublishDiagnosticsParams() {
            Uri = DocumentUri.From(documentPath),
            Diagnostics = new Container<Diagnostic>(Enumerable.Empty<Diagnostic>()),
        });
    }

    public void CancelAnalyzerDiagnostics() {
        if (this.analyzerDiagnosticsTokenSource == null)
            return;
    
        this.analyzerDiagnosticsTokenSource.Cancel();
        this.analyzerDiagnosticsTokenSource.Dispose();
        this.analyzerDiagnosticsTokenSource = null;
    }
    public void CancelDiagnostics() {
        if (this.diagnosticsTokenSource == null)
            return;

        this.diagnosticsTokenSource.Cancel();
        this.diagnosticsTokenSource.Dispose();
        this.diagnosticsTokenSource = null;
    }
}