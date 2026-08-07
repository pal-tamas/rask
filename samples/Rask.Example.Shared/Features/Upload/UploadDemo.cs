using System.Globalization;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

// A self-contained file-picker demo. Input(Type: InputType.File, OnFiles: …) wires the picker to a
// typed handler; RaskFile carries the metadata while the handler is on the stack. The mutating
// handler lives in this component so its field updates re-render the right tree.
public sealed class UploadDemo : Component
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
        Div()[
            Input<string>(
                Id: "upload-input",
                Type: InputType.File,
                Class: "form-control mb-3",
                OnFiles: OnFiles),
            _name is null
                ? (Component)Div(Class: "text-secondary small")["No file selected yet."]
                : Dl(Class: "row small mb-0")[
                    Dt(Class: "col-4 text-secondary")["Name"],
                    Dd(Class: "col-8 text-break", Data: Meta("name"))[_name],
                    Dt(Class: "col-4 text-secondary")["Size"],
                    Dd(Class: "col-8", Data: Meta("size"))[_size.ToString("N0", CultureInfo.InvariantCulture),
                        " bytes"],
                    Dt(Class: "col-4 text-secondary")["Type"],
                    Dd(Class: "col-8", Data: Meta("type"))[_contentType ?? string.Empty],
                    Dt(Class: "col-4 text-secondary")["Modified"],
                    Dd(Class: "col-8 mb-0", Data: Meta("modified"))[
                        _modified.ToString("u", CultureInfo.InvariantCulture)]
                ]
        ];

    private static new IReadOnlyDictionary<string, string?> Meta(string field) =>
        new Dictionary<string, string?> { ["rask-meta"] = field };
}
