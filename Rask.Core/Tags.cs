using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Routing;
using F = Rask.Core.Components;

namespace Rask.Core;

public static partial class Tags
{
    public static Route Route<T>(string template, IReadOnlyList<Route>? SubRoutes = null) where T : Component
        => new(typeof(T), template, SubRoutes);

    public static Route Route<T>(IReadOnlyList<Route>? SubRoutes = null) where T : Component
        => new(typeof(T), RouteTemplateResolver.GetLocalTemplate(typeof(T)), SubRoutes);

    public static Router Router(IReadOnlyList<Route> Routes)
    {
        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException("Router() must be called inside a Rask render tree.");
        var router = ctx.GetOrCreate<Router>(sp => ActivatorUtilities.CreateInstance<Router>(sp));
        router.SetRoutes(Routes);
        return router;
    }

    public static Router Router() => Router(RouteRegistry.BuildTree());

    public static Outlet Outlet()
    {
        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException("Outlet() must be called inside a Router render tree.");
        return ctx.GetOrCreate<Outlet>(_ => new Outlet());
    }

    public static Text Text(string value) => new(value);

    public static Raw Raw(string html) => new(html);

    public static F.NavLink NavLink(
        RouteUrl Href,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        string ActiveClass = "active",
        F.NavLinkMatch ActiveMatch = F.NavLinkMatch.Exact,
        IEnumerable<Child>? Children = null)
        => new(new F.NavLink.Props(Href, Id, Class, Style, Data, ActiveClass, ActiveMatch), Children);


