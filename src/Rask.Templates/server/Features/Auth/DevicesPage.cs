using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Company.RaskServer.Features.Auth;

// Every device signed in to this account: one Session row each. Signing out the others deletes their rows, and
// each of those devices finds itself signed out on its next request.
[Authorize]
[Route("/devices")]
public sealed partial class DevicesPage(IAuth auth, IUserProvider users) : AuthPage
{
    private IReadOnlyList<Session> _sessions = [];
    private bool _signedOutOthers;

    protected override Component? HeadAssets => Title["Your devices"];

    protected override async Task OnMountAsync() => await LoadAsync();

    protected override Component? Content =>
        Fragment[
            H1.Class("text-2xl font-bold")["Your devices"],
            _signedOutOthers ? Ok("devices-signed-out", "Every other device is signed out.") : null,
            Ul.Class("list")[
                _sessions.Select(session =>
                    Li.Key(session.Id).Class("list-row")[
                        Div[
                            Div.Class("font-semibold")[Describe(session.UserAgent)],
                            Div.Class("text-xs opacity-70")[
                                session.Id == users.Current.SessionId()
                                    ? "This device"
                                    : "Last seen " + session.LastSeenAt.ToString("g", CultureInfo.CurrentCulture)]
                        ]
                    ])
            ],
            _sessions.Count > 1
                ? Div.Class("card-actions mt-2")[
                    Button.Type("button").Id("devices-sign-out-others").Class("btn btn-outline btn-block").OnClick(SignOutOthersAsync)[
                        "Sign out every other device"]
                ]
                : null
        ];

    private async Task SignOutOthersAsync()
    {
        await auth.SignOutOtherDevicesAsync();
        _signedOutOthers = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (users.Current.UserId() is not { } me)
        {
            _sessions = [];
            return;
        }

        _sessions = await Session
            .Where(s => s.UserId == me)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(CancellationToken);
    }

    private static string Describe(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) ? "Unknown browser"
        : userAgent.Length > 80 ? userAgent[..80] + "…"
        : userAgent;
}
