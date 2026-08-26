using System.Globalization;
using System.Reflection;
using Xunit.Sdk;

namespace Rask.Core.Tests;

/// <summary>
///     Restores <see cref="CultureInfo.CurrentCulture" /> and <see cref="CultureInfo.CurrentUICulture" />
///     around a test that deliberately runs under a hostile culture.
/// </summary>
/// <remarks>
///     Not applied assembly-wide, unlike <see cref="ResetLiveSyncContextAttribute" />: only the handful of
///     tests that assert invariance need it, and paying two AsyncLocal writes on every test would be
///     noise. It exists for the same underlying reason though — xUnit reuses pooled threads, and since
///     .NET Core the current culture rides <c>ExecutionContext</c>, so a culture assigned by one test can
///     surface in the next one on that thread and make an unrelated assertion fail somewhere else
///     entirely. Restoring in <see cref="After" /> keeps that leak inside the test that opted in.
/// </remarks>
public sealed class RestoreCultureAttribute : BeforeAfterTestAttribute
{
    private CultureInfo? _culture;
    private CultureInfo? _uiCulture;

    public override void Before(MethodInfo methodUnderTest)
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
    }

    public override void After(MethodInfo methodUnderTest)
    {
        if (_culture is not null)
        {
            CultureInfo.CurrentCulture = _culture;
        }

        if (_uiCulture is not null)
        {
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
