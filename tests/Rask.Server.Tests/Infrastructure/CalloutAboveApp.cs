using Rask.Core;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>How many times a list has loaded — once per time it mounted.</summary>
public sealed class ListLoads
{
    public int Count { get; private set; }

    public int Load() => ++Count;
}

/// <summary>A list that loads as it mounts, and shows which load it is holding.</summary>
public sealed partial class LoadedList(ListLoads loads) : Component
{
    private int _load;

    protected override Task OnMount()
    {
        _load = loads.Load();
        return Task.CompletedTask;
    }

    protected override Component? Render() => Ul[Li[$"load {_load}"]];
}

/// <summary>A count kept in a field of the component — gone the moment the component is built again.</summary>
public sealed partial class PressCount : Component
{
    private int _presses;

    protected override Component? Render() => Button.Id("press").OnClick(() => _presses++)[$"pressed {_presses}"];
}

/// <summary>A callout its own button brings in and takes away, written AHEAD of the two components that hold state.</summary>
public sealed partial class CalloutPanel : Component
{
    private bool _saved;

    protected override Component? Render() =>
        Div[
            _saved ? P["Saved"] : null,
            Button.Id("save").OnClick(() => _saved = !_saved)["save"],
            LoadedList,
            PressCount
        ];
}

/// <summary>The panel is NOT the root: the root is forced dirty on every render, which is not what an app's page is.</summary>
public sealed partial class CalloutAboveApp : Component
{
    protected override Component? HeadAssets => Title["callout-above"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => Section[CalloutPanel];
}
