using Rask.Core.Routing;
using Rask.Mail;

namespace Rask.Example.EfCore.Features.Mail.SendMail;

// Vertical slice: queue a transactional email. IMail writes one QueuedMail row on the same SQLite
// database the catalog uses; the background MailProcessor delivers it just after — here to a pickup
// directory as an .eml file (no SMTP server needed), so the send returns instantly and the user sees a
// confirmation while delivery happens off the request thread.
[Route("mail")]
public sealed partial class SendMailPage(IMail mail) : Component
{
    private readonly SendMailForm _form = new();
    private string? _queuedFor;

    protected override Component? HeadAssets => Title["Send email — Rask EF Core"];

    private async Task SubmitAsync(SendMailForm form)
    {
        // Body(Component) renders a Rask component to HTML and encodes its text — the safe path, versus
        // interpolating the raw string into HTML. Here the typed body becomes an encoded <p>.
        await mail.SendAsync(Email
            .To(form.To)
            .Subject(form.Subject)
            .Body(P[form.Body]));

        _queuedFor = form.To;
    }

    protected override Component? Render() =>
        Div.Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 border-0 mx-auto").Style("max-width: 32rem")[
            Div.Class("p-5")[
                H1.Class("text-xl font-semibold mb-3")["Send email"],
                P.Class("text-slate-500 dark:text-slate-400 text-sm")[
                    "Queued on the app's own SQLite database and delivered off the request thread. ",
                    "With no SMTP configured this demo writes each message to a pickup directory as an ",
                    Code[".eml"], " file."
                ],
                _queuedFor is { } to
                    ? Div.Class("rounded-lg px-4 py-3 text-sm bg-emerald-50 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200").Id("mail-sent")[
                        Span.Class("me-1").Attributes(("aria-hidden", "true"))["✅"],
                        "Queued for ", Strong[to], " — the processor will deliver it shortly."
                    ]
                    : null,
                Form.Model(_form).OnValidSubmitAsync(SubmitAsync).Class("flex flex-col gap-3")[
                    Div[
                        Label.For("mail-to").Class("mb-1 block text-sm font-medium")["To"],
                        Input.Bind(() => _form.To).Id("mail-to").Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100").Placeholder("jane@example.com")
                    ],
                    Div[
                        Label.For("mail-subject").Class("mb-1 block text-sm font-medium")["Subject"],
                        Input.Bind(() => _form.Subject).Id("mail-subject").Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100")
                    ],
                    Div[
                        Label.For("mail-body").Class("mb-1 block text-sm font-medium")["Body"],
                        Input.Bind(() => _form.Body).Id("mail-body").Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100")
                    ],
                    Div.Class("flex justify-end pt-2")[
                        Button.Type("submit").Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500").Id("mail-send")[
                            Span.Class("me-1").Attributes(("aria-hidden", "true"))["➤"], "Send"
                        ]
                    ]
                ]
            ]
        ];
}

// The slice's own input model: mutable primitives the inputs bind to.
public sealed class SendMailForm
{
    public string To { get; set; } = "";
    public string Subject { get; set; } = "Hello from Rask";
    public string Body { get; set; } = "This email was queued on SQLite and delivered in the background.";
}
