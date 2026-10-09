using System.Globalization;

namespace Rask;

/// <summary>
/// Flux UI's OTP input: one cell of a <see cref="UiOtp" />, placed by hand to put separators or groups between cells.
/// </summary>
/// <remarks>
/// It takes nothing: what it accepts and whether it is masked come from the <c>Ui.Otp</c> it is inside. It is a
/// real text input, named "Character 2 of 6" for assistive tech, and it is rendered with no value and no
/// handler: what it holds is the runtime's (<c>data-rask-otp</c> on the group), which keeps the group's one
/// bound field equal to the cells.
/// </remarks>
public sealed partial class UiOtpInput : Component
{
    private const string Look =
        "block w-8 h-10 py-3 px-0 appearance-none text-center text-base sm:text-sm leading-[1.375rem] rounded-lg "
        + UiInputLook.Outline + " " + UiInputLook.InputShadow;

    /// <summary>Which cell of the code this is, counted by the <see cref="UiOtp" /> that holds it.</summary>
    internal int Cell { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiOtpScope>() is not { } scope)
        {
            return null;
        }

        var cell = Cell;
        var input = Input.Of<string>()
            .Type(scope.Private ? InputType.Password : InputType.Text)
            .Id(cell == 0 ? scope.FirstId : null)
            .TabIndex(cell == scope.Stop ? 0 : -1)
            .Disabled(scope.Disabled)
            .Aria("label", RaskStrings.Get(RaskString.OtpCharacter, "Character {0} of {1}", cell + 1, scope.Total))
            .Attributes(Marks(scope, cell))
            .Class(Look);

        return Div.Class("relative block").Data("ui-input", "")[input];
    }

    private static Dictionary<string, string?> Marks(UiOtpScope scope, int cell)
    {
        var marks = new Dictionary<string, string?>(UiInputLook.Marks(scope.Invalid), StringComparer.Ordinal)
        {
            ["data-ui-otp-input"] = null,
            ["autocomplete"] = cell == 0 ? scope.Autocomplete : "off",
        };

        if (scope.Mode == Ui.OtpMode.Numeric)
        {
            marks["inputmode"] = "numeric";
        }

        return marks;
    }
}
