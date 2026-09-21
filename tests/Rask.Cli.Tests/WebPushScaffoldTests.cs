using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The development VAPID pair <c>rask new</c> mints for a push app.
/// </summary>
/// <remarks>
///     <para>
///         It used to print two <c>dotnet user-secrets set</c> commands and leave the reader to it. Neither
///         command worked: no scaffolded csproj carries a <c>UserSecretsId</c>, so both failed with "Could
///         not find the global property 'UserSecretsId'". A scaffold that already runs code while writing
///         the app's files can just mint the pair, which is what it does now.
///     </para>
///     <para>
///         The assertions are about the key MATERIAL rather than the file's text, because the failure that
///         matters is a pair that looks present and is not usable — a placeholder left in, a constant baked
///         into the template, a truncated coordinate. Each of those reads fine and fails at the push
///         service, which is the worst place to find out.
///     </para>
/// </remarks>
public sealed class WebPushScaffoldTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    private static Dictionary<string, string> Generate(params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    /// <summary>The pair the generated file carries, read the way the configuration binder reads it.</summary>
    private static (string PublicKey, string PrivateKey) Pair(IReadOnlyDictionary<string, string> files)
    {
        var text = files[WebPushAssembly.DevelopmentSettingsFile];

        // Parsed rather than pattern-matched: this is the one scaffolded file whose value is consumed by a
        // machine, so "did it come out as valid JSON with comments in it" is part of what is under test.
        var node = JsonNode.Parse(
            text,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            })!;

        var keys = node["Rask"]!["WebPush"]!["VapidKeys"]!;

        return ((string)keys["PublicKey"]!, (string)keys["PrivateKey"]!);
    }

    [Fact]
    public void A_push_app_gets_a_development_key_pair()
    {
        var files = Generate("push");

        Assert.True(files.ContainsKey(WebPushAssembly.DevelopmentSettingsFile));
    }

    [Fact]
    public void Only_the_key_file_is_marked_secret()
    {
        var files = ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(["push"]), Version).Files;

        var secret = Assert.Single(files, f => f.Secret);
        Assert.Equal(WebPushAssembly.DevelopmentSettingsFile, Path.GetFileName(secret.Path));
    }

    [Fact]
    public void A_secret_reaches_disk_readable_by_its_owner_alone()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("rask-secret-");
        try
        {
            var path = Path.Combine(directory.FullName, WebPushAssembly.DevelopmentSettingsFile);
            var fs = new SystemFileSystem();

            fs.WriteSecretText(path, "{}");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));

            // A file that already existed keeps the mode it was created with unless it is narrowed too.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            fs.WriteSecretText(path, "{\"a\":1}");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal("{\"a\":1}", File.ReadAllText(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_generated_pair_is_a_real_P256_key_pair()
    {
        (var publicKey, var privateKey) = Pair(Generate("push"));

        var point = Base64Url.DecodeFromChars(publicKey);
        var scalar = Base64Url.DecodeFromChars(privateKey);

        Assert.Equal(65, point.Length);
        Assert.Equal(0x04, point[0]); // The uncompressed-point marker the browser requires.
        Assert.Equal(32, scalar.Length);

        // Validate() throws if the public point and the private scalar are not halves of one pair — the
        // check that a truncated or mismatched coordinate would fail and a length check would not.
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..65] },
            D = scalar,
        };

        parameters.Validate();
        using var ecdsa = ECDsa.Create(parameters);
        Assert.NotNull(ecdsa);
    }

    [Fact]
    public void Every_scaffold_gets_its_own_pair()
    {
        // The failure this rules out is a pair committed to the template or cached across calls, which
        // would hand every app on earth the same signing key — a key that is public is not a key.
        (var firstPublic, var firstPrivate) = Pair(Generate("push"));
        (var secondPublic, var secondPrivate) = Pair(Generate("push"));

        Assert.NotEqual(firstPublic, secondPublic);
        Assert.NotEqual(firstPrivate, secondPrivate);
    }

    [Fact]
    public void The_public_key_is_padding_free_base64url()
    {
        // It is handed to pushManager.subscribe as-is; '+', '/' and '=' decode to the wrong bytes there.
        (var publicKey, _) = Pair(Generate("push"));

        Assert.DoesNotContain('+', publicKey);
        Assert.DoesNotContain('/', publicKey);
        Assert.DoesNotContain('=', publicKey);
    }

    [Fact]
    public void The_private_key_never_reaches_a_committed_file()
    {
        // The whole point of the gitignored file. Asserted against every other file the scaffold writes,
        // rather than against appsettings.json alone, so a future template that echoes the pair somewhere
        // else — a client config, a README, a deploy manifest — fails here.
        var files = Generate("push");
        (_, var privateKey) = Pair(files);

        var leaks = files
            .Where(f => !string.Equals(f.Key, WebPushAssembly.DevelopmentSettingsFile, StringComparison.Ordinal))
            .Where(f => f.Value.Contains(privateKey, StringComparison.Ordinal))
            .Select(f => f.Key)
            .ToList();

        Assert.True(leaks.Count == 0, "the private key was written to: " + string.Join(", ", leaks));
    }

    [Fact]
    public void The_settings_the_app_commits_still_carry_the_contact_subject()
    {
        // The half that stays tracked. Guards the cheap way to pass the test above — moving the whole
        // WebPush section out of appsettings.json — which would leave the sender with no contact address.
        var files = Generate("push");

        Assert.Contains("\"Subject\"", files["appsettings.json"], StringComparison.Ordinal);
        Assert.Contains("appsettings.Development.json", files["appsettings.json"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_real_configuration_reader_finds_the_keys_the_app_looks_for()
    {
        // The assertion that actually decides whether push works. Everything above reads the file as
        // JSON; this reads it the way the app does — through the JSON configuration provider, at the
        // exact key paths the template's Program.cs and RaskBatteryWiring test before they register the
        // sender. A file that parses but nests the keys one level off would pass every test above, wire
        // nothing, and look like a Web Push bug rather than a scaffolding one.
        var files = Generate("push");

        using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(files[WebPushAssembly.DevelopmentSettingsFile]));

        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        (var publicKey, var privateKey) = Pair(files);

        Assert.Equal(publicKey, configuration["Rask:WebPush:VapidKeys:PublicKey"]);
        Assert.Equal(privateKey, configuration["Rask:WebPush:VapidKeys:PrivateKey"]);
    }

    [Fact]
    public void The_key_file_is_kept_out_of_the_docker_build_context()
    {
        // Gitignoring the key file does not keep it out of the IMAGE: .dockerignore is a separate list
        // and Docker never reads .gitignore. The Dockerfile's build stage runs `COPY . .`, the Web SDK
        // copies every appsettings*.json into the publish output, and the final stage copies that output
        // — so without an entry here, `rask deploy` builds the developer's VAPID private key into the
        // image it pushes. Verified against a real `dotnet publish`, which does copy the file.
        var result = ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(["push", "docker"]), Version);

        var dockerignore = result.Files.SingleOrDefault(f =>
            Path.GetFileName(f.Path).Equals(".dockerignore", StringComparison.Ordinal));

        Assert.NotNull(dockerignore);
        Assert.Contains(WebPushAssembly.DevelopmentSettingsFile, dockerignore.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_without_push_gets_no_key_file_and_no_push_section()
    {
        var files = Generate();

        Assert.False(files.ContainsKey(WebPushAssembly.DevelopmentSettingsFile));
        Assert.DoesNotContain("\"WebPush\"", files["appsettings.json"], StringComparison.Ordinal);
    }
}
