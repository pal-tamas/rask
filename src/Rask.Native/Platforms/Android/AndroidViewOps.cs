using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Rask.Native.Components;
using Rask.Native.Surface;
using AndroidOrientation = Android.Widget.Orientation;
using AndroidView = Android.Views.View;

namespace Rask.Native;

// The Android half of a pure-native surface — the mirror of UiKitViewOps. Everything structural (retained
// tree, path resolution, ordered patch replay) is NativeSurfaceHost<AndroidView>'s and is unit-tested on
// plain net10.0; this file is a mapping table from NativeNodeKind/NativePropId onto framework widgets.
//
// Framework widgets only (no AndroidX/Material dependency), matching the chrome bars in
// RaskAndroidWebView.Chrome.cs — so this compiles and themes with the default app theme.
internal sealed class AndroidViewOps(Context context, Action<NativeSurfaceEvent> raise)
    : INativeViewOps<AndroidView>
{
    private readonly Context _context = context;
    private readonly Action<NativeSurfaceEvent> _raise = raise;

    public AndroidView Create(NativeNodeKind kind) => kind switch
    {
        NativeNodeKind.Screen or NativeNodeKind.Stack => new RaskStack(_context),
        NativeNodeKind.Scroll or NativeNodeKind.List => new RaskScrollView(_context),
        NativeNodeKind.Label => new TextView(_context),
        NativeNodeKind.Button => NewButton(),
        NativeNodeKind.TextField => NewTextField(),
        NativeNodeKind.Switch => NewSwitch(),
        NativeNodeKind.Image => new ImageView(_context),
        NativeNodeKind.ActivityIndicator => new ProgressBar(_context) { Indeterminate = true },
        NativeNodeKind.Divider => NewDivider(),
        NativeNodeKind.Spacer => new AndroidView(_context),
        _ => new AndroidView(_context),
    };

    public void SetProp(AndroidView view, NativeNodeKind kind, NativePropId id, NativePropValue value)
    {
        var unset = value.Kind == NativePropKind.None;
        switch (id)
        {
            case NativePropId.Text:
                switch (view)
                {
                    case RaskEditText field:
                        // Controlled input: writing the same text back mid-edit would move the caret to the
                        // end on every keystroke, so only write a genuine change — and suppress the change
                        // event while doing it, or the write would echo back as user input.
                        var text = unset ? string.Empty : value.Text ?? string.Empty;
                        if (!string.Equals(field.Text, text, StringComparison.Ordinal))
                        {
                            field.Suppress = true;
                            field.Text = text;
                            field.SetSelection(text.Length);
                            field.Suppress = false;
                        }

                        break;
                    case TextView textView:
                        textView.Text = unset ? string.Empty : value.Text;
                        break;
                }

                break;

            case NativePropId.Placeholder when view is EditText placeholder:
                placeholder.Hint = unset ? null : value.Text;
                break;

            case NativePropId.Orientation when view is RaskStack orientationStack:
                orientationStack.Orientation =
                    !unset && (int)value.Number == (int)NativeOrientation.Horizontal
                        ? AndroidOrientation.Horizontal
                        : AndroidOrientation.Vertical;
                orientationStack.RefreshChildLayout();
                break;

            case NativePropId.Spacing when view is RaskStack spacingStack:
                spacingStack.SpacingPx = unset ? 0 : ToPx(value.Number);
                spacingStack.RefreshChildLayout();
                break;

            case NativePropId.Padding:
                var pad = unset ? 0 : ToPx(value.Number);
                ContentOf(view).SetPadding(pad, pad, pad, pad);
                break;

            case NativePropId.Alignment when view is RaskStack alignStack:
                alignStack.CrossAlignment = unset ? NativeAlignment.Stretch : (NativeAlignment)(int)value.Number;
                alignStack.RefreshChildLayout();
                break;

            case NativePropId.FontSize when view is TextView sizeText:
                sizeText.SetTextSize(
                    Android.Util.ComplexUnitType.Sp, unset ? 14f : (float)value.Number);
                break;

            case NativePropId.FontWeight when view is TextView weightText:
                var weight = unset ? NativeFontWeight.Regular : (NativeFontWeight)(int)value.Number;
                weightText.SetTypeface(
                    null,
                    weight is NativeFontWeight.Bold or NativeFontWeight.Semibold
                        ? TypefaceStyle.Bold
                        : TypefaceStyle.Normal);
                break;

            case NativePropId.TextAlign when view is TextView alignText:
                alignText.TextAlignment = unset
                    ? Android.Views.TextAlignment.ViewStart
                    : (NativeTextAlign)(int)value.Number switch
                    {
                        NativeTextAlign.Center => Android.Views.TextAlignment.Center,
                        NativeTextAlign.End => Android.Views.TextAlignment.ViewEnd,
                        _ => Android.Views.TextAlignment.ViewStart,
                    };
                break;

            case NativePropId.Lines when view is TextView linesText:
                var lines = unset ? 0 : (int)value.Number;
                linesText.SetMaxLines(lines <= 0 ? int.MaxValue : lines);
                break;

            // Ahead of the generic colour cases on purpose. A FULL node build was always fine here —
            // WriteSurfaceProps emits Style before Color and Background, so the colours land after the
            // style and win. The INCREMENTAL path was not: NativeTreeDiffer.DiffProps carries only the
            // props that actually changed, so a frame that changes Style alone arrived as Style alone,
            // and ApplyButtonStyle rewrote both colours over values it was never sent (#785). Holding
            // all three on the view and repainting the whole appearance is what makes a partial patch
            // land the same as a full one. Every button Create() makes is a RaskButton.
            case NativePropId.Color or NativePropId.Background or NativePropId.Style
                when view is RaskButton appearanceButton:
                if (appearanceButton.ButtonAppearance.Write(id, value, unset))
                {
                    ApplyButtonAppearance(appearanceButton);
                }

                break;

            case NativePropId.Color:
                ApplyForeground(view, unset ? null : ResolveColor(value.Text));
                break;

            case NativePropId.Background:
                if (unset)
                {
                    view.SetBackgroundColor(Color.Transparent);
                }
                else if (ResolveColor(value.Text) is { } background)
                {
                    view.SetBackgroundColor(background);
                }

                break;

            case NativePropId.Source when view is ImageView image:
                var drawable = unset ? 0 : ResolveDrawable(value.Text);
                if (drawable != 0)
                {
                    image.SetImageResource(drawable);
                }
                else
                {
                    image.SetImageDrawable(null);
                }

                break;

            case NativePropId.ContentMode when view is ImageView modeImage:
                modeImage.SetScaleType(unset
                    ? ImageView.ScaleType.FitCenter
                    : (NativeContentMode)(int)value.Number switch
                    {
                        NativeContentMode.Fill => ImageView.ScaleType.CenterCrop,
                        NativeContentMode.Center => ImageView.ScaleType.Center,
                        _ => ImageView.ScaleType.FitCenter,
                    });
                break;

            case NativePropId.Secure when view is EditText secure:
                secure.InputType = !unset && value.Flag
                    ? Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword
                    : Android.Text.InputTypes.ClassText;
                break;

            case NativePropId.Keyboard when view is EditText keyboard:
                keyboard.InputType = unset
                    ? Android.Text.InputTypes.ClassText
                    : (NativeKeyboardType)(int)value.Number switch
                    {
                        NativeKeyboardType.Email =>
                            Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationEmailAddress,
                        NativeKeyboardType.Number => Android.Text.InputTypes.ClassNumber,
                        NativeKeyboardType.Phone => Android.Text.InputTypes.ClassPhone,
                        NativeKeyboardType.Url =>
                            Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationUri,
                        _ => Android.Text.InputTypes.ClassText,
                    };
                break;

            case NativePropId.On when view is RaskSwitch toggle:
                var on = !unset && value.Flag;
                if (toggle.Checked != on)
                {
                    toggle.Suppress = true;
                    toggle.Checked = on;
                    toggle.Suppress = false;
                }

                break;

            case NativePropId.Enabled:
                view.Enabled = unset || value.Flag;
                break;

            case NativePropId.Animating when view is ProgressBar spinner:
                spinner.Visibility = unset || value.Flag ? ViewStates.Visible : ViewStates.Gone;
                break;

            case NativePropId.Width:
                SetDimension(view, unset ? null : ToPx(value.Number), width: true);
                break;

            case NativePropId.Height:
                SetDimension(view, unset ? null : ToPx(value.Number), width: false);
                break;

            case NativePropId.TapId:
                SetTapId(view, unset ? -1 : (int)value.Number);
                break;

            case NativePropId.ChangeId:
                switch (view)
                {
                    case RaskEditText changeField:
                        changeField.ChangeId = unset ? -1 : (int)value.Number;
                        break;
                    case RaskSwitch changeSwitch:
                        changeSwitch.ChangeId = unset ? -1 : (int)value.Number;
                        break;
                }

                break;

            case NativePropId.AccessibilityId:
                view.ContentDescription = unset ? null : value.Text;
                break;
        }
    }

    public void InsertChild(AndroidView parent, NativeNodeKind parentKind, AndroidView child, int index)
    {
        var content = ContentOf(parent);
        content.AddView(child, index);
        (content as RaskStack)?.RefreshChildLayout();
    }

    public void RemoveChild(AndroidView parent, NativeNodeKind parentKind, AndroidView child, int index)
    {
        var content = ContentOf(parent);
        content.RemoveViewAt(index);
        (content as RaskStack)?.RefreshChildLayout();
    }

    public void MoveChild(
        AndroidView parent, NativeNodeKind parentKind, AndroidView child, int fromIndex, int toIndex)
    {
        var content = ContentOf(parent);
        content.RemoveViewAt(fromIndex);
        content.AddView(child, toIndex);
        (content as RaskStack)?.RefreshChildLayout();
    }

    // Scroll and List host their children in an inner stack; every other container IS the stack.
    private static ViewGroup ContentOf(AndroidView parent) =>
        parent is RaskScrollView scroll ? scroll.Content : (ViewGroup)parent;

    private Button NewButton()
    {
        var button = new RaskButton(_context);
        ApplyButtonAppearance(button);
        button.Click += (s, _) =>
        {
            if (s is RaskButton { TapId: >= 0 } b)
            {
                _raise(new NativeSurfaceEvent(b.TapId, NativeSurfaceEventKind.Tap, null));
            }
        };
        return button;
    }

    private EditText NewTextField()
    {
        var field = new RaskEditText(_context) { InputType = Android.Text.InputTypes.ClassText };
        field.TextChanged += (s, e) =>
        {
            if (s is RaskEditText { ChangeId: >= 0, Suppress: false } f)
            {
                _raise(new NativeSurfaceEvent(
                    f.ChangeId, NativeSurfaceEventKind.Change, f.Text ?? string.Empty));
            }
        };
        return field;
    }

    private Switch NewSwitch()
    {
        var toggle = new RaskSwitch(_context);
        toggle.CheckedChange += (s, e) =>
        {
            if (s is RaskSwitch { ChangeId: >= 0, Suppress: false } t)
            {
                _raise(new NativeSurfaceEvent(
                    t.ChangeId, NativeSurfaceEventKind.Change, e.IsChecked ? "true" : "false"));
            }
        };
        return toggle;
    }

    private AndroidView NewDivider()
    {
        var rule = new AndroidView(_context);
        rule.SetBackgroundColor(Color.Argb(31, 0, 0, 0));
        rule.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Math.Max(1, ToPx(0.5)));
        return rule;
    }

    // A tap on a container is a click on the whole box, which is how a list row becomes selectable.
    private void SetTapId(AndroidView view, int id)
    {
        switch (view)
        {
            case RaskButton button:
                button.TapId = id;
                return;
            case RaskStack stack:
                stack.TapId = id;
                if (id >= 0 && !stack.TapWired)
                {
                    stack.TapWired = true;
                    stack.Clickable = true;
                    stack.Click += (s, _) =>
                    {
                        if (s is RaskStack { TapId: >= 0 } st)
                        {
                            _raise(new NativeSurfaceEvent(st.TapId, NativeSurfaceEventKind.Tap, null));
                        }
                    };
                }

                stack.Clickable = id >= 0;
                return;
        }
    }

    private static void ApplyForeground(AndroidView view, Color? color)
    {
        switch (view)
        {
            case TextView text when color is { } c:
                text.SetTextColor(c);
                break;
            case ProgressBar spinner when color is { } c:
                spinner.IndeterminateDrawable?.SetColorFilter(
                    new PorterDuffColorFilter(c, PorterDuff.Mode.SrcIn!));
                break;
        }
    }

    // The WHOLE appearance, re-derived from scratch on every write to any of its three props: the style
    // first, then the explicit colours over it, so an explicit Background or Color wins whether the
    // patch carried all three or only one. NativeButton documents exactly that precedence.
    private void ApplyButtonAppearance(RaskButton button)
    {
        var appearance = button.ButtonAppearance;
        switch (appearance.Style)
        {
            case NativeButtonStyle.Plain:
                button.SetBackgroundColor(Color.Transparent);
                button.SetTextColor(Color.Argb(255, 33, 150, 243));
                break;
            case NativeButtonStyle.Destructive:
                button.SetBackgroundColor(Color.Argb(255, 211, 47, 47));
                button.SetTextColor(Color.White);
                break;
            case NativeButtonStyle.Tinted:
                button.SetBackgroundColor(Color.Argb(40, 33, 150, 243));
                button.SetTextColor(Color.Argb(255, 33, 150, 243));
                break;
            default:
                button.SetBackgroundColor(Color.Argb(255, 33, 150, 243));
                button.SetTextColor(Color.White);
                break;
        }

        if (ResolveColor(appearance.Background) is { } background)
        {
            button.SetBackgroundColor(background);
        }

        if (ResolveColor(appearance.Foreground) is { } foreground)
        {
            button.SetTextColor(foreground);
        }
    }

    private static void SetDimension(AndroidView view, int? px, bool width)
    {
        var lp = view.LayoutParameters ?? new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        if (width)
        {
            lp.Width = px ?? ViewGroup.LayoutParams.WrapContent;
        }
        else
        {
            lp.Height = px ?? ViewGroup.LayoutParams.WrapContent;
        }

        view.LayoutParameters = lp;
    }

    private int ToPx(double dip) =>
        (int)Math.Round(dip * (_context.Resources?.DisplayMetrics?.Density ?? 1f));

    private Color? ResolveColor(string? token)
    {
        if (!NativeColor.TryResolve(token, out var light, out var dark))
        {
            return null;
        }

        var night = (_context.Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask)
                    == Android.Content.Res.UiMode.NightYes;
        var c = night ? dark : light;
        return Color.Argb(c.A, c.R, c.G, c.B);
    }

    private int ResolveDrawable(string? name) =>
        string.IsNullOrEmpty(name) || _context.Resources is null || _context.PackageName is null
            ? 0
            : _context.Resources.GetIdentifier(name, "drawable", _context.PackageName);
}

