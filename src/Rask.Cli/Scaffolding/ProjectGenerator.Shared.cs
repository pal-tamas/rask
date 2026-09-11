using System.Text;

namespace Rask.Cli.Scaffolding;

// Template content shared by more than one template, emitted verbatim with the Company.RaskServer
// namespace token replaced centrally (see ProjectGenerator.Materialize).
internal static partial class ProjectGenerator
{
    // The app shell every page renders through (RASK021), living in Features/Shared/ — the cross-cutting
    // bucket a new project shares across its feature slices. The welcome home page is its own Features/Home
    // slice. Styling is Tailwind, unconditionally: it is a battery like any other, so there is no axis to
    // choose along and no unstyled path to keep presentable.
    /// <summary>
    /// One catalog per language, under the <c>Resources/</c> directory <c>Rask.Core.targets</c> globs into
    /// <c>&lt;AdditionalFiles&gt;</c> — which is why this works unchanged on a browser-WASM project.
    /// </summary>
    /// <remarks>
    /// The FIRST language is the neutral one: it defines which keys exist, and its text is what a visitor
    /// sees until a translation is filled in. <paramref name="prefix"/> re-homes the whole set into a
    /// sub-project, which is what a front-end template's client needs.
    /// </remarks>
    private static IEnumerable<(string Path, string Content)> StringCatalogs(
        IReadOnlyList<string> cultures, string prefix = "")
    {
        for (var i = 0; i < cultures.Count; i++)
        {
            yield return ($"{prefix}Resources/Strings.{cultures[i]}.json", StringsCatalog(i == 0));
        }
    }

    // Two sheets, and the ORDER IS THE CONTRACT.
    //
    // A browser ranks @layer names by FIRST APPEARANCE, across every sheet on the page, in link order —
    private static string Slnx(params IReadOnlyList<string> projectPaths)
    {
        var builder = new StringBuilder("<Solution>\n");
        foreach (var path in projectPaths)
        {
            // .slnx paths are written with forward slashes on every platform.
            builder.Append("  <Project Path=\"").Append(path.Replace('\\', '/')).Append("\" />\n");
        }

        return builder.Append("</Solution>\n").ToString();
    }

    /// <summary>
    /// Every scaffolded project's <c>.gitignore</c>. Deliberately short: build output, IDE and OS noise, the
    /// files that carry secrets, and what the app itself writes — a committed <c>app.db</c> is the most
    /// common way a scaffolded repo ends up with real data in its history, and now that every battery is on
    /// by default the log store, the mail pickup directory and the snapshot directory appear the first time
    /// the app runs rather than only in an app that asked for them.
    /// </summary>
    private const string GitIgnore =
        """
        # Build output
        bin/
        obj/
        [Bb]uild/
        [Oo]ut/
        artifacts/

        # Written into the tree by the build, not by you: the compiled Tailwind sheet, the UI kit's
        # own sheet, and the daisyUI plugin Rask.Ui ships so this project can compile daisyUI itself.
        wwwroot/css/app.css
        wwwroot/css/rask-ui.css
        Styles/vendor/

        # IDE / editor
        .vs/
        .vscode/
        .idea/
        *.user
        *.suo
        *.userosscache

        # OS
        .DS_Store
        Thumbs.db

        # Local configuration and secrets — appsettings.Development.json and .env hold connection
        # strings and API keys. Keep the templates (appsettings.json, .env.example) tracked instead.
        appsettings.Development.json
        appsettings.Local.json
        .env
        !.env.example

        # The app's own database, and SQLite's write-ahead log alongside it
        *.db
        *.db-shm
        *.db-wal

        # What the batteries write beside the app. They are all on unless Program.cs says otherwise, so
        # these appear the first time it runs: queued mail with no SMTP configured is written here as
        # .eml files you can open, and the scheduled point-in-time backups land here.
        mail-pickup/
        snapshots/
        storage/

        # Publish output
        publish/
        *.nupkg

        """;

    /// <summary>
    /// Every scaffolded project's <c>.editorconfig</c>. This is the file that makes a repo's formatting a
    /// property of the repo rather than of whoever last opened it: <c>dotnet format</c>, Visual Studio,
    /// Rider and VS Code all read it, so a contributor with different defaults still produces the same diff.
    /// </summary>
    private const string EditorConfig =
        """
        # Formatting for this repository. `dotnet format` applies it; every major C# editor honours it.
        root = true

        [*]
        charset = utf-8
        end_of_line = lf
        indent_style = space
        indent_size = 2
        insert_final_newline = true
        trim_trailing_whitespace = true

        [*.{cs,csx}]
        indent_size = 4

        # Compiler diagnostics that catch real bugs, raised from suggestion to warning so they show up in
        # a normal build rather than only under an IDE lightbulb.
        dotnet_diagnostic.CA2007.severity = none
        dotnet_diagnostic.IDE0005.severity = warning

        # Language style
        csharp_style_namespace_declarations = file_scoped:warning
        csharp_using_directive_placement = outside_namespace:warning
        csharp_prefer_braces = true:warning
        csharp_style_var_for_built_in_types = false:suggestion
        csharp_style_var_when_type_is_apparent = true:suggestion
        dotnet_sort_system_directives_first = true
        dotnet_separate_import_directive_groups = false
        dotnet_style_require_accessibility_modifiers = for_non_interface_members:warning
        dotnet_style_readonly_field = true:warning
        dotnet_style_prefer_is_null_check_over_reference_equality_method = true:warning

        # Naming: interfaces start with I, types and members are PascalCase.
        dotnet_naming_rule.interfaces_start_with_i.symbols = interface_symbols
        dotnet_naming_rule.interfaces_start_with_i.style = prefixed_with_i
        dotnet_naming_rule.interfaces_start_with_i.severity = warning
        dotnet_naming_symbols.interface_symbols.applicable_kinds = interface
        dotnet_naming_style.prefixed_with_i.required_prefix = I
        dotnet_naming_style.prefixed_with_i.capitalization = pascal_case

        [*.{json,yml,yaml,xml,csproj,slnx,props,targets}]
        indent_size = 2

        [*.md]
        trim_trailing_whitespace = false

        """;
}
