// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.
// dotnet/sdk/src/BuiltInTools/dotnet-format/Utilities/ProcessRunner.cs

using System.Diagnostics;

namespace DotRush.Roslyn.Common.External;

public readonly struct ProcessInfo {
    public Process Process { get; }
    public ProcessStartInfo StartInfo { get; }
    public Task<ProcessResult> Result { get; }

    public int Id => Process.Id;

    public ProcessInfo(Process process, ProcessStartInfo startInfo, Task<ProcessResult> result) {
        Process = process;
        StartInfo = startInfo;
        Result = result;
    }
}