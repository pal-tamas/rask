using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Live;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// A file input whose OnFiles handler reads every file it is handed and returns, the way an app's
// loader page does; one whose handler never opens its files; one whose handler takes no argument; one whose
// handler throws; a form whose submit reads the file posted with it; and a button that holds the session's
// lock until the test's gate opens.
public sealed partial class FileReceivingApp : Component
{
    private readonly FileReceivingGate _gate;
    private readonly LoaderModel _loader = new();
    public long BytesRead;
    public int Batches;

    public FileReceivingApp(FileReceivingGate gate) => _gate = gate;

    protected override Component? HeadAssets => Markup.Title["files"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.P[$"read={BytesRead};batches={Batches}"],
        Input.Value<string>(null).Type(InputType.File).Id("reads").OnFiles(ReadAll),
        Input.Value<string>(null).Type(InputType.File).Id("ignores").OnFiles(_ => Batches++),
        Input.Value<string>(null).Type(InputType.File).Id("counts").OnFiles(() => Batches++),
        Input.Value<string>(null).Type(InputType.File).Id("throws")
            .OnFiles(_ => throw new InvalidOperationException("the provider file is malformed")),
        Form.Model(_loader).Id("loader").OnAnySubmit(Submitted)[Input.Value<string>(null).Type(InputType.File).Name("doc")],
        Button.OnClick(Hold)["hold"]
    ];

    private Task Submitted(FormData data) => ReadAll(data.Files("doc"));

    private async Task Hold()
    {
        _gate.Parked.TrySetResult();
        await _gate.Open.Task.ConfigureAwait(false);
    }

    private async Task ReadAll(IReadOnlyList<IRaskFile> files)
    {
        foreach (var file in files)
        {
            var stream = file.OpenReadStream(long.MaxValue);
            await using (stream.ConfigureAwait(false))
            {
                var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer).ConfigureAwait(false);
                BytesRead += buffer.Length;
            }
        }

        Batches++;
    }

    public sealed class LoaderModel
    {
        public string? Note { get; set; }
    }
}

/// <summary>What <see cref="FileReceivingApp" />'s hold button parks on, one per host.</summary>
public sealed class FileReceivingGate
{
    public TaskCompletionSource Parked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
