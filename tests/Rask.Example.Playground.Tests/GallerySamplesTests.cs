using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Playground;
using Rask.Example.Playground.Compiler;

namespace Rask.Example.Playground.Tests;

// Guards the shipped example gallery: every curated snippet must compile and produce a live component under
// the same reference set the browser ships (BCL + Rask.Core). A broken sample would greet a visitor with red
// squiggles on load — this catches it at build time on the desktop runtime.
public sealed class GallerySamplesTests
{
    public static TheoryData<string> SampleIds()
    {
        var data = new TheoryData<string>();
        foreach (var sample in PlaygroundSamples.All)
        {
            data.Add(sample.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SampleIds))]
    public async Task Gallery_sample_compiles_and_renders(string id)
    {
        var sample = PlaygroundSamples.All.Single(s => s.Id == id);
        var compiler = new PlaygroundCompiler(TestReferences.Build(), new ServiceCollection().BuildServiceProvider());

        var result = await compiler.CompileAsync(sample.Code);

        Assert.True(
            result.Succeeded,
            $"'{sample.Title}' failed to compile:\n" + string.Join("\n",
                result.Diagnostics
                    .Where(d => d.Severity == PlaygroundSeverity.Error)
                    .Select(d => $"  {d.Id} ({d.StartLine},{d.StartColumn}): {d.Message}")));
        Assert.NotNull(result.Component);
    }

    [Fact]
    public void Gallery_is_non_empty_and_starter_is_the_first_sample()
    {
        Assert.NotEmpty(PlaygroundSamples.All);
        Assert.Equal(PlaygroundSamples.All[0].Code, PlaygroundSamples.Starter);
    }
}