    public static F.A A(
        string? Href = null,
        string? Target = null,
        string? Rel = null,
        string? Download = null,
        string? Hreflang = null,
        string? Type = null,
        string? ReferrerPolicy = null,
        string? Ping = null,
        Action? OnClick = null,
        Func<Task>? OnClickAsync = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(
            new F.A.Props(Href, Target, Rel, Download, Hreflang, Type, ReferrerPolicy, Ping, OnClick, OnClickAsync, Id,
                Class, Style, Data), Children);

    public static F.Abbr Abbr(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Abbr.Props(Id, Class, Style, Data), Children);

    public static F.Address Address(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Address.Props(Id, Class, Style, Data), Children);

    public static F.Article Article(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Article.Props(Id, Class, Style, Data), Children);

    public static F.Aside Aside(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Aside.Props(Id, Class, Style, Data), Children);

    public static F.Audio Audio(
        string? Src = null,
        bool Controls = false,
        bool Autoplay = false,
        bool Loop = false,
        bool Muted = false,
        string? Preload = null,
        string? CrossOrigin = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Audio.Props(Src, Controls, Autoplay, Loop, Muted, Preload, CrossOrigin, Id, Class, Style, Data),
            Children);

    public static F.B B(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.B.Props(Id, Class, Style, Data), Children);

    public static F.Bdi Bdi(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Bdi.Props(Id, Class, Style, Data), Children);

    public static F.Bdo Bdo(
        string? Dir = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Bdo.Props(Dir, Id, Class, Style, Data), Children);

    public static F.Blockquote Blockquote(
        string? Cite = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Blockquote.Props(Cite, Id, Class, Style, Data), Children);

    public static F.Body Body(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Body.Props(Id, Class, Style, Data), Children);

    public static F.Button Button(
        string? Type = null,
        bool Disabled = false,
        string? Name = null,
        string? Value = null,
        Action? OnClick = null,
        Func<Task>? OnClickAsync = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Button.Props(Type, Disabled, Name, Value, OnClick, OnClickAsync, Id, Class, Style, Data),
            Children);

    public static F.Canvas Canvas(
        int? Width = null,
        int? Height = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Canvas.Props(Width, Height, Id, Class, Style, Data), Children);

    public static F.Caption Caption(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Caption.Props(Id, Class, Style, Data), Children);

    public static F.Cite Cite(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Cite.Props(Id, Class, Style, Data), Children);

    public static F.Code Code(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Code.Props(Id, Class, Style, Data), Children);

    public static F.Colgroup Colgroup(
        int? Span = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Colgroup.Props(Span, Id, Class, Style, Data), Children);

    public static F.Data Data(
        string? Value = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Data.Props(Value, Id, Class, Style, Data), Children);

    public static F.Datalist Datalist(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Datalist.Props(Id, Class, Style, Data), Children);

    public static F.Dd Dd(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Dd.Props(Id, Class, Style, Data), Children);

    public static F.Del Del(
        string? Cite = null,
        string? DateTime = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Del.Props(Cite, DateTime, Id, Class, Style, Data), Children);

    public static F.Details Details(
        bool Open = false,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Details.Props(Open, Id, Class, Style, Data), Children);

    public static F.Dfn Dfn(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Dfn.Props(Id, Class, Style, Data), Children);

    public static F.Dialog Dialog(
        bool Open = false,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Dialog.Props(Open, Id, Class, Style, Data), Children);

    public static F.Div Div(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Div.Props(Id, Class, Style, Data), Children);

    public static F.Doctype Doctype() => new();

    public static F.Dl Dl(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Dl.Props(Id, Class, Style, Data), Children);

    public static F.Dt Dt(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Dt.Props(Id, Class, Style, Data), Children);

    public static F.Em Em(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Em.Props(Id, Class, Style, Data), Children);

    public static F.Fieldset Fieldset(
        bool Disabled = false,
        string? Form = null,
        string? Name = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Fieldset.Props(Disabled, Form, Name, Id, Class, Style, Data), Children);

    public static F.Figcaption Figcaption(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Figcaption.Props(Id, Class, Style, Data), Children);

    public static F.Figure Figure(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Figure.Props(Id, Class, Style, Data), Children);

    public static F.Footer Footer(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Footer.Props(Id, Class, Style, Data), Children);

    public static F.Form Form(
        string? Enctype = null,
        string? Target = null,
        string? AcceptCharset = null,
        string? Autocomplete = null,
        bool Novalidate = false,
        string? Name = null,
        Action<FormData>? OnSubmit = null,
        Func<FormData, Task>? OnSubmitAsync = null,
        object? Model = null,
        Delegate? OnValidSubmit = null,
        Delegate? OnInvalidSubmit = null,
        EditContext? Context = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(
            new F.Form.Props(Enctype, Target, AcceptCharset, Autocomplete, Novalidate, Name, OnSubmit,
                OnSubmitAsync, Model, OnValidSubmit, OnInvalidSubmit, Context, Id, Class, Style, Data), Children);

    public static F.Form Form<TModel>(
        TModel Model,
        Action<TModel>? OnValidSubmit = null,
        Action<TModel>? OnInvalidSubmit = null,
        Func<TModel, Task>? OnValidSubmitAsync = null,
        Func<TModel, Task>? OnInvalidSubmitAsync = null,
        EditContext? Context = null,
        string? Enctype = null,
        string? Target = null,
        string? AcceptCharset = null,
        string? Autocomplete = null,
        bool Novalidate = false,
        string? Name = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null) where TModel : class
    {
        var valid = (Delegate?)OnValidSubmit ?? OnValidSubmitAsync;
        var invalid = (Delegate?)OnInvalidSubmit ?? OnInvalidSubmitAsync;
        return Form(
            Enctype, Target,
            AcceptCharset, Autocomplete, Novalidate,
            Name, Model: Model, OnValidSubmit: valid, OnInvalidSubmit: invalid,
            Context: Context, Id: Id, Class: Class, Style: Style, Data: Data, Children: Children);
    }

    public static F.Fragment Fragment(IEnumerable<Child>? Children = null) => new(Children);
    public static F.Fragment Fragment(params Child[] children) => new(children);

    public static F.H1 H1(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.H1.Props(Id, Class, Style, Data), Children);

    public static F.H2 H2(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.H2.Props(Id, Class, Style, Data), Children);

    public static F.H3 H3(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.H3.Props(Id, Class, Style, Data), Children);

    public static F.H4 H4(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.H4.Props(Id, Class, Style, Data), Children);

    public static F.H5 H5(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.H5.Props(Id, Class, Style, Data), Children);

    public static F.H6 H6(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.H6.Props(Id, Class, Style, Data), Children);

    public static F.Head Head(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Head.Props(Id, Class, Style, Data), Children);

    public static F.Header Header(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Header.Props(Id, Class, Style, Data), Children);

    public static F.Hgroup Hgroup(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Hgroup.Props(Id, Class, Style, Data), Children);

    public static F.Html Html(
        string? Lang = null,
        string? Dir = null,
        string? Xmlns = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Html.Props(Lang, Dir, Xmlns, Id, Class, Style, Data), Children);

    public static F.HtmlObject HtmlObject(
        string? DataUrl = null,
        string? Type = null,
        string? Name = null,
        int? Width = null,
        int? Height = null,
        string? Form = null,
        string? UseMap = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.HtmlObject.Props(DataUrl, Type, Name, Width, Height, Form, UseMap, Id, Class, Style, Data),
            Children);

    public static F.I I(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.I.Props(Id, Class, Style, Data), Children);

    public static F.Iframe Iframe(
        string? Src = null,
        string? Srcdoc = null,
        string? Name = null,
        string? Sandbox = null,
        string? Allow = null,
        int? Width = null,
        int? Height = null,
        string? Loading = null,
        string? ReferrerPolicy = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(
            new F.Iframe.Props(Src, Srcdoc, Name, Sandbox, Allow, Width, Height, Loading, ReferrerPolicy, Id, Class,
                Style, Data), Children);

    public static F.Ins Ins(
        string? Cite = null,
        string? DateTime = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Ins.Props(Cite, DateTime, Id, Class, Style, Data), Children);

    public static F.Kbd Kbd(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Kbd.Props(Id, Class, Style, Data), Children);

    public static F.Label Label(
        string? For = null,
        string? Form = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Label.Props(For, Form, Id, Class, Style, Data), Children);

    public static F.Legend Legend(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Legend.Props(Id, Class, Style, Data), Children);

    public static F.Li Li(
        int? Value = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Li.Props(Value, Id, Class, Style, Data), Children);

    public static F.Main Main(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Main.Props(Id, Class, Style, Data), Children);

    public static F.Map Map(
        string? Name = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Map.Props(Name, Id, Class, Style, Data), Children);

    public static F.Mark Mark(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Mark.Props(Id, Class, Style, Data), Children);

    public static F.Menu Menu(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Menu.Props(Id, Class, Style, Data), Children);

    public static F.Meter Meter(
        double? Value = null,
        double? Min = null,
        double? Max = null,
        double? Low = null,
        double? High = null,
        double? Optimum = null,
        string? Form = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Meter.Props(Value, Min, Max, Low, High, Optimum, Form, Id, Class, Style, Data), Children);

    public static F.Nav Nav(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Nav.Props(Id, Class, Style, Data), Children);

    public static F.Noscript Noscript(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Noscript.Props(Id, Class, Style, Data), Children);

    public static F.Ol Ol(
        string? Type = null,
        bool Reversed = false,
        int? Start = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Ol.Props(Type, Reversed, Start, Id, Class, Style, Data), Children);

    public static F.Optgroup Optgroup(
        bool Disabled = false,
        string? Label = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Optgroup.Props(Disabled, Label, Id, Class, Style, Data), Children);

    public static F.Option Option(
        string? Value = null,
        bool Selected = false,
        bool Disabled = false,
        string? Label = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Option.Props(Value, Selected, Disabled, Label, Id, Class, Style, Data), Children);

    public static F.Output Output(
        string? For = null,
        string? Form = null,
        string? Name = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Output.Props(For, Form, Name, Id, Class, Style, Data), Children);

    public static F.P P(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.P.Props(Id, Class, Style, Data), Children);

    public static F.Picture Picture(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Picture.Props(Id, Class, Style, Data), Children);

    public static F.Pre Pre(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Pre.Props(Id, Class, Style, Data), Children);

    public static F.Progress Progress(
        double? Value = null,
        double? Max = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Progress.Props(Value, Max, Id, Class, Style, Data), Children);

    public static F.Q Q(
        string? Cite = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Q.Props(Cite, Id, Class, Style, Data), Children);

    public static F.Rp Rp(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Rp.Props(Id, Class, Style, Data), Children);

    public static F.Rt Rt(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Rt.Props(Id, Class, Style, Data), Children);

    public static F.Ruby Ruby(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Ruby.Props(Id, Class, Style, Data), Children);

    public static F.S S(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.S.Props(Id, Class, Style, Data), Children);

    public static F.Samp Samp(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Samp.Props(Id, Class, Style, Data), Children);

    public static F.Script Script(
        string? Src = null,
        string? Type = null,
        bool Async = false,
        bool Defer = false,
        string? CrossOrigin = null,
        string? Integrity = null,
        bool NoModule = false,
        string? ReferrerPolicy = null,
        string? Charset = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(
            new F.Script.Props(Src, Type, Async, Defer, CrossOrigin, Integrity, NoModule, ReferrerPolicy, Charset, Id,
                Class, Style, Data), Children);

    public static F.Search Search(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Search.Props(Id, Class, Style, Data), Children);

    public static F.Section Section(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Section.Props(Id, Class, Style, Data), Children);

    public static F.Select Select(
        string? Name = null,
        bool Multiple = false,
        bool Required = false,
        bool Disabled = false,
        int? Size = null,
        string? Form = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        Action<string>? OnChange = null,
        Func<string, Task>? OnChangeAsync = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(
            new F.Select.Props(Name, Multiple, Required, Disabled, Size, Form, Autofocus, Autocomplete, OnChange,
                OnChangeAsync, Id, Class, Style, Data), Children);

    public static F.Slot Slot(
        string? Name = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Slot.Props(Name, Id, Class, Style, Data), Children);

    public static F.Small Small(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Small.Props(Id, Class, Style, Data), Children);

    public static F.Span Span(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Span.Props(Id, Class, Style, Data), Children);

    public static F.Strong Strong(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Strong.Props(Id, Class, Style, Data), Children);

    public static F.Style Style(
        string? Type = null,
        string? Media = null,
        string? Title = null,
        string? Nonce = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Style.Props(Type, Media, Title, Nonce, Id, Class, Style, Data), Children);

    public static F.Sub Sub(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Sub.Props(Id, Class, Style, Data), Children);

    public static F.Summary Summary(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Summary.Props(Id, Class, Style, Data), Children);

    public static F.Sup Sup(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Sup.Props(Id, Class, Style, Data), Children);

    public static F.Table Table(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Table.Props(Id, Class, Style, Data), Children);

    public static F.Tbody Tbody(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Tbody.Props(Id, Class, Style, Data), Children);

    public static F.Td Td(
        int? Colspan = null,
        int? Rowspan = null,
        string? Headers = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Td.Props(Colspan, Rowspan, Headers, Id, Class, Style, Data), Children);

    public static F.Template Template(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Template.Props(Id, Class, Style, Data), Children);

    public static F.Textarea Textarea(
        string? Name = null,
        int? Rows = null,
        int? Cols = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        int? MaxLength = null,
        int? MinLength = null,
        string? Wrap = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Form = null,
        string? Dirname = null,
        Action<string>? OnInput = null,
        Action<string>? OnChange = null,
        Func<string, Task>? OnInputAsync = null,
        Func<string, Task>? OnChangeAsync = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(
            new F.Textarea.Props(Name, Rows, Cols, Placeholder, Required, Disabled, ReadOnly, MaxLength, MinLength,
                Wrap, Autofocus, Autocomplete, Form, Dirname, OnInput, OnChange, OnInputAsync, OnChangeAsync, Id, Class,
                Style, Data), Children);

    public static F.Tfoot Tfoot(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Tfoot.Props(Id, Class, Style, Data), Children);

    public static F.Th Th(
        int? Colspan = null,
        int? Rowspan = null,
        string? Headers = null,
        string? Scope = null,
        string? Abbr = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Th.Props(Colspan, Rowspan, Headers, Scope, Abbr, Id, Class, Style, Data), Children);

    public static F.Thead Thead(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Thead.Props(Id, Class, Style, Data), Children);

    public static F.Time Time(
        string? DateTime = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Time.Props(DateTime, Id, Class, Style, Data), Children);

    public static F.Title Title(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Title.Props(Id, Class, Style, Data), Children);

    public static F.Tr Tr(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Tr.Props(Id, Class, Style, Data), Children);

    public static F.U U(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.U.Props(Id, Class, Style, Data), Children);

    public static F.Ul Ul(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(new F.Ul.Props(Id, Class, Style, Data), Children);

    public static F.Video Video(
        string? Src = null,
        string? Poster = null,
        int? Width = null,
        int? Height = null,
        bool Controls = false,
        bool Autoplay = false,
        bool Loop = false,
        bool Muted = false,
        string? Preload = null,
        string? CrossOrigin = null,
        bool PlaysInline = false,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
        => new(
            new F.Video.Props(Src, Poster, Width, Height, Controls, Autoplay, Loop, Muted, Preload, CrossOrigin,
                PlaysInline, Id, Class, Style, Data), Children);

    public static F.Area Area(
        string? Alt = null,
        string? Coords = null,
        string? Shape = null,
        string? Href = null,
        string? Target = null,
        string? Rel = null,
        string? Download = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Area.Props(Alt, Coords, Shape, Href, Target, Rel, Download, Id, Class, Style, Data));

    public static F.Base Base(
        string? Href = null,
        string? Target = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Base.Props(Href, Target, Id, Class, Style, Data));

    public static F.Br Br(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Br.Props(Id, Class, Style, Data));

    public static F.Col Col(
        int? Span = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Col.Props(Span, Id, Class, Style, Data));

    public static F.Embed Embed(
        string? Src = null,
        string? Type = null,
        int? Width = null,
        int? Height = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Embed.Props(Src, Type, Width, Height, Id, Class, Style, Data));

    public static F.Hr Hr(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Hr.Props(Id, Class, Style, Data));

    public static F.Img Img(
        string? Src = null,
        string? Alt = null,
        int? Width = null,
        int? Height = null,
        string? Loading = null,
        string? Srcset = null,
        string? Sizes = null,
        string? CrossOrigin = null,
        string? ReferrerPolicy = null,
        string? Decoding = null,
        string? UseMap = null,
        bool Ismap = false,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Img.Props(Src, Alt, Width, Height, Loading, Srcset, Sizes, CrossOrigin, ReferrerPolicy, Decoding,
            UseMap, Ismap, Id, Class, Style, Data));

    public static F.Input Input(
        string? Type = null,
        string? Name = null,
        string? Value = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        bool Checked = false,
        string? Min = null,
        string? Max = null,
        string? Step = null,
        string? Pattern = null,
        int? Size = null,
        int? MaxLength = null,
        int? MinLength = null,
        bool Multiple = false,
        string? Accept = null,
        string? Alt = null,
        string? Autocomplete = null,
        bool Autofocus = false,
        string? Form = null,
        string? FormAction = null,
        string? FormEnctype = null,
        string? FormMethod = null,
        bool FormNovalidate = false,
        string? FormTarget = null,
        string? List = null,
        string? Src = null,
        int? Width = null,
        int? Height = null,
        Action<string>? OnInput = null,
        Action<string>? OnChange = null,
        Func<string, Task>? OnInputAsync = null,
        Func<string, Task>? OnChangeAsync = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Input.Props(Type, Name, Value, Placeholder, Required, Disabled, ReadOnly, Checked, Min, Max, Step,
            Pattern, Size, MaxLength, MinLength, Multiple, Accept, Alt, Autocomplete, Autofocus, Form, FormAction,
            FormEnctype, FormMethod, FormNovalidate, FormTarget, List, Src, Width, Height, OnInput, OnChange,
            OnInputAsync, OnChangeAsync, Id, Class, Style, Data));

    public static F.Link Link(
        string? Href = null,
        string? Rel = null,
        string? Type = null,
        string? Media = null,
        string? Sizes = null,
        string? Hreflang = null,
        string? As = null,
        string? CrossOrigin = null,
        string? ReferrerPolicy = null,
        bool Disabled = false,
        string? Color = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Link.Props(Href, Rel, Type, Media, Sizes, Hreflang, As, CrossOrigin, ReferrerPolicy, Disabled,
            Color, Id, Class, Style, Data));

    public static F.Meta Meta(
        string? Charset = null,
        string? Name = null,
        string? Content = null,
        string? HttpEquiv = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Meta.Props(Charset, Name, Content, HttpEquiv, Id, Class, Style, Data));

    public static F.Source Source(
        string? Src = null,
        string? Type = null,
        string? Srcset = null,
        string? Sizes = null,
        string? Media = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Source.Props(Src, Type, Srcset, Sizes, Media, Id, Class, Style, Data));

    public static F.Track Track(
        string? Kind = null,
        string? Src = null,
        string? Srclang = null,
        string? Label = null,
        bool Default = false,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Track.Props(Kind, Src, Srclang, Label, Default, Id, Class, Style, Data));

    public static F.Wbr Wbr(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => new(new F.Wbr.Props(Id, Class, Style, Data));

    public static F.RaskScopedStyles RaskScopedStyles() => new();

    public static F.RaskRuntimeScript RaskRuntimeScript() => new();
}
