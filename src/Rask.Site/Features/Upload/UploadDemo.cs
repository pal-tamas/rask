using System.Globalization;
using Rask.Core.Forms;

namespace Rask.Site.Features;

// A self-contained file-picker demo. A file Ui.Input's OnFiles wires the picker to a typed handler; IRaskFile carries the metadata while the handler is on the stack. The mutating
// handler lives in this component so its field updates re-render the right tree.
public sealed partial class UploadDemo : Component
{
    private string? _contentType;
    private DateTimeOffset _modified;
    private string? _name;
    private long _size;

    private void OnFiles(IReadOnlyList<IRaskFile> files)
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
            Ui.Input.Of<string>()
                .Type(InputType.File)
                .Label("File")
                .Id("upload-input")
                .Class("mb-3")
                .OnFiles(OnFiles),
            _name is null
                ? Div.Class("text-ui-muted text-sm")["No file selected yet."]
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

    private static new AttrBag Meta(string field) => new("rask-meta", field);
}
