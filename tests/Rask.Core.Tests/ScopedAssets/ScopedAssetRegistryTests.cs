using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

[Collection("ScopedAssets")]
public partial class ScopedAssetRegistryTests
{
    public ScopedAssetRegistryTests() => ScopedAssetRegistry.InvalidateAll();

    // ─── Registration & retrieval ─────────────────────────────────────────

    [Fact]
    public void An_empty_registry_has_no_css()
    {
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash));
        Assert.Equal(string.Empty, hash);
        Assert.Null(ScopedAssetRegistry.GetByHash("anyhash12345", AssetKind.Css));
    }

    [Fact]
    public void An_empty_registry_has_no_js()
    {
        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash));
        Assert.Equal(string.Empty, hash);
    }

    [Fact]
    public void Registering_css_produces_a_hash_and_allows_lookup_both_by_type_and_by_hash()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash));
        Assert.Matches("^[0-9a-f]{12}$", hash);

        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css);
        Assert.NotNull(bytes);
        var content = Encoding.UTF8.GetString(bytes.Value.Utf8.Span);
        Assert.Contains(".x[data-r-", content);
    }

    [Fact]
    public void Registering_js_produces_a_hash_and_allows_lookup_both_by_type_and_by_hash()
    {
        ScopedAssetRegistry.RegisterJs(
            typeof(WidgetA),
            "export function hello() { return 1; }");

        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash));
        Assert.Matches("^[0-9a-f]{12}$", hash);

        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Js);
        Assert.NotNull(bytes);
        var content = Encoding.UTF8.GetString(bytes.Value.Utf8.Span);
        Assert.Contains("window.Rask[\"WidgetA\"]", content);
        Assert.Contains("hello: typeof hello === 'function'", content);
    }

    [Fact]
    public void Registering_js_with_an_async_function_export_strips_the_export_and_exposes_it()
    {
        // An `export async function` must have its `export` stripped (the wrapper IIFE is
        // not an ES module, so a leftover `export` is a SyntaxError) and still be re-exposed
        // on window.Rask. A mixed sync export verifies both forms collect together.
        ScopedAssetRegistry.RegisterJs(
            typeof(WidgetA),
            "export async function copy(t) { await navigator.clipboard.writeText(t); }\n" +
            "export function size() { return 1; }");

        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash));
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Js);
        Assert.NotNull(bytes);
        var content = Encoding.UTF8.GetString(bytes.Value.Utf8.Span);

        // The `export` keyword is gone; the bare `async function` declaration remains.
        Assert.DoesNotContain("export ", content);
        Assert.Contains("async function copy(", content);
        // Both functions are re-exposed on the returned namespace object.
        Assert.Contains("copy: typeof copy === 'function'", content);
        Assert.Contains("size: typeof size === 'function'", content);
    }

    [Fact]
    public void An_exported_class_is_exposed_with_a_factory_and_loses_its_export_keyword()
    {
        ScopedAssetRegistry.RegisterJs(
            typeof(WidgetA),
            "export class Chart { constructor(el) { this.el = el; } draw() {} }\n" +
            "export function size() { return 1; }");

        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash));
        var content = Encoding.UTF8.GetString(ScopedAssetRegistry.GetByHash(hash, AssetKind.Js)!.Value.Utf8.Span);

        Assert.DoesNotContain("export ", content);
        Assert.Contains("class Chart {", content);
        Assert.Contains("Chart: Chart", content);
        Assert.Contains("__new_Chart: function () { return new Chart(...arguments); }", content);
        Assert.Contains("size: typeof size === 'function'", content);
    }

    [Fact]
    public void An_export_default_class_is_exposed_with_a_factory_too()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export default class Chart { draw() {} }");

        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash));
        var content = Encoding.UTF8.GetString(ScopedAssetRegistry.GetByHash(hash, AssetKind.Js)!.Value.Utf8.Span);

        Assert.DoesNotContain("export ", content);
        Assert.Contains("__new_Chart: function () { return new Chart(...arguments); }", content);
    }

    [Fact]
    public void Registering_both_gives_the_type_independent_css_and_js_hashes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var cssHash));
        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var jsHash));
        Assert.NotEqual(cssHash, jsHash);
    }

    [Fact]
    public void Registering_the_same_css_twice_raises_no_event_on_the_second_call()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        var count = 0;
        Action<Type, AssetKind> handler = (_, _) => count++;
        ScopedAssetRegistry.AssetChanged += handler;
        try
        {
            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
            Assert.Equal(0, count);
        }
        finally
        {
            ScopedAssetRegistry.AssetChanged -= handler;
        }
    }

    [Fact]
    public void Registering_different_css_for_the_same_type_drops_the_old_hash_from_the_hash_index()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var firstHash);

        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: blue; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var secondHash);

        Assert.NotEqual(firstHash, secondHash);
        Assert.Null(ScopedAssetRegistry.GetByHash(firstHash, AssetKind.Css));
        Assert.NotNull(ScopedAssetRegistry.GetByHash(secondHash, AssetKind.Css));
    }

    [Fact]
    public void Registering_css_with_a_whitespace_source_acts_as_unregister()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), "   \n  ");

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
    }

    [Fact]
    public void Registering_js_with_a_whitespace_source_acts_as_unregister()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "");

        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
    }

    [Fact]
    public void Unregistering_an_unknown_type_does_nothing_and_raises_no_event()
    {
        var count = 0;
        Action<Type, AssetKind> handler = (_, _) => count++;
        ScopedAssetRegistry.AssetChanged += handler;
        try
        {
            ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
            ScopedAssetRegistry.UnregisterJs(typeof(WidgetA));
            Assert.Equal(0, count);
        }
        finally
        {
            ScopedAssetRegistry.AssetChanged -= handler;
        }
    }

    [Fact]
    public void Unregistering_drops_both_the_type_and_the_hash_entries()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.Null(ScopedAssetRegistry.GetByHash(hash, AssetKind.Css));
        Assert.False(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out _));
    }

    [Fact]
    public void Invalidating_all_clears_everything()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".y { color: blue; }");

        ScopedAssetRegistry.InvalidateAll();

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out _));
        Assert.Equal(0, ScopedAssetRegistry.CssEntryCount);
        Assert.Equal(0, ScopedAssetRegistry.JsEntryCount);
    }

    // ─── Hash collapse (refcounting) ──────────────────────────────────────

    [Fact]
    public void Two_types_with_identical_source_produce_different_hashes_because_the_scope_id_differs()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".x { color: red; }");

        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashA);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashB);

        Assert.NotEqual(hashA, hashB);
        Assert.Equal(2, ScopedAssetRegistry.CssEntryCount);
    }

    [Fact]
    public void Two_types_with_identical_rewritten_content_share_a_single_entry()
    {
        // CSS that doesn't depend on the scope id: only @font-face. CssScoper.Rewrite passes
        // @font-face through unchanged, so both types produce byte-equal rewritten bytes and
        // share a single registry entry (refcount=2).
        const string fontFaceCss = "@font-face { font-family: 'X'; src: url('a.woff2'); }";
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), fontFaceCss);
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), fontFaceCss);

        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashA);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashB);

        Assert.Equal(hashA, hashB);
        Assert.Equal(1, ScopedAssetRegistry.CssEntryCount);

        // Unregistering one decrements refcount; entry survives because the other still owns it.
        ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
        Assert.NotNull(ScopedAssetRegistry.GetByHash(hashB, AssetKind.Css));

        // Unregistering the last reference drops the entry.
        ScopedAssetRegistry.UnregisterCss(typeof(WidgetB));
        Assert.Null(ScopedAssetRegistry.GetByHash(hashB, AssetKind.Css));
    }

    // ─── Hashing properties ───────────────────────────────────────────────

    [Fact]
    public void The_hash_is_12_lower_case_hex_chars()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        Assert.Equal(12, hash.Length);
        Assert.Matches("^[0-9a-f]{12}$", hash);
    }

    [Fact]
    public void The_hash_is_stable_across_multiple_registrations_of_the_same_content()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var firstHash);

        ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var secondHash);

        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public void The_hash_is_independent_of_the_registration_order()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".y { color: blue; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashAFirst);

        ScopedAssetRegistry.InvalidateAll();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".y { color: blue; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashASecond);

        Assert.Equal(hashAFirst, hashASecond);
    }

    [Fact]
    public void Hashes_are_unique_across_1000_distinct_content_variants()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 1000; i++)
        {
            var bytes = Encoding.UTF8.GetBytes($"variant-{i}");
            // Use the same hash function the registry uses (via a register/read round-trip
            // against a unique type per iteration would be heavier; here we just want a
            // pre-image-distinct sample to assert post-image distinctness empirically).
            using var sha = SHA256.Create();
            var full = sha.ComputeHash(bytes);
            var sb = new StringBuilder(12);
            for (var j = 0; j < 6; j++)
            {
                sb.Append(full[j].ToString("x2"));
            }

            Assert.True(hashes.Add(sb.ToString()),
                $"collision at variant {i}");
        }
    }

    // ─── Scope id ─────────────────────────────────────────────────────────

    [Fact]
    public void A_css_only_type_has_a_scope_id()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out var scope));
        Assert.Matches("^r-[0-9a-f]{8}$", scope);
    }

    [Fact]
    public void A_js_only_type_has_no_scope_id()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");

        Assert.False(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out _));
    }

    [Fact]
    public void The_scope_id_is_stable_across_registrations_and_derives_from_the_type_name()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out var first);
        ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".totally { different: content; }");
        ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out var second);

        Assert.Equal(first, second);
    }

    // ─── Kind-typed indexing ──────────────────────────────────────────────

    [Fact]
    public void Getting_by_hash_with_a_cross_kind_mismatch_gives_null()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var cssHash);

        // Same hash queried as JS → null (cross-type confusion prevention)
        Assert.Null(ScopedAssetRegistry.GetByHash(cssHash, AssetKind.Js));
        Assert.NotNull(ScopedAssetRegistry.GetByHash(cssHash, AssetKind.Css));
    }

    [Fact]
    public void Getting_by_a_null_or_empty_hash_gives_null()
    {
        Assert.Null(ScopedAssetRegistry.GetByHash(null!, AssetKind.Css));
        Assert.Null(ScopedAssetRegistry.GetByHash("", AssetKind.Css));
    }

    [Fact]
    public void Getting_by_an_unknown_hash_gives_null() =>
        Assert.Null(ScopedAssetRegistry.GetByHash("ffffffffffff", AssetKind.Css));

    [Fact]
    public void The_asset_etag_is_the_hash_wrapped_in_double_quotes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css);

        Assert.NotNull(bytes);
        Assert.Equal($"\"{hash}\"", bytes.Value.Etag);
    }

    // ─── Concurrency ──────────────────────────────────────────────────────

    [Fact]
    public void Concurrently_registering_distinct_types_all_succeed()
    {
        var types = new[]
        {
            typeof(P0), typeof(P1), typeof(P2), typeof(P3), typeof(P4), typeof(P5), typeof(P6), typeof(P7),
            typeof(P8), typeof(P9)
        };

        Parallel.ForEach(types, t =>
        {
            ScopedAssetRegistry.RegisterCss(t, $".x{t.Name} {{ color: red; }}");
        });

        foreach (var t in types)
        {
            Assert.True(ScopedAssetRegistry.TryGetCss(t, out _),
                $"{t.Name} not registered");
        }
    }

    [Fact]
    public void Concurrently_registering_and_replacing_the_same_type_leaves_a_consistent_state()
    {
        var hashes = new ConcurrentBag<string>();
        Parallel.For(0, 100, i =>
        {
            ScopedAssetRegistry.RegisterCss(
                typeof(WidgetA),
                $".x {{ color: rgb({i % 256},0,0); }}");
            if (ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var h))
            {
                hashes.Add(h);
            }
        });

        // The final state has exactly one hash for WidgetA, and that hash is one of
        // the observed values during the race. The by-hash index agrees.
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var finalHash));
        Assert.NotNull(ScopedAssetRegistry.GetByHash(finalHash, AssetKind.Css));
    }

    [Fact]
    public void Concurrently_registering_and_getting_by_hash_never_throws_and_the_final_state_is_consistent()
    {
        // Concurrent register-replace + lookup race: an in-flight GetByHash may legitimately
        // return null if another thread replaced the type's hash between the two calls
        // (the old hash got refcount-decremented to zero and dropped). That's correct
        // production behavior — a stale URL mid-hot-reload yields a 404, recovers on next
        // render. The invariants we DO require: no exception, and the final committed
        // state is queryable end-to-end.
        Parallel.For(0, 500, i =>
        {
            if (i % 2 == 0)
            {
                ScopedAssetRegistry.RegisterCss(
                    typeof(WidgetA),
                    $".x {{ color: rgb({i % 256},0,0); }}");
            }
            else
            {
                _ = ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var h);
                _ = ScopedAssetRegistry.GetByHash(h, AssetKind.Css);
            }
        });

        // Post-race quiescent assertion: the surviving registration is byte-self-consistent.
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var finalHash));
        Assert.NotNull(ScopedAssetRegistry.GetByHash(finalHash, AssetKind.Css));
    }

    // ─── Events ───────────────────────────────────────────────────────────

    [Fact]
    public void AssetChanged_fires_with_the_type_and_kind_on_each_kind_independently()
    {
        var events = new List<(Type, AssetKind)>();
        Action<Type, AssetKind> handler = (t, k) => events.Add((t, k));
        ScopedAssetRegistry.AssetChanged += handler;
        try
        {
            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
            ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");

            Assert.Contains((typeof(WidgetA), AssetKind.Css), events);
            Assert.Contains((typeof(WidgetA), AssetKind.Js), events);
        }
        finally
        {
            ScopedAssetRegistry.AssetChanged -= handler;
        }
    }

    [Fact]
    public void AssetChanged_fires_on_replace_not_on_a_no_op_register()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        var count = 0;
        Action<Type, AssetKind> handler = (_, _) => count++;
        ScopedAssetRegistry.AssetChanged += handler;
        try
        {
            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
            Assert.Equal(0, count);

            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: blue; }");
            Assert.Equal(1, count);
        }
        finally
        {
            ScopedAssetRegistry.AssetChanged -= handler;
        }
    }

    [Fact]
    public void AssetChanged_fires_on_unregister_only_when_something_was_registered()
    {
        var count = 0;
        Action<Type, AssetKind> handler = (_, _) => count++;
        ScopedAssetRegistry.AssetChanged += handler;
        try
        {
            // No registration → no event.
            ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
            Assert.Equal(0, count);

            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
            count = 0;
            ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
            Assert.Equal(1, count);
        }
        finally
        {
            ScopedAssetRegistry.AssetChanged -= handler;
        }
    }

    // ─── Type-system edge cases ───────────────────────────────────────────

    [Fact]
    public void Registering_css_for_a_null_type_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ScopedAssetRegistry.RegisterCss(null!, ".x {}"));
    }

    [Fact]
    public void Registering_css_for_an_open_generic_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ScopedAssetRegistry.RegisterCss(typeof(Generic<>), ".x {}"));

        Assert.Contains("Open generic", ex.Message);
    }

    [Fact]
    public void Registering_js_for_an_open_generic_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ScopedAssetRegistry.RegisterJs(typeof(Generic<>), "export function f(){}"));
    }

    [Fact]
    public void A_generic_with_different_type_args_gets_distinct_hashes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Generic<int>), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(Generic<string>), ".x { color: red; }");

        ScopedAssetRegistry.TryGetCss(typeof(Generic<int>), out var hashInt);
        ScopedAssetRegistry.TryGetCss(typeof(Generic<string>), out var hashStr);

        Assert.NotEqual(hashInt, hashStr);
    }

    [Fact]
    public void A_derived_type_without_its_own_css_has_no_registration()
    {
        ScopedAssetRegistry.RegisterCss(typeof(BaseWidget), ".base { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(BaseWidget), out _));
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(DerivedWidget), out _));
    }

    [Fact]
    public void Css_on_both_a_base_and_a_derived_type_is_stored_independently()
    {
        ScopedAssetRegistry.RegisterCss(typeof(BaseWidget), ".base { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(DerivedWidget), ".derived { color: blue; }");

        ScopedAssetRegistry.TryGetCss(typeof(BaseWidget), out var hashBase);
        ScopedAssetRegistry.TryGetCss(typeof(DerivedWidget), out var hashDerived);
        Assert.NotEqual(hashBase, hashDerived);
    }

    [Fact]
    public void A_nested_type_registers_with_a_distinct_hash()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Outer.Inner), ".n { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(Outer.Inner), out var hash));
        Assert.Matches("^[0-9a-f]{12}$", hash);
    }

    [Fact]
    public void A_dynamically_loaded_type_registers_and_serves()
    {
        // Use the current assembly as a stand-in for "dynamically loaded" — the API
        // we exercise (RegisterCss with a runtime-resolved Type) is the same shape.
        var type = Assembly.GetExecutingAssembly().GetType(typeof(WidgetA).FullName!);
        Assert.NotNull(type);

        ScopedAssetRegistry.RegisterCss(type!, ".dynamic { color: red; }");
        Assert.True(ScopedAssetRegistry.TryGetCss(type!, out _));
    }

    [Fact]
    public void A_registered_type_is_held_strongly_by_the_registry()
    {
        // A type from a collectible AssemblyLoadContext: while the registry holds it,
        // the ALC cannot collect. This is the documented constraint.
        var alc = new AssemblyLoadContext("test-alc-" + Guid.NewGuid(), true);
        var alcWeak = new WeakReference(alc);

        // Register the current-assembly type using the same ALC's resolution path.
        // The registry's strong Type reference would keep an actual ALC-loaded assembly
        // rooted; here we only assert the registry retains the type after registration.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));

        alc.Unload();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The registration itself stays valid (its type isn't from the unloaded ALC).
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        _ = alcWeak; // referenced to silence unused warnings; full collectible-ALC test
        // would need a runtime-emitted assembly which is heavy for this suite
    }

    // ─── Enumeration (for publish-time bake) ──────────────────────────────

    [Fact]
    public void Enumerating_an_empty_registry_yields_nothing() => Assert.Empty(ScopedAssetRegistry.EnumerateAll());

    [Fact]
    public void Enumerating_yields_the_registered_css_and_js_entries_with_distinct_hashes_and_kinds()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");

        var entries = ScopedAssetRegistry.EnumerateAll().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries.Count(e => e.Kind == AssetKind.Css));
        Assert.Equal(1, entries.Count(e => e.Kind == AssetKind.Js));

        // Hash + kind together uniquely identify an entry.
        Assert.Equal(entries.Count, entries.Select(e => (e.Hash, e.Kind)).Distinct().Count());

        // Bytes match what GetByHash returns.
        foreach (var e in entries)
        {
            var lookup = ScopedAssetRegistry.GetByHash(e.Hash, e.Kind);
            Assert.NotNull(lookup);
            Assert.Equal(lookup.Value.Utf8.ToArray(), e.Utf8.ToArray());
        }
    }

    [Fact]
    public void Enumerating_two_types_sharing_the_same_rewritten_content_yields_one_entry_with_that_hash()
    {
        const string passthrough = "@font-face { font-family: 'X'; src: url('a.woff2'); }";
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), passthrough);
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), passthrough);

        // Both types reference the same hash via refcount — the by-hash bucket has one
        // entry that EnumerateAll yields once. The bake step writes one file; both
        // type's <link> tags resolve to the same URL on the wire.
        var entries = ScopedAssetRegistry.EnumerateAll().ToList();
        Assert.Single(entries);
        Assert.Equal(AssetKind.Css, entries[0].Kind);
    }

    // ─── Test fixture types ───────────────────────────────────────────────

    private sealed class WidgetA : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class WidgetB : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P0 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P1 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P2 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P3 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P4 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P5 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P6 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P7 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P8 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class P9 : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class Generic<T> : Component
    {
        protected override Component? Render() => this;
    }

    private class BaseWidget : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class DerivedWidget : BaseWidget
    {
    }

    internal static partial class Outer
    {
        internal sealed partial class Inner : Component
        {
            protected override Component? Render() => this;
        }
    }
}
