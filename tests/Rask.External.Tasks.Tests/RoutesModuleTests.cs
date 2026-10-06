using Microsoft.Build.Utilities;

namespace Rask.External.Generated
{
    /// <summary>
    ///     Stands in for the carrier the island generator emits, so this test assembly is one the task can read a
    ///     routes module out of — a real PE constant, with no compile to arrange.
    /// </summary>
    internal static class RaskExternalRoutes
    {
        public const string TypeScript = "export const Routes = {}\n";
    }
}

namespace Rask.External.Tasks.Tests
{
    // `@rask/routes` is the one generated module an island imports for its CODE, not its types. So it has to
    // be on disk where the type-checker's path mapping looks, and the bundler has to be told where that is —
    // in a production build and under the `rask dev` server alike, which share one generated config.
    public sealed class RoutesModuleTests : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("rask-external-routes").FullName;

        public void Dispose() => Directory.Delete(_root, recursive: true);

        [Fact]
        public void The_routes_module_is_written_where_the_path_mapping_resolves_it()
        {
            var task = PropTypes(typeof(RoutesModuleTests).Assembly.Location);

            Assert.True(task.Execute());

            Assert.Equal(
                Rask.External.Generated.RaskExternalRoutes.TypeScript,
                File.ReadAllText(Path.Combine(_root, "obj", "types", "routes.ts")));
            Assert.Contains(
                "\"@rask/*\": [\"./types/*\"]",
                File.ReadAllText(Path.Combine(_root, "obj", "tsconfig.paths.json")),
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_routes_module_the_assembly_no_longer_carries_is_removed()
        {
            var stale = Path.Combine(_root, "obj", "types", "routes.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
            File.WriteAllText(stale, "export const Routes = { Gone: 1 }");

            Assert.True(PropTypes(typeof(Assert).Assembly.Location).Execute());

            Assert.False(File.Exists(stale));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("http://localhost:5174")]
        public void The_bundler_resolves_the_routes_module_in_a_build_and_under_the_dev_server(string? devServerUrl)
        {
            var obj = Path.Combine(_root, "obj");
            var source = Path.Combine(_root, "Chart.tsx");
            File.WriteAllText(source, "export default () => null");
            var island = new TaskItem(source);
            island.SetMetadata("IslandName", "Chart");
            island.SetMetadata("Runtime", "react");
            var task = new WriteExternalBuildInputsTask
            {
                BuildEngine = new RecordingEngine(),
                Islands = [island],
                IntermediateDirectory = obj,
                AdapterDirectory = Path.Combine(obj, "rask"),
                OutputDirectory = Path.Combine(_root, "wwwroot"),
                ManifestPath = Path.Combine(_root, "wwwroot", "manifest.json"),
                PublicBase = "/_rask/external/",
                DevServerUrl = devServerUrl ?? string.Empty,
            };

            Assert.True(task.Execute());

            var routes = Path.Combine(obj, "types", "routes.ts").Replace('\\', '/');
            Assert.Contains(
                $"resolve: {{ alias: {{ '@rask/routes': '{routes}' }} }},",
                File.ReadAllText(task.ConfigPath),
                StringComparison.Ordinal);
        }

        private WriteExternalPropTypesTask PropTypes(string assembly)
        {
            var obj = Path.Combine(_root, "obj");
            return new WriteExternalPropTypesTask
            {
                BuildEngine = new RecordingEngine(),
                AssemblyPath = assembly,
                OutputDirectory = Path.Combine(obj, "types"),
                TsConfigPath = Path.Combine(obj, "tsconfig.paths.json"),
                ProjectDirectory = _root,
            };
        }
    }
}
