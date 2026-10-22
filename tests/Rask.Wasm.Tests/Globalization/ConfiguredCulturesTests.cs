using System.Reflection;
using System.Reflection.Emit;
using Rask.Core.Globalization;

namespace Rask.Wasm.Tests.Globalization;

// The build bakes Rask:Cultures into the app's assembly; this is the half that reads it back.
public class ConfiguredCulturesTests
{
    [Fact]
    public void The_languages_the_build_baked_are_the_apps_in_the_order_the_file_lists_them()
    {
        var app = AppWith("hu, en ,de");
        var options = new RaskCultureOptions();

        ConfiguredCultures.AddTo(options, app);

        Assert.Equal(["hu", "en", "de"], options.SupportedCultures);
    }

    [Fact]
    public void A_language_already_named_is_not_named_twice()
    {
        var app = AppWith("en,hu");
        var options = new RaskCultureOptions { SupportedCultures = { "HU" } };

        ConfiguredCultures.AddTo(options, app);

        Assert.Equal(["HU", "en"], options.SupportedCultures);
    }

    [Fact]
    public void An_app_built_without_the_section_names_nothing()
    {
        var options = new RaskCultureOptions();

        ConfiguredCultures.AddTo(options, typeof(ConfiguredCulturesTests).Assembly);

        Assert.Empty(options.SupportedCultures);
    }

    private static AssemblyBuilder AppWith(string cultures) =>
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("App" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run,
            [
                new CustomAttributeBuilder(
                    typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                    [ConfiguredCultures.MetadataKey, cultures]),
            ]);
}
