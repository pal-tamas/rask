using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Core.Forms;

// Shared building blocks for the Expression-driven binding factories that live as
// `[GenerateForwarderFactory] public static` methods on Input / Textarea / Select.
// The factories run during the parent's Render() — before HtmlSerializer enters the
// Form's EnterChildrenScope — so EditContextScope.Current is still null at that point.
// ResolveBindingContext falls back to LiveRenderContext.GetOrCreateEditContext (keyed
// by model reference), which yields the same instance Form will later push, so per-
// field NotifyFieldChanged / ValidateField from input handlers land in the right context.
internal static class BindingHelpers
{
    // Determines whether an empty form value should round-trip to `null`. Value types use
    // Nullable<T>; reference types (notably `string?`) carry C# nullable-annotation metadata
    // that NullabilityInfoContext reads off the PropertyInfo. Unknown (NRT disabled in the
    // model's compilation context) is treated as non-nullable, matching pre-NRT behavior.
    private static readonly NullabilityInfoContext _nullabilityCtx = new();

    public static EditContext? ResolveBindingContext(object model) =>
        EditContextScope.Current ?? LiveRenderContext.Current?.GetOrCreateEditContext(model);

    // Picks the <input type=…> attribute from the bound property's CLR type. Every numeric
    // primitive that .NET ships an IParsable<T> for maps to "number" so the browser supplies
    // the native numeric keypad and increment buttons; the actual round-trip through
    // RouteValueParser uses T.TryParse(raw, InvariantCulture, out) so the parse side is
    // already polymorphic across the whole set.
    public static string DefaultInputType(Type propType)
    {
        var t = Nullable.GetUnderlyingType(propType) ?? propType;
        if (t == typeof(bool))
        {
            return "checkbox";
        }

        if (t == typeof(byte) || t == typeof(sbyte)
                              || t == typeof(short) || t == typeof(ushort)
                              || t == typeof(int) || t == typeof(uint)
                              || t == typeof(long) || t == typeof(ulong)
                              || t == typeof(nint) || t == typeof(nuint)
                              || t == typeof(float) || t == typeof(double)
                              || t == typeof(decimal) || t == typeof(Half))
        {
            return "number";
        }

        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
        {
            return "datetime-local";
        }

        if (t == typeof(DateOnly))
        {
            return "date";
        }

        if (t == typeof(TimeOnly) || t == typeof(TimeSpan))
        {
            return "time";
        }

        return "text";
    }

    public static bool IsImmediateUpdateType(Type propType) =>
        (Nullable.GetUnderlyingType(propType) ?? propType) == typeof(string);

