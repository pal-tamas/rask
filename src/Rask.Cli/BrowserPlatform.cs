namespace Rask.Cli;

/// <summary>The platforms whose "open a URL" command differs. Explicit so every branch is testable from any host OS.</summary>
internal enum BrowserPlatform
{
    MacOS,
    Windows,
    Linux
}
