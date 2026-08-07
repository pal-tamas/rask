using Rask.Core.Routing;
using Rask.Mail;

namespace Rask.Example.EfCore.Features.Mail.SendMail;

// Vertical slice: queue a transactional email. IMailQueue writes one QueuedMail row on the same SQLite
// database the catalog uses; the background MailProcessor delivers it just after — here to a pickup
// directory as an .eml file (no SMTP server needed), so the send returns instantly and the user sees a
// confirmation while delivery happens off the request thread.
[Route("mail")]
public sealed partial class SendMailPage(IMailQueue mail) : Component
{
    private readonly SendMailForm _form = new();
    private string? _queuedFor;

    protected override Component? Head => Title()["Send email — Rask EF Core"];

    private async Task SubmitAsync(SendMailForm form)
    {
        // Body(Component) renders a Rask component to HTML and encodes its text — the safe path, versus
        // interpolating the raw string into HTML. Here the typed body becomes an encoded <p>.
        await mail.SendAsync(Email
            .To(form.To)
            .Subject(form.Subject)
            .Body(P()[form.Body]));

        _queuedFor = form.To;
    }

    protected override Component? Render() =>
        Div(Class: "card shadow-sm border-0 mx-auto", Style: "max-width: 32rem")[
            Div(Class: "card-body")[
                H1(Class: "h4 mb-3")["Send email"],
                P(Class: "text-secondary small")[
                    "Queued on the app's own SQLite database and delivered off the request thread. ",
                    "With no SMTP configured this demo writes each message to a pickup directory as an ",
                    Code()[".eml"], " file."
                ],
                _queuedFor is { } to
                    ? Div(Class: "alert alert-success", Id: "mail-sent")[
                        I(Class: "bi bi-check2-circle me-1"),
                        "Queued for ", Strong()[to], " — the processor will deliver it shortly."
                    ]
                    : null,
                Form(_form, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
                    Div()[
                        Label("mail-to", Class: "form-label small mb-1")["To"],
                        Input(() => _form.To, Id: "mail-to", Class: "form-control", Placeholder: "jane@example.com")
                    ],
                    Div()[
                        Label("mail-subject", Class: "form-label small mb-1")["Subject"],
                        Input(() => _form.Subject, Id: "mail-subject", Class: "form-control")
                    ],
                    Div()[
                        Label("mail-body", Class: "form-label small mb-1")["Body"],
                        Input(() => _form.Body, Id: "mail-body", Class: "form-control")
                    ],
                    Div(Class: "d-flex justify-content-end pt-2")[
                        Button("submit", Class: "btn btn-primary", Id: "mail-send")[
                            I(Class: "bi bi-send me-1"), "Send"
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
