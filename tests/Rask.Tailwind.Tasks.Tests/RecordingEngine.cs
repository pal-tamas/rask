using System.Collections;
using Microsoft.Build.Framework;

namespace Rask.Tailwind.Tasks.Tests;

/// <summary>A build engine that keeps what a task logged, so a test can read it back.</summary>
internal sealed class RecordingEngine : IBuildEngine
{
    public List<string> Errors { get; } = [];

    public List<string> Messages { get; } = [];

    public bool ContinueOnError => false;

    public int LineNumberOfTaskNode => 0;

    public int ColumnNumberOfTaskNode => 0;

    public string ProjectFileOfTaskNode => "test.csproj";

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);

    public void LogWarningEvent(BuildWarningEventArgs e) => Messages.Add(e.Message ?? string.Empty);

    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? string.Empty);

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public bool BuildProjectFile(
        string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => false;
}
