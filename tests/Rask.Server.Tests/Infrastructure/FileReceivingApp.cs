using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Live;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// A file input whose OnFiles handler reads every file it is handed and returns, the way an app's
// loader page does; one whose handler never opens its files; one whose handler throws; and a form whose submit reads the file
// posted with it.
public sealed partial class FileReceivingApp : Component
{
    private readonly LoaderModel _loader = new();
    public long BytesRead;
    public int Batches;

    protected override Component? HeadAssets => Markup.Title["files"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.P[$"read={BytesRead};batches={Batches}"],
        Input.Value<string>(null).Type(InputType.File).Id("reads").OnFiles(ReadAll),
        Input.Value<string>(null).Type(InputType.File).Id("ignores").OnFiles(_ => Batches++),
        Input.Value<string>(null).Type(InputType.File).Id("throws")
            .OnFiles(_ => throw new InvalidOperationException("the provider file is malformed")),
        Form.Model(_loader).Id("loader").OnAnySubmit(Submitted)[Input.Value<string>(null).Type(InputType.File).Name("doc")]
    ];

    private Task Submitted(FormData data) => ReadAll(data.Files("doc"));

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
