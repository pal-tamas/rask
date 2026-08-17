using System.Globalization;
using CoreGraphics;
using Foundation;
using Rask.Native.Components;
using Rask.Native.Surface;
using UIKit;

namespace Rask.Native;

// The iOS half of a pure-native surface: how to make each UIView, how to configure one prop, and how to
// reorder a container's children. Everything structural — the retained tree, path resolution, ordered patch
// replay — is NativeSurfaceHost<UIView>'s, and is unit-tested on plain net10.0; this file is a mapping table.
//
// Layout is Auto Layout via UIStackView throughout: every container IS a stack, so sizing falls out of the
// intrinsic content sizes of the leaves and there is no frame math to keep in sync. Scroll and List wrap a
// stack in a scroll view, which is why InsertChild has to look at parentKind rather than always using the
// view it was handed.
internal sealed class UiKitViewOps(Action<NativeSurfaceEvent> raise) : INativeViewOps<UIView>
{
    private readonly Action<NativeSurfaceEvent> _raise = raise;

    public UIView Create(NativeNodeKind kind) => kind switch
    {
        NativeNodeKind.Screen => NewStack(UILayoutConstraintAxis.Vertical),
        NativeNodeKind.Stack => NewStack(UILayoutConstraintAxis.Vertical),
        NativeNodeKind.Scroll or NativeNodeKind.List => new RaskScrollView(),
        NativeNodeKind.Label => new RaskLabel { Lines = 0, TranslatesAutoresizingMaskIntoConstraints = false },
        NativeNodeKind.Button => NewButton(),
        NativeNodeKind.TextField => NewTextField(),
        NativeNodeKind.Switch => NewSwitch(),
        NativeNodeKind.Image => new UIImageView
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TranslatesAutoresizingMaskIntoConstraints = false,
        },
        NativeNodeKind.ActivityIndicator => NewSpinner(),
        NativeNodeKind.Divider => NewDivider(),
        NativeNodeKind.Spacer => new UIView { TranslatesAutoresizingMaskIntoConstraints = false },
        _ => new UIView { TranslatesAutoresizingMaskIntoConstraints = false },
    };

    public void SetProp(UIView view, NativeNodeKind kind, NativePropId id, NativePropValue value)
    {
        var unset = value.Kind == NativePropKind.None;
        switch (id)
        {
            case NativePropId.Text:
                switch (view)
                {
                    case UILabel label:
                        label.Text = unset ? null : value.Text;
                        break;
                    case UIButton button:
                        button.SetTitle(unset ? null : value.Text, UIControlState.Normal);
                        break;
                    case UITextField field:
                        // Controlled input: only write when it actually differs, or assigning mid-edit moves
                        // the caret to the end on every keystroke.
                        var text = unset ? string.Empty : value.Text ?? string.Empty;
                        if (!string.Equals(field.Text, text, StringComparison.Ordinal))
                        {
                            field.Text = text;
                        }

                        break;
                }

                break;

            case NativePropId.Placeholder when view is UITextField placeholderField:
                placeholderField.Placeholder = unset ? null : value.Text;
                break;

            case NativePropId.Orientation when view is UIStackView orientationStack:
                orientationStack.Axis = !unset && (int)value.Number == (int)NativeOrientation.Horizontal
                    ? UILayoutConstraintAxis.Horizontal
                    : UILayoutConstraintAxis.Vertical;
                break;

            case NativePropId.Spacing when view is UIStackView spacingStack:
                spacingStack.Spacing = unset ? 0 : (nfloat)value.Number;
                break;

            case NativePropId.Padding:
                ApplyPadding(view, unset ? 0 : value.Number);
                break;

            case NativePropId.Alignment when view is UIStackView alignStack:
                alignStack.Alignment = unset
                    ? UIStackViewAlignment.Fill
                    : (NativeAlignment)(int)value.Number switch
                    {
                        NativeAlignment.Start => UIStackViewAlignment.Leading,
                        NativeAlignment.Center => UIStackViewAlignment.Center,
                        NativeAlignment.End => UIStackViewAlignment.Trailing,
                        _ => UIStackViewAlignment.Fill,
                    };
                break;

            case NativePropId.FontSize or NativePropId.FontWeight when view is UILabel fontLabel:
                ApplyFont(fontLabel, id, value, unset);
                break;

            case NativePropId.TextAlign when view is UILabel alignLabel:
                alignLabel.TextAlignment = unset
                    ? UITextAlignment.Natural
                    : (NativeTextAlign)(int)value.Number switch
                    {
                        NativeTextAlign.Center => UITextAlignment.Center,
                        NativeTextAlign.End => UITextAlignment.Right,
                        _ => UITextAlignment.Natural,
                    };
                break;

            case NativePropId.Lines when view is UILabel linesLabel:
                linesLabel.Lines = unset ? 0 : (nint)value.Number;
                break;

            case NativePropId.Color:
                ApplyForeground(view, unset ? null : RaskChromeContainerView.ResolveUIColor(value.Text));
                break;

            case NativePropId.Background:
                view.BackgroundColor = unset ? null : RaskChromeContainerView.ResolveUIColor(value.Text);
                break;

            case NativePropId.Style when view is UIButton styleButton:
                ApplyButtonStyle(styleButton, unset ? NativeButtonStyle.Filled : (NativeButtonStyle)(int)value.Number);
                break;

            case NativePropId.Source when view is UIImageView imageView:
                // A bundled asset-catalog entry paints on the first frame and works offline; a URL is not
                // fetched here — an app that needs one loads it itself and feeds the bytes in.
                imageView.Image = unset ? null : UIImage.FromBundle(value.Text ?? string.Empty);
                break;

            case NativePropId.ContentMode when view is UIImageView modeView:
                modeView.ContentMode = unset
                    ? UIViewContentMode.ScaleAspectFit
                    : (NativeContentMode)(int)value.Number switch
                    {
                        NativeContentMode.Fill => UIViewContentMode.ScaleAspectFill,
                        NativeContentMode.Center => UIViewContentMode.Center,
                        _ => UIViewContentMode.ScaleAspectFit,
                    };
                break;

            case NativePropId.Secure when view is UITextField secureField:
                secureField.SecureTextEntry = !unset && value.Flag;
                break;

            case NativePropId.Keyboard when view is UITextField keyboardField:
                keyboardField.KeyboardType = unset
                    ? UIKeyboardType.Default
                    : (NativeKeyboardType)(int)value.Number switch
                    {
                        NativeKeyboardType.Email => UIKeyboardType.EmailAddress,
                        NativeKeyboardType.Number => UIKeyboardType.NumberPad,
                        NativeKeyboardType.Phone => UIKeyboardType.PhonePad,
                        NativeKeyboardType.Url => UIKeyboardType.Url,
                        _ => UIKeyboardType.Default,
                    };
                break;

            case NativePropId.On when view is UISwitch toggle:
                var on = !unset && value.Flag;
                if (toggle.On != on)
                {
                    toggle.SetState(on, animated: true);
                }

                break;

            case NativePropId.Enabled when view is UIControl control:
                control.Enabled = unset || value.Flag;
                break;

            case NativePropId.Animating when view is UIActivityIndicatorView spinner:
                if (unset || value.Flag)
                {
                    spinner.StartAnimating();
                }
                else
                {
                    spinner.StopAnimating();
                }

                break;

            case NativePropId.Width:
                ApplyDimension(view, NSLayoutAttribute.Width, unset ? null : value.Number);
                break;

            case NativePropId.Height:
                ApplyDimension(view, NSLayoutAttribute.Height, unset ? null : value.Number);
                break;

            case NativePropId.TapId:
                SetHandlerId(view, unset ? -1 : (int)value.Number, tap: true);
                break;

            case NativePropId.ChangeId:
                SetHandlerId(view, unset ? -1 : (int)value.Number, tap: false);
                break;

            case NativePropId.AccessibilityId:
                view.AccessibilityIdentifier = unset ? null : value.Text;
                view.AccessibilityLabel = unset ? null : value.Text;
                view.IsAccessibilityElement = !unset;
                break;
        }
    }

    public void InsertChild(UIView parent, NativeNodeKind parentKind, UIView child, int index) =>
        ContentOf(parent).InsertArrangedSubview(child, (nuint)index);

    public void RemoveChild(UIView parent, NativeNodeKind parentKind, UIView child, int index)
    {
        ContentOf(parent).RemoveArrangedSubview(child);
        // RemoveArrangedSubview only stops the stack laying it out; without this it stays on screen.
        child.RemoveFromSuperview();
    }

    public void MoveChild(UIView parent, NativeNodeKind parentKind, UIView child, int fromIndex, int toIndex)
    {
        var content = ContentOf(parent);
        content.RemoveArrangedSubview(child);
        content.InsertArrangedSubview(child, (nuint)toIndex);
    }

    // Scroll and List host their children in an inner stack; every other container IS the stack.
    private static UIStackView ContentOf(UIView parent) =>
        parent is RaskScrollView scroll ? scroll.Content : (UIStackView)parent;

    private static UIStackView NewStack(UILayoutConstraintAxis axis) => new()
    {
        Axis = axis,
        Alignment = UIStackViewAlignment.Fill,
        Distribution = UIStackViewDistribution.Fill,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    private UIButton NewButton()
    {
        var button = new RaskButton { TranslatesAutoresizingMaskIntoConstraints = false };
        ApplyButtonStyle(button, NativeButtonStyle.Filled);
        button.TouchUpInside += (s, _) =>
        {
            if (s is RaskButton { TapId: >= 0 } b)
            {
                _raise(new NativeSurfaceEvent(b.TapId, NativeSurfaceEventKind.Tap, null));
            }
        };
        return button;
    }

    private UITextField NewTextField()
    {
        var field = new RaskTextField
        {
            BorderStyle = UITextBorderStyle.RoundedRect,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        field.EditingChanged += (s, _) =>
        {
            if (s is RaskTextField { ChangeId: >= 0 } f)
            {
                _raise(new NativeSurfaceEvent(f.ChangeId, NativeSurfaceEventKind.Change, f.Text ?? string.Empty));
            }
        };
        return field;
    }

    private UISwitch NewSwitch()
    {
        var toggle = new RaskSwitch { TranslatesAutoresizingMaskIntoConstraints = false };
        toggle.ValueChanged += (s, _) =>
        {
            if (s is RaskSwitch { ChangeId: >= 0 } t)
            {
                _raise(new NativeSurfaceEvent(
                    t.ChangeId,
                    NativeSurfaceEventKind.Change,
                    t.On ? "true" : "false"));
            }
        };
        return toggle;
    }

    private static UIActivityIndicatorView NewSpinner()
    {
        var spinner = new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Medium)
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        spinner.StartAnimating();
        return spinner;
    }

    private static UIView NewDivider()
    {
        var rule = new UIView
        {
            BackgroundColor = UIColor.Separator,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        // One physical pixel, so it stays a hairline at every screen scale.
        rule.HeightAnchor.ConstraintEqualTo(1f / UIScreen.MainScreen.Scale).Active = true;
        return rule;
    }

    // A tap belongs to the whole box for a stack, so it needs a recognizer rather than a control target.
    private void SetHandlerId(UIView view, int id, bool tap)
    {
        switch (view)
        {
            case RaskButton button when tap:
                button.TapId = id;
                break;
            case RaskTextField field when !tap:
                field.ChangeId = id;
                break;
            case RaskSwitch toggle when !tap:
                toggle.ChangeId = id;
                break;
            case UIStackView stack when tap:
                AttachStackTap(stack, id);
                break;
        }
    }

    private void AttachStackTap(UIStackView stack, int id)
    {
        // Re-attaching on every prop write would stack recognizers up; the id is captured in a box the
        // recognizer reads, so the same recognizer serves every later id change.
        if (stack.GestureRecognizers?.OfType<RaskTapRecognizer>().FirstOrDefault() is { } existing)
        {
            existing.HandlerId = id;
            existing.Enabled = id >= 0;
            return;
        }

        if (id < 0)
        {
            return;
        }

        var recognizer = new RaskTapRecognizer { HandlerId = id };
        recognizer.AddTarget(() =>
        {
            if (recognizer.HandlerId >= 0)
            {
                _raise(new NativeSurfaceEvent(recognizer.HandlerId, NativeSurfaceEventKind.Tap, null));
            }
        });
        stack.AddGestureRecognizer(recognizer);
        stack.UserInteractionEnabled = true;
    }

    private static void ApplyPadding(UIView view, double padding)
    {
        var stack = view as UIStackView ?? (view as RaskScrollView)?.Content;
        if (stack is null)
        {
            return;
        }

        stack.LayoutMarginsRelativeArrangement = padding > 0;
        stack.LayoutMargins = new UIEdgeInsets(
            (nfloat)padding, (nfloat)padding, (nfloat)padding, (nfloat)padding);
    }

    private static void ApplyForeground(UIView view, UIColor? color)
    {
        switch (view)
        {
            case UILabel label:
                label.TextColor = color ?? UIColor.Label;
                break;
            case UIButton button:
                button.SetTitleColor(color, UIControlState.Normal);
                break;
            case UIActivityIndicatorView spinner:
                spinner.Color = color;
                break;
            default:
                view.BackgroundColor = color ?? view.BackgroundColor;
                break;
        }
    }

    // Size and weight are two separate props but one UIFont, so the label remembers both: reading the weight
    // back off the font is lossy, and without it a later FontSize change would silently reset a weight the
    // app had already set.
    private static void ApplyFont(UILabel label, NativePropId id, NativePropValue value, bool unset)
    {
        if (label is not RaskLabel typed)
        {
            return;
        }

        if (id == NativePropId.FontSize)
        {
            typed.FontSize = unset ? UIFont.LabelFontSize : (nfloat)value.Number;
        }
        else
        {
            typed.Weight = unset ? NativeFontWeight.Regular : (NativeFontWeight)(int)value.Number;
        }

        var weight = typed.Weight switch
        {
            NativeFontWeight.Medium => UIFontWeight.Medium,
            NativeFontWeight.Semibold => UIFontWeight.Semibold,
            NativeFontWeight.Bold => UIFontWeight.Bold,
            _ => UIFontWeight.Regular,
        };
        typed.Font = UIFont.SystemFontOfSize(typed.FontSize, weight);
    }

    private static void ApplyButtonStyle(UIButton button, NativeButtonStyle style)
    {
        var configuration = style switch
        {
            NativeButtonStyle.Tinted => UIButtonConfiguration.TintedButtonConfiguration,
            NativeButtonStyle.Plain => UIButtonConfiguration.PlainButtonConfiguration,
            NativeButtonStyle.Destructive => UIButtonConfiguration.FilledButtonConfiguration,
            _ => UIButtonConfiguration.FilledButtonConfiguration,
        };
        if (style == NativeButtonStyle.Destructive)
        {
            configuration.BaseBackgroundColor = UIColor.SystemRed;
        }

        button.Configuration = configuration;
    }

    private static void ApplyDimension(UIView view, NSLayoutAttribute attribute, double? points)
    {
        var anchor = attribute == NSLayoutAttribute.Width ? view.WidthAnchor : view.HeightAnchor;
        // Re-use the constraint we installed rather than adding another, or repeated prop writes would pile
        // up mutually unsatisfiable constraints and Auto Layout would start breaking them at random.
        var existing = view.Constraints.FirstOrDefault(c =>
            c.FirstAttribute == attribute && c.Relation == NSLayoutRelation.Equal && c.SecondItem is null);

        if (points is null)
        {
            if (existing is not null)
            {
                existing.Active = false;
            }

            return;
        }

        if (existing is not null)
        {
            existing.Constant = (nfloat)points.Value;
            existing.Active = true;
            return;
        }

        anchor.ConstraintEqualTo((nfloat)points.Value).Active = true;
    }
}

// Small carriers for the handler id, so an event knows which delegate to run without a side table keyed by
// NSObject (which would keep views alive and complicate teardown).
internal sealed class RaskButton : UIButton
{
    public int TapId { get; set; } = -1;
}

// Remembers the two halves of its font, which UIFont does not let you read back cleanly.
internal sealed class RaskLabel : UILabel
{
    public nfloat FontSize { get; set; } = UIFont.LabelFontSize;

    public NativeFontWeight Weight { get; set; } = NativeFontWeight.Regular;
}

internal sealed class RaskTextField : UITextField
{
    public int ChangeId { get; set; } = -1;
}

internal sealed class RaskSwitch : UISwitch
{
    public int ChangeId { get; set; } = -1;
}

internal sealed class RaskTapRecognizer : UITapGestureRecognizer
{
    public int HandlerId { get; set; } = -1;
}

// A scroll view with a stack inside it, pinned so it scrolls along its own axis and fills the other.
internal sealed class RaskScrollView : UIScrollView
{
    public RaskScrollView()
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        Content = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        AddSubview(Content);
        NSLayoutConstraint.ActivateConstraints(
        [
            Content.TopAnchor.ConstraintEqualTo(ContentLayoutGuide.TopAnchor),
            Content.BottomAnchor.ConstraintEqualTo(ContentLayoutGuide.BottomAnchor),
            Content.LeadingAnchor.ConstraintEqualTo(ContentLayoutGuide.LeadingAnchor),
            Content.TrailingAnchor.ConstraintEqualTo(ContentLayoutGuide.TrailingAnchor),
            // Without this the stack has no defined width and lays out at zero.
            Content.WidthAnchor.ConstraintEqualTo(FrameLayoutGuide.WidthAnchor),
        ]);
    }

    public UIStackView Content { get; }
}
