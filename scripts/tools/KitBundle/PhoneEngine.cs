using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KitBundle;

/// <summary>
///     Ui.Phone's engine: <c>ui-phone.ts</c> and intl-tel-input into <c>Resources/ui-phone.js</c>, the library's own
///     utils (Google's libphonenumber, compiled by its authors) beside it as <c>ui-phone-utils.js</c>, their
///     notices, the versions <c>UiPhoneEngine</c> carries, and <c>UiPhoneCountries.g.cs</c> — each country's calling
///     code and English name, so a page is drawn with the right country before the engine has loaded, and without
///     script at all.
/// </summary>
/// <remarks>
///     intl-tel-input is pinned to the release fluxui.dev serves (read off its phone.min.js), not npm's latest:
///     Ui.Phone's formatting is held to Flux's key by key. The English names are Unicode CLDR's
///     (<c>cldr-localenames-full</c>, bundle.json's data), which is what a browser's <c>Intl.DisplayNames</c> answers.
/// </remarks>
internal sealed partial class PhoneEngine : Engine
{
    private const string Library = "intl-tel-input";
    private const string Names = "cldr-localenames-full";

    internal override string Name => "phone";

    internal override IReadOnlyList<string> Bundles => ["ui-phone.js", "ui-phone-utils.js"];

    protected override string Entry => "ui-phone.ts";

    /// <summary>The intl-tel-input release fluxui.dev's phone field runs: its phone.min.js names it, once.</summary>
    internal override async Task<string?> Follows(string dependency)
    {
        if (!string.Equals(dependency, Library, StringComparison.Ordinal))
        {
            return null;
        }

        var page = Encoding.UTF8.GetString(await Upstream.Get("https://fluxui.dev/components/phone"));
        var script = FluxScript().Match(page) is { Success: true } found
            ? found.Value
            : throw new KitBundleException("fluxui.dev/components/phone loads no /flux/phone.min.js");
        var code = Encoding.UTF8.GetString(await Upstream.Get("https://fluxui.dev" + script));
        var versions = FluxVersion().Matches(code).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return code.Contains(Library, StringComparison.Ordinal) && versions.Count == 1
            ? versions.Single()
            : throw new KitBundleException($"fluxui.dev{script} does not name one {Library} release");
    }

