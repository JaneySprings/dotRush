using DotRush.Server.Extensions;
using DotRush.Server.Services;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace DotRush.Server.Handlers;

public class WatchedFilesHandler : DidChangeWatchedFilesHandlerBase {
    private readonly WorkspaceService workspaceService;

    public WatchedFilesHandler(WorkspaceService workspaceService) {
        this.workspaceService = workspaceService;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities) {
        return new DidChangeWatchedFilesRegistrationOptions() {
            Watchers = new[] {
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher() {
                    Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete,
                    GlobPattern = new GlobPattern("**/*")
                },
            }
        };
    }

    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken) {
        foreach (var change in request.Changes) {
            var path = change.Uri.GetFileSystemPath();
            HandleFileChange(path, change.Type);

            if (change.Type == FileChangeType.Created && Directory.Exists(path)) {
                foreach (var filePath in WorkspaceExtensions.GetVisibleFiles(path, "*"))
                    HandleFileChange(filePath, FileChangeType.Created);
            }
        }

        return Unit.Task;
    }

    private void HandleFileChange(string path, FileChangeType changeType) {
        if (path.IsSupportedProject() && changeType == FileChangeType.Changed)
            return; // Handled from IDE

        if (changeType == FileChangeType.Deleted) {
            if (path.IsSupportedDocument()) {
                workspaceService.DeleteCSharpDocument(path);
                return;
            }
            workspaceService.DeleteAdditionalDocument(path);
            workspaceService.DeleteFolder(path);
            return;
        }

        if (changeType == FileChangeType.Changed) {
            if (path.IsSupportedDocument())
                workspaceService.UpdateCSharpDocument(path);

            if (path.IsSupportedAdditionalDocument())
                workspaceService.UpdateAdditionalDocument(path);
            
            return;
        }

        if (changeType == FileChangeType.Created && File.Exists(path)) {
            if (path.IsSupportedDocument())
                workspaceService.CreateCSharpDocument(path);

            if (path.IsSupportedAdditionalDocument())
                workspaceService.CreateAdditionalDocument(path);
            
            return;
        } 
    }
}