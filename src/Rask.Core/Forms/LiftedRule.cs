namespace Rask.Core.Forms;

// A control over a value type — Ui.Checkbox and Ui.Switch over bool, a slider over double — binds a property of
// its nullable twin through ExpressionAccessor.NonNullable, because every member of a generated form model is
// nullable. The edit context hands a rule the PROPERTY's value, not the control's, so the rule the model
// registered over T? is the right one to ask, as it is: a null is the property's own empty state and its rule
// says what that means. Only a rule the field wrote over T has to be kept from a null it cannot take.
internal static class LiftedRule
{
    // Whether the rule is one of the two shapes over T?, for a control bound as T.
    public static bool IsOverNullable<T>(Delegate rule)
    {
        if (!typeof(T).IsValueType || rule.GetType() is not { IsGenericType: true } shape)
        {
            return false;
        }

        var arguments = shape.GetGenericArguments();
        var definition = shape.GetGenericTypeDefinition();
        var takes = definition == typeof(Validate<>)
                    || (definition == typeof(Func<,>) && arguments[1] == typeof(ValueTask<IEnumerable<string>>));

        return takes && Nullable.GetUnderlyingType(arguments[0]) == typeof(T);
    }

    // The property's own rule, then the one the field wrote — over the boxed value both are handed anyway, so the
    // pair needs no T? to name. The written rule is not asked about a property that holds nothing.
    public static Delegate Then(Delegate own, Delegate written) =>
        RuleSequence<object?>.Of(Boxed(own, asksAboutNull: true), Boxed(written, asksAboutNull: false));

    private static Delegate Boxed(Delegate rule, bool asksAboutNull)
    {
        if (DelegateValidator.IsAsync(rule))
        {
            return new Func<object?, ValueTask<IEnumerable<string>>>(value => value is null && !asksAboutNull
                ? new ValueTask<IEnumerable<string>>([])
                : DelegateValidator.InvokeAsync(rule, value, Ambient.CancellationToken));
        }

        return new Validate<object?>(value => value is null && !asksAboutNull ? [] : DelegateValidator.InvokeSync(rule, value));
    }
}
