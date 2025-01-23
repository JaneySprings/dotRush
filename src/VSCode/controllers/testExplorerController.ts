import { DotNetTaskProvider } from '../providers/dotnetTaskProvider';
import { TestExtensions } from '../models/test';
import { Interop } from '../interop/interop';
import { Extensions } from '../extensions';
import * as res from '../resources/constants'
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

export class TestExplorerController {
    private static controller: vscode.TestController;
    private static testsResultDirectory: string;

    public static activate(context: vscode.ExtensionContext) {
        TestExplorerController.testsResultDirectory = path.join(context.extensionPath, "extension", "bin", "TestExplorer");

        TestExplorerController.controller = vscode.tests.createTestController(res.testExplorerViewId, res.testExplorerViewTitle);
        TestExplorerController.controller.refreshHandler = TestExplorerController.refreshTests;
        
        context.subscriptions.push(TestExplorerController.controller);
        context.subscriptions.push(TestExplorerController.controller.createRunProfile('Run NUnit/XUnit Tests', vscode.TestRunProfileKind.Run, TestExplorerController.runTests, true));
        context.subscriptions.push(TestExplorerController.controller.createRunProfile('Debug NUnit/XUnit Tests', vscode.TestRunProfileKind.Debug, TestExplorerController.debugTests, true));
        TestExplorerController.refreshTests();
    }

    private static async refreshTests(): Promise<void> {
        TestExplorerController.controller.items.replace([]);

        const projectFiles = await Extensions.getTestProjectFiles();
        return Extensions.parallelForEach(projectFiles, async (projectFile) => {
            const projectName = path.basename(projectFile, '.csproj');
            const discoveredTests = await Interop.getTests(projectFile);
            if (discoveredTests.length === 0)
                return;

            const root = TestExplorerController.controller.createTestItem(projectName, projectName, vscode.Uri.file(projectFile));
            root.children.replace(discoveredTests.map(t => TestExtensions.toTestItem(t, TestExplorerController.controller)));
            TestExplorerController.controller.items.add(root);
        });
    }
    private static async runTests(request: vscode.TestRunRequest, token: vscode.CancellationToken): Promise<void> {
        TestExplorerController.convertTestRequest(request).forEach(async(filters, project) => {
            const testReport = TestExplorerController.getTestReportPath(project);
            const testArguments: string[] = [ '--logger',`'trx;LogFileName=${testReport}'` ];
            if (filters.length > 0) {
                testArguments.push('--filter');
                testArguments.push(`'${filters.join('|')}'`);
            }
            
            const testRun = TestExplorerController.controller.createTestRun(request);
            const execution = await vscode.tasks.executeTask(DotNetTaskProvider.getTestTask(project.uri!.fsPath, testArguments));
            await new Promise<boolean>((resolve) => vscode.tasks.onDidEndTaskProcess(e => {
                if (e.execution.task === execution.task)
                    resolve(e.exitCode === 0);
            }));

            await TestExplorerController.pushTestResults(testRun, project, testReport);
        });
    }
    private static async debugTests(request: vscode.TestRunRequest, token: vscode.CancellationToken): Promise<void> {
        TestExplorerController.convertTestRequest(request).forEach(async(filters, project) => {
            const execution = await vscode.tasks.executeTask(DotNetTaskProvider.getBuildTask(project.uri!.fsPath));
            const executionExitCode = await new Promise<number>((resolve) => vscode.tasks.onDidEndTaskProcess(e => {
                if (e.execution.task === execution.task)
                    resolve(e.exitCode ?? -1);
            }));

            if (executionExitCode !== 0 || token.isCancellationRequested)
                return;
            
            await vscode.debug.startDebugging(TestExplorerController.getWorkspaceFolder(), {
                name: request.profile?.label ?? 'Debug Tests',
                type: res.debuggerVsdbgId,
                request: 'attach',
                processId: await Interop.runTestHost(project.uri!.fsPath, filters.join('|'))
            });
        });
    }

    private static convertTestRequest(request: vscode.TestRunRequest) : Map<vscode.TestItem, string[]> {
        const testItems = new Map<vscode.TestItem, string[]>();
        const getRootNode = (item: vscode.TestItem) : vscode.TestItem => {
            if (item.parent === undefined)
                return item;
            return getRootNode(item.parent);
        }

        if (request.include === undefined) {
            TestExplorerController.controller.items.forEach(item => testItems.set(item, []));
            return testItems;
        }

        request.include?.forEach(item => {
            const rootNode = getRootNode(item);
            if (!testItems.has(rootNode))
                testItems.set(rootNode, []);
            if (item.id !== rootNode.id)
                testItems.get(rootNode)?.push(item.id);
        });

        return testItems;
    }
    private static pushTestResults(testRun: vscode.TestRun, project: vscode.TestItem, testReport: string): Promise<void> {
        const getAllChildren = (root: vscode.TestItem) => {
            const result = new Map<string, vscode.TestItem>();
            const pushNodes = (node: vscode.TestItemCollection) => node.forEach(n => {
                result.set(n.id, n);
                pushNodes(n.children);
            });
            pushNodes(root.children);
            return result;
        }
        
        const testNodes = getAllChildren(project);
        return Interop.getTestResults(testReport).then(testResults => {
            testResults.forEach(result => {
                const duration = TestExtensions.toDurationNumber(result.duration);
                const testItem = testNodes.get(result.fullName);
                if (testItem === undefined)
                    return;

                if (result.state === 'Passed')
                    testRun.passed(testItem, duration);
                else if (result.state === 'Failed')
                    testRun.failed(testItem, TestExtensions.toTestMessage(result), duration);
                else
                    testRun.skipped(testItem);
            });
            testRun.end();
        });
    }
    public static getWorkspaceFolder() : vscode.WorkspaceFolder | undefined {
        if (vscode.workspace.workspaceFolders === undefined)
            return undefined;
        if (vscode.workspace.workspaceFolders.length === 1)
            return vscode.workspace.workspaceFolders[0];

        return undefined;
    }
    public static getTestReportPath(project: vscode.TestItem): string {
        const testReport = path.join(TestExplorerController.testsResultDirectory, `${project.label}.trx`);
        if (fs.existsSync(testReport))
            vscode.workspace.fs.delete(vscode.Uri.file(testReport));

        return testReport;
    }
}