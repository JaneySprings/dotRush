using Microsoft.CodeAnalysis.CodeActions;
using DotRush.Roslyn.Server.Services;
using ProtocolModels = OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using System.Reflection;
using DotRush.Roslyn.Common.Extensions;
using Microsoft.CodeAnalysis;
using FileSystemExtensions = DotRush.Roslyn.Common.Extensions.FileSystemExtensions;

namespace DotRush.Roslyn.Server.Extensions;

public static class CodeActionExtensions {
    private static FieldInfo? inNewFileField;

    public static IEnumerable<CodeAction> ToSingleCodeActions(this CodeAction codeAction) {
        if (codeAction.NestedActions.IsEmpty)
            return new[] { codeAction };

        return codeAction.NestedActions.SelectMany(it => it.ToSingleCodeActions());
    }

    public static ProtocolModels.CodeAction ToCodeAction(this CodeAction codeAction, int? uniqueId = null) {
        return new ProtocolModels.CodeAction() {
            IsPreferred = codeAction.Priority == CodeActionPriority.High,
            Kind = ProtocolModels.CodeActionKind.QuickFix,
            Title = codeAction.Title,
            Data = uniqueId,
        };
    }

    public static async Task<ProtocolModels.CodeAction?> ResolveCodeActionAsync(this CodeAction codeAction, WorkspaceService solutionService, CancellationToken cancellationToken) {
        if (solutionService.Solution == null)
            return null;

        var textDocumentEdits = new List<ProtocolModels.TextDocumentEdit>();
        var operations = await codeAction.GetOperationsAsync(solutionService.Solution, new ProgreeMock(), cancellationToken);
        foreach (var operation in operations) {
            if (operation is ApplyChangesOperation applyChangesOperation) {
                var solutionChanges = applyChangesOperation.ChangedSolution.GetChanges(solutionService.Solution);
                foreach (var projectChanges in solutionChanges.GetProjectChanges()) {
                    foreach (var documentChanges in projectChanges.GetChangedDocuments()) {
                        var newDocument = projectChanges.NewProject.GetDocument(documentChanges);
                        var oldDocument = solutionService.Solution.GetDocument(newDocument?.Id);
                        if (oldDocument?.FilePath == null || newDocument?.FilePath == null)
                            continue;
                        if (textDocumentEdits.Any(x => FileSystemExtensions.PathEquals(x.TextDocument.Uri.GetFileSystemPath(), newDocument.FilePath)))
                            continue;

                        var sourceText = await oldDocument.GetTextAsync(cancellationToken);
                        var textEdits = new List<ProtocolModels.TextEdit>();
                        var textChanges = await newDocument.GetTextChangesAsync(oldDocument, cancellationToken);
                        foreach (var textChange in textChanges) {
                            textEdits.Add(new ProtocolModels.TextEdit() {
                                NewText = textChange.NewText ?? string.Empty,
                                Range = textChange.Span.ToRange(sourceText),
                            });
                        }
                        textDocumentEdits.Add(new ProtocolModels.TextDocumentEdit() {
                            Edits = textEdits,
                            TextDocument = new ProtocolModels.OptionalVersionedTextDocumentIdentifier() {
                                Uri = DocumentUri.FromFileSystemPath(newDocument.FilePath)
                            }
                        });
                    }
                }
            }
        }

        return new ProtocolModels.CodeAction() {
            Kind = ProtocolModels.CodeActionKind.QuickFix,
            Title = codeAction.Title,
            Edit = new ProtocolModels.WorkspaceEdit() {
                DocumentChanges = new ProtocolModels.Container<ProtocolModels.WorkspaceEditDocumentChange>(
                    textDocumentEdits.Select(x => new ProtocolModels.WorkspaceEditDocumentChange(x))
                ),
            },
        };
    }

    public static bool IsBlacklisted(this CodeAction codeAction) {
        var actionType = codeAction.GetType();
        var actionName = actionType.Name;
        if (actionName == "GenerateTypeCodeActionWithOption" || actionName == "ChangeSignatureCodeAction" || actionName == "PullMemberUpWithDialogCodeAction")
            return true;

        if (actionName != "GenerateTypeCodeAction")
            return false;

        if (inNewFileField == null)
            inNewFileField = actionType.GetField("_inNewFile", BindingFlags.Instance | BindingFlags.NonPublic);

        var isNewFile = inNewFileField?.GetValue(codeAction);
        return isNewFile != null && (bool)isNewFile;
    }
}

public class ProgreeMock : IProgress<CodeAnalysisProgress> {
    public void Report(CodeAnalysisProgress value) {
    }
}