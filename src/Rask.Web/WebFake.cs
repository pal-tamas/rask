using System.Linq.Expressions;
using Rask.Core;

namespace Rask.Web;

/// <summary>
///     A test's stand-in for a web object: every chain that starts at it is answered here instead of by the browser,
///     for this test's flow, until it is disposed of. <c>using var clipboard = Navigator.Clipboard.Fake();</c>
/// </summary>
/// <typeparam name="T">The MDN interface faked.</typeparam>
/// <remarks>
///     <code>
///     using var clipboard = Navigator.Clipboard.Fake();
///     clipboard.Returns(c => c.ReadText(), "pasted");
///
///     await page.Click("Copy");
///
///     Assert.Equal("writeText", clipboard.Calls.Single().Member);
///     </code>
///     <para>
///         A read or call nobody set up answers the type's default; a write is remembered, so reading it back sees it.
///         An object kept from it stays inside it. <see cref="Raise" /> fires an event at its subscribers, whose
///         handlers run in their components' order and re-render them, as the browser's would. <see cref="Throws" />
///         fails a member as the browser refusing it would, and <see cref="CallsBack" /> runs the handler a call such as
///         a lock request was handed.
///     </para>
/// </remarks>
public sealed class WebFake<T> : IDisposable
    where T : JsObject
{
    private readonly WebFakes.Entry _entry;

    internal WebFake(T web) => _entry = WebFakes.Add(web.Chain.Path());

    /// <summary>Every call and write the code under test made on it, in order: <c>writeText("hi")</c> as Member <c>writeText</c>.</summary>
    public IReadOnlyList<WebCall> Calls => _entry.Calls;

    /// <summary>Answers <paramref name="member" /> — a read or a call, as far along as it goes — with <paramref name="value" />.</summary>
    /// <example><c>fake.Returns(c => c.ReadText(), "pasted")</c>, <c>navigator.Returns(n => n.Language, "hu")</c></example>
    public WebFake<T> Returns<TValue>(Expression<Func<T, ValueTask<TValue>>> member, TValue value)
    {
        _entry.Answers[WebFakes.Member(member.Body)] = value;
        return this;
    }

    /// <summary>
    ///     Makes <paramref name="member" /> fail with <paramref name="error" />, as the browser refusing it would.
    /// </summary>
    /// <example><c>storage.Throws(s => s.Persist(), new JSException("denied"))</c></example>
    public WebFake<T> Throws<TValue>(Expression<Func<T, ValueTask<TValue>>> member, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _entry.Answers[WebFakes.Member(member.Body)] = new WebFakes.Refusal(error);
        return this;
    }

    /// <summary>
    ///     Answers the call MDN names <paramref name="member" /> (<c>"request"</c>) by running <paramref name="answer" />,
    ///     handed the call and the handler the code under test passed it, to call with what the browser would pass. The
    ///     call settles when <paramref name="answer" /> does, as a lock request settles once its handler has.
    /// </summary>
    /// <example><c>locks.CallsBack&lt;Lock?&gt;("request", (call, handler) =&gt; handler(new Lock { Name = "sync" }))</c></example>
    public WebFake<T> CallsBack<TArg>(string member, Func<WebCall, Func<TArg, Task>, Task> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        _entry.CallsBack[member] = (call, run) => answer(call, arg => run([arg]));
        return this;
    }

    /// <summary>Fires the event MDN names <paramref name="type" /> (<c>"change"</c>) at every handler subscribed to it.</summary>
    public void Raise(string type, Event e)
    {
        foreach (var listener in _entry.Listeners.Where(l => string.Equals(l.Type, type, StringComparison.Ordinal)).ToList())
        {
            listener.Owner.RunFromScript(() => listener.Invoke(e));
        }
    }

    /// <summary>Hands the object back to the browser.</summary>
    public void Dispose() => WebFakes.Remove(_entry);
}
