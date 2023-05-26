using DotRush.Server.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using CodeAnalysis = Microsoft.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace DotRush.Server.Services;

public class CompilationService {
    private SolutionService solutionService;

    public CompilationService(SolutionService solutionService) {
        this.solutionService = solutionService;
    }

    public async Task DiagnoseProject(string projectPath, ITextDocumentLanguageServer proxy, Action? onCompleted) {
        var projectIds = this.solutionService.Solution?.GetProjectIdsWithFilePath(projectPath);
        if (projectIds == null)
            return;

        var result = new Dictionary<string, List<CodeAnalysis.Diagnostic>>();
        foreach (var projectId in projectIds) {
            var project = this.solutionService.Solution?.GetProject(projectId);
            if (project == null)
                continue;
            
            foreach (var document in project.Documents) {
                var diagnostics = await Diagnose(document, CancellationToken.None);
                if (result.ContainsKey(document.FilePath!))
                    result[document.FilePath!].AddRange(diagnostics!);
                else
                    result.Add(document.FilePath!, diagnostics!.ToList());
            }
        }

        foreach (var diagnostic in result) {
            proxy.PublishDiagnostics(new PublishDiagnosticsParams() {
                Uri = DocumentUri.From(diagnostic.Key),
                Diagnostics = new Container<Diagnostic>(diagnostic.Value.ToServerDiagnostics()),
            });
        }

        onCompleted?.Invoke();
    }

    public async Task DiagnoseDocument(string documentPath, ITextDocumentLanguageServer proxy, CancellationToken cancellationToken) {
        var documentId = this.solutionService.Solution?.GetDocumentIdsWithFilePath(documentPath).FirstOrDefault();
        if (documentId == null)
            return;

        var document = this.solutionService.Solution?.GetDocument(documentId);
        if (document == null)
            return;

        var diagnostics = await Diagnose(document, cancellationToken);
        if (diagnostics == null)
            return;

        proxy.PublishDiagnostics(new PublishDiagnosticsParams() {
            Uri = DocumentUri.From(documentPath),
            Diagnostics = new Container<Diagnostic>(diagnostics.ToServerDiagnostics()),
        });
    }

    public async Task<IEnumerable<CodeAnalysis.Diagnostic>?> Diagnose(CodeAnalysis.Document document, CancellationToken cancellationToken) {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        return semanticModel?
            .GetDiagnostics(cancellationToken: cancellationToken)
            .Where(d => File.Exists(d.Location.SourceTree?.FilePath));
    }
}