using System.Diagnostics;

namespace Rask.Wasm.Hosting.Tests;

/// <summary>
///     A host bakes its WASM client's publish directory into its own assembly, and that path names the
///     client's target framework. These assert where the framework is read from.
/// </summary>
/// <remarks>
///     <para>
///         Only the probe, driven through MSBuild against fixture projects — publishing a real client
///         links a WebAssembly runtime and takes minutes, which does not belong in the unit gate.
///     </para>
///     <para>
///         The probe is where the failure was. It read a literal <c>&lt;TargetFramework&gt;</c> element
///         off the client's csproj and assumed <c>net10.0-browser</c> for anything else, so a client on
///         another .NET version — or one whose framework comes from a property or a
///         <c>Directory.Build.props</c> — got a bundle path no publish writes, and the host answered with
///         404s from a green build.
///     </para>
/// </remarks>
public sealed class WasmBundleDirProbeTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "rask-bundle-probe-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public void TheBundleDirNamesTheClientsDeclaredFramework()
    {
        WriteClient("<TargetFramework>net10.0-browser</TargetFramework>");

        Assert.EndsWith("/bin/Debug/net10.0-browser/publish/wwwroot", Probe(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBundleDirNamesAFrameworkTheClientInheritsRatherThanDeclares()
    {
        // The case the old probe could not see: no <TargetFramework> element in the csproj at all. The
        // version is deliberately not the one it used to assume, so its fallback cannot pass this.
        WriteClient(string.Empty, inheritedFramework: "net11.0-browser");

        Assert.EndsWith("/bin/Debug/net11.0-browser/publish/wwwroot", Probe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AClientWithSeveralFrameworksIsRefusedRatherThanGuessed()
    {
        // A bundle is published for ONE framework. Picking one of several would bake a path that is right
        // or wrong depending on which the publish happened to build.
        WriteClient("<TargetFrameworks>net10.0;net10.0-browser</TargetFrameworks>");

        var (exit, output) = Run();

        Assert.True(exit != 0, $"a client with two frameworks must fail the probe:\n{output}");
        Assert.Contains("must build for exactly one framework", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientWithOneFrameworkWrittenAsAListIsServed()
    {
        // One framework spelled as a list is still one framework. MSBuild reports HasSingleTargetFramework
        // false for any <TargetFrameworks> project, so a probe that trusted the flag refused this client —
        // one the old XmlPeek fallback had served correctly — over a problem that was not there.
        WriteClient("<TargetFrameworks>net10.0-browser</TargetFrameworks>");

        Assert.EndsWith("/bin/Debug/net10.0-browser/publish/wwwroot", Probe(), StringComparison.Ordinal);
    }

    private void WriteClient(string frameworkElement, string? inheritedFramework = null)
    {
        var client = Directory.CreateDirectory(Path.Combine(_dir, "Client")).FullName;
        File.WriteAllText(Path.Combine(client, "Client.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                {frameworkElement}
                <RaskWasm>true</RaskWasm>
              </PropertyGroup>
            </Project>
            """);

        if (inheritedFramework is not null)
        {
            File.WriteAllText(Path.Combine(client, "Directory.Build.props"), $"""
                <Project>
                  <PropertyGroup>
                    <TargetFramework>{inheritedFramework}</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
        }

        var host = Directory.CreateDirectory(Path.Combine(_dir, "Host")).FullName;
        File.WriteAllText(Path.Combine(host, "Host.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Client/Client.csproj"
                                  ReferenceOutputAssembly="false"
                                  SkipGetTargetFrameworkProperties="true"/>
              </ItemGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Wasm.Hosting", "build", "Rask.Wasm.Hosting.targets")}"/>
            </Project>
            """);
    }

    // The baked bundle directory, with separators normalized so the assertion reads the same everywhere.
    private string Probe()
    {
        var (exit, output) = Run("-getProperty:_RaskWasmAppBundleDir");

        Assert.True(exit == 0, $"the bundle-dir probe failed:\n{output}");
        return output.Trim().Replace('\\', '/');
    }

    private (int Exit, string Output) Run(params string[] extra)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(_dir, "Host"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add("Host.csproj");
        psi.ArgumentList.Add("-t:_RaskComputeWasmBundleDir");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-nodeReuse:false");
        foreach (var argument in extra)
        {
            psi.ArgumentList.Add(argument);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        return (p.ExitCode, stdout + stderr);
    }

    private static string SrcDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Rask.Wasm.Hosting")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!, "src");
        }
    }
}