    protected override async Task Bundle(string tree, IReadOnlyList<LockedPackage> locked)
    {
        var library = Path.Combine(tree, "node_modules", Library);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(library, "package.json")));
        var release = manifest.RootElement.GetProperty("version").GetString()!;
        var license = manifest.RootElement.GetProperty("license").ToString();

        // The utils, as the library ships them: the file fluxui.dev serves is this one.
        var utils = await File.ReadAllBytesAsync(Path.Combine(library, "dist", "js", "utils.js"));
        await File.WriteAllBytesAsync(Path.Combine(Repository.Resources, "ui-phone-utils.js"), utils);
        var utilsVersion = Hash(utils);

        var outfile = Path.Combine(Repository.Resources, "ui-phone.js");
        var banner = $"/*! Rask.Ui phone engine. Bundles {Library} {release} (MIT) — see rask-ui-phone.LICENSES.txt, beside this file. */";
        await Esbuild.Run(tree, [.. Options(outfile, banner), $"--define:UTILS=\"./rask-ui-phone-utils.js?v={utilsVersion}\""]);
        var version = Hash(await File.ReadAllBytesAsync(outfile));

        var notices = "Third-party notices of Ui.Phone.\n\n"
            + "The phone engine (rask-ui-phone.js) bundles intl-tel-input. Its utils (rask-ui-phone-utils.js) are that\n"
            + "release's own build of Google's libphonenumber and of the Closure Library code it is compiled with, which\n"
            + "are under the Apache License 2.0; their copyright lines are kept at the head of each part in that file.\n"
            + Section($"{Library} {release} — {license}", await File.ReadAllTextAsync(Path.Combine(library, "LICENSE")))
            + Section("libphonenumber (Copyright (C) 2010 The Libphonenumber Authors) and the Closure Library\n"
                + "(Copyright The Closure Library Authors), in rask-ui-phone-utils.js — Apache-2.0",
                await File.ReadAllTextAsync(Path.Combine(Directory, "notices", "apache-2.0.txt")));
        await File.WriteAllTextAsync(Path.Combine(Repository.Resources, "ui-phone.LICENSES.txt"), notices);

        var engine = Path.Combine(Repository.Kit, "UiPhoneEngine.cs");
        await File.WriteAllTextAsync(
            engine,
            WithConstant(WithConstant(await File.ReadAllTextAsync(engine), "Version", version), "UtilsVersion", utilsVersion));

        var names = locked.Single(p => string.Equals(p.Path, BundleLock.DataPrefix + Names, StringComparison.Ordinal));
        var countries = Countries(await File.ReadAllTextAsync(Path.Combine(library, "dist", "js", "data.mjs")), await EnglishNames(names));
        await File.WriteAllTextAsync(Path.Combine(Repository.Kit, "UiPhoneCountries.g.cs"), CountryTable(countries, release, names.Version));
        Console.WriteLine($"ui-phone.js: version {version}; ui-phone-utils.js: version {utilsVersion}; {countries.Count} countries");
    }

    // Every country the library knows, its calling code and its English name, in English alphabetical order. The
    // library's data module is a literal array of [iso2, dialCode, priority, areaCodes, nationalPrefix] rows.
    private static List<(string Iso2, string Dial, string Name)> Countries(string module, Dictionary<string, string> english)
    {
        var start = module.IndexOf("var rawCountryData = ", StringComparison.Ordinal);
        var end = module.IndexOf("\n];", start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new KitBundleException($"{Library}'s dist/js/data.mjs holds no rawCountryData array: its layout moved");
        }

        var literal = module[(start + "var rawCountryData = ".Length)..(end + 2)];
        using var rows = JsonDocument.Parse(literal, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return
        [
            .. rows.RootElement.EnumerateArray()
                .Select(row => (Iso2: row[0].GetString()!.ToUpperInvariant(), Dial: row[1].GetString()!))
                .Select(row => (row.Iso2, row.Dial, Name: english.GetValueOrDefault(row.Iso2, row.Iso2)))
                .OrderBy(row => row.Name, English),
        ];
    }

    // CLDR's English territory names, as Intl.DisplayNames('en', { type: 'region' }) answers them.
    private static async Task<Dictionary<string, string>> EnglishNames(LockedPackage names)
    {
        var territories = Installer.Files(await Installer.Fetch(names)).FirstOrDefault(f => f.Path == "main/en/territories.json").Bytes
                          ?? throw new KitBundleException($"{Names} {names.Version} holds no main/en/territories.json");
        using var json = JsonDocument.Parse(territories);
        return json.RootElement.GetProperty("main").GetProperty("en").GetProperty("localeDisplayNames").GetProperty("territories")
            .EnumerateObject()
            .ToDictionary(t => t.Name, t => t.Value.GetString()!, StringComparer.Ordinal);
    }

    private static string CountryTable(List<(string Iso2, string Dial, string Name)> countries, string release, string cldr)
    {
        static string Literal(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        var table = string.Join("\n", countries.Select(c => $"        new({Literal(c.Iso2)}, {Literal(c.Dial)}, {Literal(c.Name)}),"));
        return $$"""
            // <auto-generated>
            // Generated by scripts/tools/KitBundle from intl-tel-input {{release}} (MIT) and the English region names
            // of Unicode CLDR {{cldr}} ({{Names}}). Do not edit: change the tool and run it.
            // </auto-generated>

            #nullable enable

            namespace Rask;

            /// <summary>Every country Ui.Phone knows: its calling code and its English name, in English alphabetical order.</summary>
            internal static partial class UiPhoneCountries
            {
                /// <summary>The countries, {{countries.Count}} of them.</summary>
                internal static readonly UiPhoneCountry[] All =
                [
            {{table}}
                ];
            }

            """;
    }

    [GeneratedRegex(@"/flux/phone\.min\.js\?id=[\w-]+")]
    private static partial Regex FluxScript();

    [GeneratedRegex(@"\bversion:""(\d+\.\d+\.\d+)""")]
    private static partial Regex FluxVersion();
}