// LinearLayout has no per-child spacing and no cross-axis "stretch", so the stack owns both and re-applies
// them to its children whenever either the setting or the child list changes.
internal sealed class RaskStack : LinearLayout
{
    // LinearLayout's own default orientation is HORIZONTAL, but iOS creates its stacks vertical
    // (UiKitViewOps: NewStack(UILayoutConstraintAxis.Vertical)) — so leaving the platform default in place
    // made an unset NativeStack/NativeScreen Orientation mean two different things per platform. On Android
    // the row filled the width and every child past it was silently invisible, which is the shape of the
    // example in docs/native.md. Vertical is the shared default; an explicit Orientation still wins.
    public RaskStack(Context context) : base(context) => Orientation = AndroidOrientation.Vertical;

    public int SpacingPx { get; set; }

    public NativeAlignment CrossAlignment { get; set; } = NativeAlignment.Stretch;

    public int TapId { get; set; } = -1;

    public bool TapWired { get; set; }

    public void RefreshChildLayout()
    {
        var horizontal = Orientation == AndroidOrientation.Horizontal;
        SetGravity(CrossAlignment switch
        {
            NativeAlignment.Center => horizontal ? GravityFlags.CenterVertical : GravityFlags.CenterHorizontal,
            NativeAlignment.End => horizontal ? GravityFlags.Bottom : GravityFlags.End,
            NativeAlignment.Start => horizontal ? GravityFlags.Top : GravityFlags.Start,
            _ => GravityFlags.NoGravity,
        });

        for (var i = 0; i < ChildCount; i++)
        {
            var child = GetChildAt(i);
            if (child is null)
            {
                continue;
            }

            var lp = child.LayoutParameters as LinearLayout.LayoutParams
                     ?? new LinearLayout.LayoutParams(
                         ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);

            // Stretch fills the cross axis; every other alignment sizes to content and lets Gravity place it.
            var fill = CrossAlignment == NativeAlignment.Stretch;
            if (horizontal)
            {
                lp.Height = fill ? ViewGroup.LayoutParams.MatchParent : ViewGroup.LayoutParams.WrapContent;
                lp.RightMargin = i == ChildCount - 1 ? 0 : SpacingPx;
                lp.BottomMargin = 0;
            }
            else
            {
                lp.Width = fill ? ViewGroup.LayoutParams.MatchParent : ViewGroup.LayoutParams.WrapContent;
                lp.BottomMargin = i == ChildCount - 1 ? 0 : SpacingPx;
                lp.RightMargin = 0;
            }

            child.LayoutParameters = lp;
        }
    }
}

// A scroll view with a stack inside it — the container the children actually go into.
internal sealed class RaskScrollView : ScrollView
{
    public RaskScrollView(Context context) : base(context)
    {
        Content = new RaskStack(context) { Orientation = AndroidOrientation.Vertical };
        AddView(Content, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
    }

    public RaskStack Content { get; }
}

// Carriers for the handler id, so an event knows which delegate to run. Suppress guards the controlled-input
// write-back: setting Text/Checked from a frame must not echo back as if the user had done it.
internal sealed class RaskButton(Context context) : Button(context)
{
    public int TapId { get; set; } = -1;

    // Style, Background and Color decide one painted result together, so the button holds all three and
    // repaints from the set — see NativeButtonAppearance.
    public NativeButtonAppearance ButtonAppearance { get; } = new();
}

internal sealed class RaskEditText(Context context) : EditText(context)
{
    public int ChangeId { get; set; } = -1;

    public bool Suppress { get; set; }
}

internal sealed class RaskSwitch(Context context) : Switch(context)
{
    public int ChangeId { get; set; } = -1;

    public bool Suppress { get; set; }
}
