﻿using System.Diagnostics;
using dotRush.Server.Services;

namespace dotRush.Server;

public class Program {
    public static async Task Main(string[] args) {
        await ConfigureServices(args[1], args.Skip(2).ToArray());

        var server = new ServerSession(Console.OpenStandardInput(), Console.OpenStandardOutput());
        var ideProcess = Process.GetProcessById(int.Parse(args[0]));

        ideProcess.EnableRaisingEvents = true;
        ideProcess.Exited += (sender, e) => Environment.Exit(0);
        
        await server.Listen();
    }

    private static async Task ConfigureServices(string framework, string[] targets) {
        CompilationService.Initialize();
        DocumentService.Initialize();

        await SolutionService.Initialize(framework, targets);
    }
}