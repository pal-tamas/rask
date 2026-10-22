using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerBoundExample : Component
{
    private readonly Post _post = new();

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatePicker.Bind(() => _post.PublishedAt).Live().Label("Publish on"),
            Ui.Text[_post.PublishedAt is { } day ? "Publishes " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "." : "Not scheduled."]
        ];

    public sealed class Post
    {
        public DateOnly? PublishedAt { get; set; } = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);
    }
}
