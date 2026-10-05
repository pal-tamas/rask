using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>
///     The <c>--json</c> surface: the machine-readable form of the commands worth scripting.
/// </summary>
/// <remarks>
///     <para>
///         Declared in one place so the commands that offer it cannot drift on the flag's spelling,
///         its help text, or its serialization settings — the same reasoning as the shared argument
///         schema. <see cref="Flag" /> is what a command adds; <see cref="Write" /> is how it emits.
///     </para>
///     <para>
///         Source-generated contexts, matching the rest of the CLI (<c>DeployConfig</c>,
///         <c>GenerateConfig</c>): reflection-based serialization would be a trimming hazard in a tool
///         that ships as a self-contained binary, and the shapes here are fixed and few.
///     </para>
///     <para>
///         Output goes to <see cref="IConsole.Out" /> unstyled and unindented-by-nothing-else: a
///         <c>--json</c> run prints the document and nothing else, so <c>rask info --json | jq</c> works
///         without filtering banners out. Errors still go to stderr, so a failed run is distinguishable
///         by exit code and stream rather than by parsing.
///     </para>
/// </remarks>
internal static class JsonOutput
{
    /// <summary>The flag every <c>--json</c>-capable command declares, worded once.</summary>
    public static ArgumentSchema WithJson(this ArgumentSchema schema) =>
        schema.Flag("json", description: "Print the result as JSON instead of a human-readable report.");

    /// <summary>
    ///     The rejection for <c>--json</c> on a command whose only document is its dry-run plan. Refused
    ///     rather than ignored: a script that asked for JSON and got a scaffolded project, or a running
    ///     dev server, has no way to tell what happened from the output it was waiting to parse.
    /// </summary>
    public static string DryRunOnly(string command) =>
        $"--json only applies to `rask {command} --dry-run`.";

    public static void Write<T>(IConsole console, T value, JsonTypeInfo<T> typeInfo) =>
        console.Out.WriteLine(JsonSerializer.Serialize(value, typeInfo));
}
