using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    public static EditContext? ResolveBindingContext(object model) =>
        EditContextScope.Current ?? LiveRenderContext.Current?.GetOrCreateEditContext(model);

    public static string DefaultInputType(Type propType)
    {
        var t = Nullable.GetUnderlyingType(propType) ?? propType;
        if (t == typeof(bool))
        {
            return "checkbox";
        }

        if (t == typeof(int) || t == typeof(long) || t == typeof(short) ||
            t == typeof(double) || t == typeof(float) || t == typeof(decimal))
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

    public static Func<string, Task> StringSetHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool validateOnSet) =>
        async raw =>
        {
            if (!TrySetTyped(acc, raw))
            {
                return;
            }

            ctx?.NotifyFieldChanged(fid);
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
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool setOnChange) =>
        async raw =>
        {
            if (setOnChange)
            {
                if (TrySetTyped(acc, raw))
                {
                    ctx?.NotifyFieldChanged(fid);
                }
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

    public static Func<string, Task> BoolToggleHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool wasChecked) =>
        async _ =>
        {
            acc.Setter(!wasChecked);
            ctx?.NotifyFieldChanged(fid);
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
                        "the page/model's public members.")]
    private static bool TrySetTyped(ExpressionAccessor.Accessor acc, string raw)
    {
        var t = acc.PropertyType;
        if (Nullable.GetUnderlyingType(t) is not null && string.IsNullOrEmpty(raw))
        {
            acc.Setter(null);
            return true;
        }

        if (t.IsEnum)
        {
            if (Enum.TryParse(t, raw, true, out var en))
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
}
