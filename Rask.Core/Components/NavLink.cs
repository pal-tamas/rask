using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Core.Components;

public sealed class NavLink : Component<NavLink.Props>
{
    public NavLink(Props? props, IEnumerable<Child>? children = null) : base(props ?? new Props(), children) { }
    public NavLink(Props? props, params Child[] children) : base(props ?? new Props(), children) { }

    protected override string TagName => "a";

    public new sealed record Props(
        RouteUrl? Href = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        string ActiveClass = "active",
        NavLinkMatch ActiveMatch = NavLinkMatch.Exact)
        : Component.Props(Id, Class, Style, Data)
    {
        public override IEnumerable<KeyValuePair<string, string?>> ToAttributes()
        {
            // Inlines Component.Props.ToAttributes() so the active class can be merged
            // into the class attribute. Order must match base: id, class, style, data-*.
            if (Id is not null)
            {
                yield return new KeyValuePair<string, string?>("id", Id);
            }

            var effectiveClass = ResolveEffectiveClass();
            if (effectiveClass is not null)
            {
                yield return new KeyValuePair<string, string?>("class", effectiveClass);
            }

            if (Style is not null)
            {
                yield return new KeyValuePair<string, string?>("style", Style);
            }

            if (Data is not null)
            {
                foreach (var kv in Data)
                {
                    yield return new KeyValuePair<string, string?>($"data-{kv.Key}", kv.Value);
                }
            }

            if (Href is not null)
            {
                yield return new KeyValuePair<string, string?>("href", Href.Value.ToString());
            }

            yield return new KeyValuePair<string, string?>("data-rask-nav", null);
        }

        private string? ResolveEffectiveClass()
        {
            if (!IsActive() || string.IsNullOrEmpty(ActiveClass))
            {
                return Class;
            }

            return string.IsNullOrEmpty(Class) ? ActiveClass : $"{Class} {ActiveClass}";
        }

        private bool IsActive()
        {
            if (Href is null)
            {
                return false;
            }

            var route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
            if (route is null)
            {
                return false;
            }

            return ActiveMatch switch
            {
                NavLinkMatch.Exact => MatchExact(route, Href.Value.Path, Href.Value.QueryString),
                NavLinkMatch.Prefix => MatchPrefix(route.Path, Href.Value.Path),
                _ => false
            };
        }

        private static bool MatchExact(RouteState route, string hrefPath, string? hrefQuery)
        {
            if (!PathsEqual(route.Path, hrefPath))
            {
                return false;
            }

            if (string.IsNullOrEmpty(hrefQuery))
            {
                return true;
            }

            foreach (var pair in ParseQuery(hrefQuery))
            {
                if (!route.Query.TryGetValue(pair.Key, out var values))
                {
                    return false;
                }

                if (!values.Contains(pair.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchPrefix(string currentPath, string hrefPath)
        {
            var cur = TrimSlash(currentPath);
            var pre = TrimSlash(hrefPath);
            if (pre.Length == 0)
            {
                return true;
            }

            if (cur.Equals(pre, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return cur.Length > pre.Length
                   && cur.StartsWith(pre, StringComparison.OrdinalIgnoreCase)
                   && cur[pre.Length] == '/';
        }

        private static bool PathsEqual(string a, string b) =>
            string.Equals(TrimSlash(a), TrimSlash(b), StringComparison.OrdinalIgnoreCase);

        private static string TrimSlash(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            return s.Length > 1 && s[^1] == '/' ? s[..^1] : s;
        }

        private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string qs)
        {
            var s = qs.StartsWith('?') ? qs[1..] : qs;
            if (s.Length == 0)
            {
                yield break;
            }

            foreach (var part in s.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq < 0)
                {
                    yield return new KeyValuePair<string, string>(Uri.UnescapeDataString(part), string.Empty);
                }
                else
                {
                    yield return new KeyValuePair<string, string>(
                        Uri.UnescapeDataString(part[..eq]),
                        Uri.UnescapeDataString(part[(eq + 1)..]));
                }
            }
        }
    }
}
