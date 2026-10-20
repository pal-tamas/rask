namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastVariantsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Ui.Button.Key("success").Id("toast-success")
                .OnClick(() => Toast.Success("The post has been created successfully.").Heading("Post created"))["Success"],
            Ui.Button.Key("warning").Id("toast-warning")
                .OnClick(() => Toast.Warning("Your post has unsaved changes.").Heading("Unsaved changes"))["Warning"],
            Ui.Button.Key("danger").Id("toast-danger")
                .OnClick(() => Toast.Error("Your changes have not been saved.").Heading("Something went wrong"))["Danger"]
        ];
}
