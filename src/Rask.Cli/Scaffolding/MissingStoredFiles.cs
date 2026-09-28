using System.Formats.Tar;

namespace Rask.Cli.Scaffolding;

/// <summary>What <see cref="StoredFilesCheck.FindMissing"/> found.</summary>
/// <param name="Checked">Disk-provider rows looked at.</param>
/// <param name="Missing">Rows whose bytes are not under the root.</param>
/// <param name="Examples">The first few missing keys, for the message.</param>
internal sealed record MissingStoredFiles(int Checked, int Missing, IReadOnlyList<string> Examples);
