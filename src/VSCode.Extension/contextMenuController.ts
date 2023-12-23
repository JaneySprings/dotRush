import { DotNetTaskProvider } from './tasks/dotnetTaskProvider';
import { ProjectItem } from './models/projectItem';
import * as res from './resources/constants';
import * as vscode from 'vscode';

export class ContextMenuController {
    public static activate(context: vscode.ExtensionContext) {
        context.subscriptions.push(vscode.commands.registerCommand(res.taskCommandBuildId, async (path: vscode.Uri) => {
            const projectFile = await ContextMenuController.selectProjectFile(path);
            if (projectFile === undefined)
                return;
            vscode.tasks.executeTask(await DotNetTaskProvider.getBuildTask(projectFile, '--no-restore'));
        }));
        context.subscriptions.push(vscode.commands.registerCommand(res.taskCommandRestoreId, async (path: vscode.Uri) => {
            const projectFile = await ContextMenuController.selectProjectFile(path);
            if (projectFile === undefined)
                return;
            vscode.tasks.executeTask(await DotNetTaskProvider.getRestoreTask(projectFile));
        }));
        context.subscriptions.push(vscode.commands.registerCommand(res.taskCommandCleanId, async (path: vscode.Uri) => {
            const projectFile = await ContextMenuController.selectProjectFile(path);
            if (projectFile === undefined)
                return;
            vscode.tasks.executeTask(await DotNetTaskProvider.getCleanTask(projectFile));
        }));
    }

    private static async selectProjectFile(targetUri: vscode.Uri): Promise<vscode.Uri | undefined> {
        if (targetUri.fsPath.endsWith(".csproj"))
            return targetUri;

        const files = await vscode.workspace.findFiles(new vscode.RelativePattern(targetUri, '**/*.csproj'));
        if (files.length === 0) {
            vscode.window.showErrorMessage(res.messaeNoProjectFileFound);
            return undefined;
        }
        if (files.length === 1)
            return files[0];

        const selectedItem = await vscode.window.showQuickPick(files.map(it => new ProjectItem(it)), { placeHolder: res.messageSelectProjectFile });
        return selectedItem?.uri;
    }
}