    public static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "";
        }

        return value switch
        {
            string s => s,
            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    // Collapses the typed AfterBind / AfterBindAsync pair from a `Bound<TProp>` factory into a
    // single non-generic Func<Task>? closure. Returns null when neither is supplied so the
    // handler builders can stay on the cheap no-callback path. The closure reads the now-set
    // value back through acc.Getter() (handlers fire afterBind *after* TrySetTyped), so the
    // user's callback always sees the new value.
    public static Func<Task>? BuildAfterBind<TProp>(
        ExpressionAccessor.Accessor acc,
        Action<TProp>? sync,
        Func<TProp, Task>? asyncFn)
    {
        if (sync is null && asyncFn is null)
        {
            return null;
        }

        return async () =>
        {
            var v = (TProp)acc.Getter()!;
            sync?.Invoke(v);
            if (asyncFn is not null)
            {
                await asyncFn(v).ConfigureAwait(false);
            }
        };
    }

    // `afterBind` is the post-bind side-effect hook (typically populated from the `AfterBind` /
    // `AfterBindAsync` factory parameters on Input/Select/Textarea). It only fires when a parse
    // actually mutated the model, so the user never sees a stale value; we run it after
    // NotifyFieldChanged so validators that observe IsModified see the new state.
    public static Func<string, Task> StringSetHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool validateOnSet,
        Func<Task>? afterBind = null) =>
        async raw =>
        {
            if (!TrySetTyped(acc, raw))
            {
                return;
            }

            ctx?.NotifyFieldChanged(fid);
            if (afterBind is not null)
            {
                await afterBind().ConfigureAwait(false);
            }

            // Blazor parity: stay quiet until the user (or a submit) has touched the field,
            // then re-validate on every keystroke so a correction clears the message
            // without needing a blur to trigger the change event.
            if (ctx is not null && (validateOnSet || ctx.IsTouched(fid)))
            {
                if (ctx.HasAsyncValidators)
                {
                    await ctx.ValidateFieldAsync(fid).ConfigureAwait(false);
                }
                else
                {
                    ctx.ValidateField(fid);
                }
            }
        };

    public static Func<string, Task> TouchAndValidateHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool setOnChange,
        Func<Task>? afterBind = null) =>
        async raw =>
        {
            var didBind = false;
            if (setOnChange)
            {
                if (TrySetTyped(acc, raw))
                {
                    ctx?.NotifyFieldChanged(fid);
                    didBind = true;
                }
            }

            if (didBind && afterBind is not null)
            {
                await afterBind().ConfigureAwait(false);
            }

            ctx?.NotifyFieldTouched(fid);
            if (ctx is not null)
            {
                if (ctx.HasAsyncValidators)
                {
                    await ctx.ValidateFieldAsync(fid).ConfigureAwait(false);
                }
                else
                {
                    ctx.ValidateField(fid);
                }
            }
        };

    // Checkbox binding sets the model to the checkbox's actual checked state reported by
    // the client (rask.js sends "true"/"false" for type=checkbox), rather than flipping a
    // state captured at render time. Setting (not toggling) makes the binding self-
    // correcting: every change event carries the absolute checked state, so the model
    // re-syncs to the live checkbox even if an intermediate render/diff was coalesced or
    // missed. The prior blind-toggle (acc.Setter(!wasChecked)) could not recover from a
    // one-step desync between el.checked and the server model — once drifted it kept
    // inverting, which surfaced as the checkbox "sticking" after a few clicks once those
    // clicks started shipping diffs (which don't re-base an unchanged checked attribute)
    // instead of full HTML (whose morph re-based it every time).
    public static Func<string, Task> BoolSetHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid,
        Func<Task>? afterBind = null) =>
        async value =>
        {
            // Empty / unparseable → false (an unchecked box reports "false"); bool.TryParse
            // accepts "true"/"false" case-insensitively. Boxing the bool satisfies both
            // `bool` and `bool?` properties; null is never reproduced from the UI (HTML
            // checkboxes have no indeterminate state — matches Blazor's <input type=checkbox>).
            acc.Setter(bool.TryParse(value, out var b) && b);
            ctx?.NotifyFieldChanged(fid);
            if (afterBind is not null)
            {
                await afterBind().ConfigureAwait(false);
            }

            ctx?.NotifyFieldTouched(fid);
            if (ctx is not null)
            {
                if (ctx.HasAsyncValidators)
                {
                    await ctx.ValidateFieldAsync(fid).ConfigureAwait(false);
                }
                else
                {
                    ctx.ValidateField(fid);
                }
            }
        };

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "acc.PropertyType is the model property's static type; if the user marks the [RouteParam]/" +
                        "[QueryParam] / form-bound property correctly (RASK011 enforces IParsable<T> or string), " +
                        "TryParse's IL2060/IL2070 demands are met by the same DynamicDependency that preserves " +
                        "the page/model's public members. Activator.CreateInstance(t) on the empty-input path is " +
                        "only reached for value types and returns a zero-initialised instance without invoking a " +
                        "constructor, so it carries no member-access demand.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Same rationale as IL2072 above — the value-type default path uses Activator.CreateInstance " +
                        "on acc.PropertyType, which the trimmer cannot prove is a value type at compile time.")]
    private static bool TrySetTyped(ExpressionAccessor.Accessor acc, string raw)
    {
        var t = acc.PropertyType;
        var underlying = Nullable.GetUnderlyingType(t);
        if (string.IsNullOrEmpty(raw))
        {
            if (IsNullableProperty(acc.Property, underlying, t))
            {
                acc.Setter(null);
                return true;
            }

            // Non-nullable value type: empty raw maps to default(T) so the user can clear a
            // number/date/enum input. Without this, IParsable<T>.TryParse("") fails, the model
            // keeps its prior value, and the next render snaps the cleared input back to it.
            // Non-nullable reference types (string with non-nullable NRT) fall through —
            // RouteValueParser returns "" verbatim for string.
            if (t.IsValueType)
            {
                acc.Setter(Activator.CreateInstance(t));
                return true;
            }
        }

        // Enum.TryParse needs the unwrapped type — Nullable<TEnum>.IsEnum is false, so a
        // nullable enum would otherwise fall through to RouteValueParser, which only
        // probes IParsable<T> (enums don't expose it through reflection's interface table).
        var effective = underlying ?? t;
        if (effective.IsEnum)
        {
            if (Enum.TryParse(effective, raw, true, out var en))
            {
                acc.Setter(en);
                return true;
            }

            return false;
        }

        if (RouteValueParser.TryParse(t, raw, out var parsed))
        {
            acc.Setter(parsed);
            return true;
        }

        return false;
    }

    private static bool IsNullableProperty(PropertyInfo prop, Type? underlying, Type type)
    {
        if (underlying is not null)
        {
            return true;
        }

        if (type.IsValueType)
        {
            return false;
        }

        NullabilityInfo info;
        lock (_nullabilityCtx)
        {
            info = _nullabilityCtx.Create(prop);
        }

        return info.WriteState == NullabilityState.Nullable;
    }
}
