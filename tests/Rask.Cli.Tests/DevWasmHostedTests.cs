namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask dev</c> recognising a host that serves a Rask WebAssembly app.
/// </summary>
/// <remarks>
///     Such a host references <c>Rask.Spa.Hosting</c>, the same package a TypeScript front end's host
///     does. Classified by that package alone it reads as a bundler SPA: <c>rask dev</c> then looks for an
///     npm client that is not there and never asks for the WebAssembly client's build output, so hot
///     reload never reaches the browser.
/// </remarks>
public sealed class DevWasmHostedTests
{
    [Fact]
    public void A_spa_host_referencing_a_wasm_client_is_wasm_hosted()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Rask.Spa.Hosting" Version="1.0.0"/>
                <ProjectReference Include="../App.Browser/App.Browser.csproj" ReferenceOutputAssembly="false"/>
              </ItemGroup>
            </Project>
            """);
        fs.Seed("/App.Browser/App.Browser.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.WebAssembly"><PropertyGroup><RaskWasm>true</RaskWasm></PropertyGroup></Project>
            """);

        Assert.Equal(DevTemplateKind.WasmHosted, DevTarget.Detect(fs, "/app", null)!.Kind);
    }

    [Fact]
    public void The_one_project_build_is_wasm_hosted()
    {
        // The browser half's entry point in Client/ is the convention the build keys on.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup><PackageReference Include="Rask.Spa.Hosting" Version="1.0.0"/></ItemGroup>
            </Project>
            """);
        fs.Seed("/app/Client/Program.cs", "await Rask.Wasm.WasmHostBuilder.CreateDefault().RunAsync<App>();");

        Assert.Equal(DevTemplateKind.WasmHosted, DevTarget.Detect(fs, "/app", null)!.Kind);
    }

    [Fact]
    public void Turning_the_client_off_turns_the_classification_off_too()
    {
        // <RaskClient>false</RaskClient> stops the build compiling Client/, so rask dev must not ask for a
        // build output that will never exist.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><RaskClient>false</RaskClient></PropertyGroup>
            </Project>
            """);
        fs.Seed("/app/Client/Program.cs", "// not a browser entry point any more");

        Assert.Equal(DevTemplateKind.Server, DevTarget.Detect(fs, "/app", null)!.Kind);
    }
}
