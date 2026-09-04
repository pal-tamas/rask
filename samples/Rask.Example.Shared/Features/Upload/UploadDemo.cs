using System.Globalization;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

// A self-contained file-picker demo. Input(Type: InputType.File, OnFiles: …) wires the picker to a
// typed handler; RaskFile carries the metadata while the handler is on the stack. The mutating
// handler lives in this component so its field updates re-render the right tree.
public sealed partial class UploadDemo : Component
{
    private string? _contentType;
    private DateTimeOffset _modified;
    private string? _name;
    private long _size;

    private void OnFiles(IReadOnlyList<RaskFile> files)
    {
        if (files.Count == 0)
        {
            _name = null;
            return;
        }

        var file = files[0];
        _name = file.Name;
        _size = file.Size;
        _contentType = file.ContentType;
        _modified = file.LastModified;
    }

    protected override Component? Render() =>
        Div[
            Input.Value<string>(null)
                .Id("upload-input")
                .Type(InputType.File)
                .Class($"{Tw.Input} mb-3")
                .OnFiles(OnFiles),
            _name is null
                ? (Component)Div.Class("text-ui-muted text-sm")["No file selected yet."]
                : Dl.Class("grid grid-cols-12 gap-4 text-sm mb-0")[
                    Dt.Class("col-span-4 text-ui-muted")["Name"],
                    Dd.Class("col-span-8 text-break").Data(Meta("name"))[_name],
                    Dt.Class("col-span-4 text-ui-muted")["Size"],
                    Dd.Class("col-span-8").Data(Meta("size"))[_size.ToString("N0", CultureInfo.InvariantCulture),
                        " bytes"],
                    Dt.Class("col-span-4 text-ui-muted")["Type"],
                    Dd.Class("col-span-8").Data(Meta("type"))[_contentType ?? string.Empty],
                    Dt.Class("col-span-4 text-ui-muted")["Modified"],
                    Dd.Class("col-span-8 mb-0").Data(Meta("modified"))[
                        _modified.ToString("u", CultureInfo.InvariantCulture)]
                ]
        ];

    private static new IReadOnlyDictionary<string, string?> Meta(string field) =>
        new Dictionary<string, string?> { ["rask-meta"] = field };
}
