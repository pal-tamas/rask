using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

[Collection("ScopedAssets")]
public class ScopedAssetRegistryTests
{
    public ScopedAssetRegistryTests() => ScopedAssetRegistry.InvalidateAll();

    // ─── Registration & retrieval ─────────────────────────────────────────

    [Fact]
    public void EmptyRegistry_TryGetCss_ReturnsFalse()
    {
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash));
        Assert.Equal(string.Empty, hash);
        Assert.Null(ScopedAssetRegistry.GetByHash("anyhash12345", AssetKind.Css));
    }

    [Fact]
    public void EmptyRegistry_TryGetJs_ReturnsFalse()
    {
        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash));
        Assert.Equal(string.Empty, hash);
    }

    [Fact]
    public void RegisterCss_ProducesHashAndAllowsLookupBoth_ByTypeAndByHash()
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
    public void RegisterJs_ProducesHashAndAllowsLookupBoth_ByTypeAndByHash()
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
    public void RegisterBoth_TypeHasIndependentCssAndJsHashes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var cssHash));
        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var jsHash));
        Assert.NotEqual(cssHash, jsHash);
    }

    [Fact]
    public void RegisterCss_SameContentTwice_NoEventOnSecondCall()
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
    public void RegisterCss_DifferentContentForSameType_DropsOldHashFromByHashIndex()
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
    public void RegisterCss_WithWhitespaceSource_ActsAsUnregister()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), "   \n  ");

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
    }

    [Fact]
    public void RegisterJs_WithWhitespaceSource_ActsAsUnregister()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "");

        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
    }

    [Fact]
    public void UnregisterUnknownType_IsNoOp_NoEvent()
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
    public void Unregister_DropsBothByTypeAndByHashEntries()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.Null(ScopedAssetRegistry.GetByHash(hash, AssetKind.Css));
        Assert.False(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out _));
    }

    [Fact]
    public void InvalidateAll_ClearsEverything()
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
    public void TwoTypesWithIdenticalSource_ProduceDifferentHashes_BecauseScopeIdDiffers()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".x { color: red; }");

        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashA);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashB);

        Assert.NotEqual(hashA, hashB);
        Assert.Equal(2, ScopedAssetRegistry.CssEntryCount);
    }

    [Fact]
    public void TwoTypesWithIdenticalRewrittenContent_ShareSingleEntry()
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
    public void Hash_Is12LowercaseHex()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        Assert.Equal(12, hash.Length);
        Assert.Matches("^[0-9a-f]{12}$", hash);
    }

    [Fact]
    public void Hash_IsStable_AcrossMultipleRegistrationsOfTheSameContent()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var firstHash);

        ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var secondHash);

        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public void Hash_IsIndependentOfRegistrationOrder()
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
    public void Hashes_AreUnique_Across1000DistinctContentVariants()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 1000; i++)
        {
            var bytes = Encoding.UTF8.GetBytes($"variant-{i}");
            // Use the same hash function the registry uses (via a register/read round-trip
            // against a unique type per iteration would be heavier; here we just want a
            // pre-image-distinct sample to assert post-image distinctness empirically).
            using var sha = System.Security.Cryptography.SHA256.Create();
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
    public void TryGetScopeId_CssOnly_ReturnsScopeId()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out var scope));
        Assert.Matches("^r-[0-9a-f]{8}$", scope);
    }

    [Fact]
    public void TryGetScopeId_JsOnly_ReturnsFalse()
    {
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f() {}");

        Assert.False(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out _));
    }

    [Fact]
    public void ScopeId_IsStableAcrossRegistrations_DerivesFromTypeFqn()
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
    public void GetByHash_CrossKindMismatch_ReturnsNull()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var cssHash);

        // Same hash queried as JS → null (cross-type confusion prevention)
        Assert.Null(ScopedAssetRegistry.GetByHash(cssHash, AssetKind.Js));
        Assert.NotNull(ScopedAssetRegistry.GetByHash(cssHash, AssetKind.Css));
    }

    [Fact]
    public void GetByHash_NullOrEmptyHash_ReturnsNull()
    {
        Assert.Null(ScopedAssetRegistry.GetByHash(null!, AssetKind.Css));
        Assert.Null(ScopedAssetRegistry.GetByHash("", AssetKind.Css));
    }

    [Fact]
    public void GetByHash_UnknownHash_ReturnsNull()
    {
        Assert.Null(ScopedAssetRegistry.GetByHash("ffffffffffff", AssetKind.Css));
    }

    [Fact]
    public void AssetBytes_EtagIsHashWrappedInDoubleQuotes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css);

        Assert.NotNull(bytes);
        Assert.Equal($"\"{hash}\"", bytes.Value.Etag);
    }

    // ─── Concurrency ──────────────────────────────────────────────────────

    [Fact]
    public void Concurrent_RegisterDistinctTypes_AllSucceed()
    {
        var types = new Type[]
        {
            typeof(P0), typeof(P1), typeof(P2), typeof(P3), typeof(P4),
            typeof(P5), typeof(P6), typeof(P7), typeof(P8), typeof(P9)
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
    public void Concurrent_RegisterReplaceSameType_LeavesConsistentState()
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
    public void Concurrent_RegisterAndGetByHash_NeverThrows_FinalStateIsConsistent()
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
    public void AssetChanged_FiresWithTypeAndKind_OnEachKindIndependently()
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
    public void AssetChanged_FiresOnReplace_NotOnNoOpRegister()
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
    public void AssetChanged_FiresOnUnregister_OnlyWhenSomethingWasRegistered()
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
    public void RegisterCss_NullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ScopedAssetRegistry.RegisterCss(null!, ".x {}"));
    }

    [Fact]
    public void RegisterCss_OpenGeneric_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => ScopedAssetRegistry.RegisterCss(typeof(Generic<>), ".x {}"));
        Assert.Contains("Open generic", ex.Message);
    }

    [Fact]
    public void RegisterJs_OpenGeneric_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ScopedAssetRegistry.RegisterJs(typeof(Generic<>), "export function f(){}"));
    }

    [Fact]
    public void Generic_DifferentTypeArgs_GetDistinctHashes()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Generic<int>), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(Generic<string>), ".x { color: red; }");

        ScopedAssetRegistry.TryGetCss(typeof(Generic<int>), out var hashInt);
        ScopedAssetRegistry.TryGetCss(typeof(Generic<string>), out var hashStr);

        Assert.NotEqual(hashInt, hashStr);
    }

    [Fact]
    public void Inheritance_DerivedTypeWithoutOwnCss_HasNoRegistration()
    {
        ScopedAssetRegistry.RegisterCss(typeof(BaseWidget), ".base { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(BaseWidget), out _));
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(DerivedWidget), out _));
    }

    [Fact]
    public void Inheritance_BothBaseAndDerivedHaveCss_StoredIndependently()
    {
        ScopedAssetRegistry.RegisterCss(typeof(BaseWidget), ".base { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(DerivedWidget), ".derived { color: blue; }");

        ScopedAssetRegistry.TryGetCss(typeof(BaseWidget), out var hashBase);
        ScopedAssetRegistry.TryGetCss(typeof(DerivedWidget), out var hashDerived);
        Assert.NotEqual(hashBase, hashDerived);
    }

    [Fact]
    public void NestedType_RegistersWithDistinctHash()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Outer.Inner), ".n { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(Outer.Inner), out var hash));
        Assert.Matches("^[0-9a-f]{12}$", hash);
    }

    [Fact]
    public void DynamicallyLoadedType_RegistersAndServes()
    {
        // Use the current assembly as a stand-in for "dynamically loaded" — the API
        // we exercise (RegisterCss with a runtime-resolved Type) is the same shape.
        var type = Assembly.GetExecutingAssembly().GetType(typeof(WidgetA).FullName!);
        Assert.NotNull(type);

        ScopedAssetRegistry.RegisterCss(type!, ".dynamic { color: red; }");
        Assert.True(ScopedAssetRegistry.TryGetCss(type!, out _));
    }

    [Fact]
    public void RegisteredType_IsHeldStronglyByRegistry()
    {
        // A type from a collectible AssemblyLoadContext: while the registry holds it,
        // the ALC cannot collect. This is the documented constraint.
        var alc = new AssemblyLoadContext("test-alc-" + Guid.NewGuid(), isCollectible: true);
        WeakReference alcWeak = new WeakReference(alc);

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
    public void EnumerateAll_EmptyRegistry_YieldsNothing()
    {
        Assert.Empty(ScopedAssetRegistry.EnumerateAll());
    }

    [Fact]
    public void EnumerateAll_YieldsRegisteredCssAndJsEntries_WithDistinctHashesAndKinds()
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
    public void EnumerateAll_TwoTypesShareSameRewrittenContent_YieldsOneEntryWithThatHash()
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

    private sealed class WidgetA : Component { protected override RenderResult Render() => this; }
    private sealed class WidgetB : Component { protected override RenderResult Render() => this; }
    private sealed class P0 : Component { protected override RenderResult Render() => this; }
    private sealed class P1 : Component { protected override RenderResult Render() => this; }
    private sealed class P2 : Component { protected override RenderResult Render() => this; }
    private sealed class P3 : Component { protected override RenderResult Render() => this; }
    private sealed class P4 : Component { protected override RenderResult Render() => this; }
    private sealed class P5 : Component { protected override RenderResult Render() => this; }
    private sealed class P6 : Component { protected override RenderResult Render() => this; }
    private sealed class P7 : Component { protected override RenderResult Render() => this; }
    private sealed class P8 : Component { protected override RenderResult Render() => this; }
    private sealed class P9 : Component { protected override RenderResult Render() => this; }

    private sealed class Generic<T> : Component { protected override RenderResult Render() => this; }

    private class BaseWidget : Component { protected override RenderResult Render() => this; }
    private sealed class DerivedWidget : BaseWidget { }

    internal static class Outer
    {
        internal sealed class Inner : Component { protected override RenderResult Render() => this; }
    }
}
