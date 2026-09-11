using System.Collections;
using Microsoft.Build.Framework;

namespace Rask.External.Tasks.Tests;

/// <summary>A build engine that keeps every error, warning and message a task logs, for assertions.</summary>
internal sealed class RecordingEngine : IBuildEngine
{
    public List<BuildErrorEventArgs> Errors { get; } = [];

    public List<BuildWarningEventArgs> Warnings { get; } = [];

    public List<string> Messages { get; } = [];

    public bool ContinueOnError => false;

    public int LineNumberOfTaskNode => 0;

    public int ColumnNumberOfTaskNode => 0;

    public string ProjectFileOfTaskNode => string.Empty;

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

    public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);

    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? string.Empty);

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public bool BuildProjectFile(
        string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
}
