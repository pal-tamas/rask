namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiMenuItem" /> or a <see cref="UiNavmenuItem" /> is drawn.</summary>
    public enum MenuItemVariant
    {
        /// <summary>An ordinary row.</summary>
        Default,

        /// <summary>A destructive action: red under the pointer and under the keyboard cursor.</summary>
        Danger,
    }
}
