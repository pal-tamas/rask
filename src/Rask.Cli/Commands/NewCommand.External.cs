using System.Globalization;
using Rask.Cli.Scaffolding;
using Spectre.Console;

namespace Rask.Cli.Commands;

internal sealed partial class NewCommand
{
    /// <summary>
    ///     Runs a framework's own scaffolder, or null when it succeeded.
    /// </summary>
    /// <remarks>
    ///     The missing-command case is checked before the run rather than after: an executable that is not
    ///     there surfaces as a Win32Exception with a message naming a file, which reads as a bug in the tool
    ///     rather than as "install Node.js".
    /// </remarks>
    private async Task<int?> RunExternalScaffoldAsync(
        ExternalScaffold external, string targetDirectory, CancellationToken cancellationToken)
    {
        Console.WriteLine(external.Description, ConsoleStyle.Dim);

        // The scaffolder writes INTO the target directory, which nothing has created yet on a fresh run.
        _fileSystem.CreateDirectory(targetDirectory);

        // A creator that will only take one path segment is run from inside the project directory
        // instead, with that directory created first — see ExternalScaffold.WorkingSubdirectory.
        var creatorDirectory = external.WorkingSubdirectory.Length == 0
            ? targetDirectory
            : Path.Combine(targetDirectory, external.WorkingSubdirectory);

        if (external.WorkingSubdirectory.Length != 0)
        {
            _fileSystem.CreateDirectory(creatorDirectory);
        }

        int exitCode;
        try
        {
            exitCode = await _process
                .RunAsync(external.Command, external.Arguments, creatorDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteErrorLine(
                $"'{external.Command}' is not available, and this template needs it. {external.MissingHint}",
                ConsoleStyle.Error);
            return 1;
        }

        if (exitCode != 0)
        {
            Console.WriteErrorLine(
                $"'{external.Command} {string.Join(" ", external.Arguments)}' failed (exit {exitCode.ToString(CultureInfo.InvariantCulture)}). "
                + "Nothing further was written.",
                ConsoleStyle.Error);
            return 1;
        }

        RemoveNestedRepository(creatorDirectory, external);

        return null;
    }

    /// <summary>
    ///     Deletes the repository a creator initialised inside the app it just wrote.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>create-analog</c> initialises one and offers no flag to stop it, so a scaffolded
    ///         project arrives with a second repository nested inside the one <c>rask new</c> is about
    ///         to create — or inside the repository the user already had. The outer one then treats
    ///         that directory as an EMBEDDED repository and records a gitlink for it, so none of the
    ///         front end's files are added at all: a hint is printed, the commit succeeds, and the app
    ///         is simply missing from it.
    ///     </para>
    ///     <para>
    ///         Found by committing a scaffolded sample into this repository and watching every file
    ///         under client/ fail to appear. The outer repository is the one that should own these
    ///         files, and <c>rask new</c> creates it moments later unless <c>--no-git</c> says
    ///         otherwise.
    ///     </para>
    /// </remarks>
    private void RemoveNestedRepository(string workingDirectory, ExternalScaffold external)
    {
        if (external.CreatedDirectory.Length == 0)
        {
            return;
        }

        var nested = Path.Combine(workingDirectory, external.CreatedDirectory, ".git");
        if (!_fileSystem.DirectoryExists(nested))
        {
            return;
        }

        _fileSystem.TryDeleteDirectory(nested);
        Console.WriteLine(
            $"Removed the repository {external.Command} initialised inside {external.CreatedDirectory}/ — this project's "
            + "own repository owns those files.",
            ConsoleStyle.Dim);
    }

    /// <summary>
    ///     Applies an edit to a file the scaffold did not write, reporting rather than failing when it is
    ///     not there.
    /// </summary>
    /// <remarks>
    ///     Not fatal on purpose. These amend an external scaffolder's output, and that output is not ours to
    ///     depend on the shape of — a create-vite that stops writing a .gitignore should cost a line of
    ///     advice, not a failed scaffold with a half-written project on disk.
    /// </remarks>
    private void ApplyPatch(ScaffoldPatch patch)
    {
        var relative = Path.GetRelativePath(_workingDirectory, patch.Path);
        if (!_fileSystem.FileExists(patch.Path))
        {
            Console.WriteLine($"Skipped {relative} ({patch.Description}) — it isn't there.", ConsoleStyle.Dim);
            return;
        }

        try
        {
            _fileSystem.WriteAllText(patch.Path, patch.Transform(_fileSystem.ReadAllText(patch.Path)));
            WriteCreated($"{relative} ({patch.Description})");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"Could not patch {relative} ({patch.Description}) — {ex.Message}", ConsoleStyle.Dim);
        }
    }
}
