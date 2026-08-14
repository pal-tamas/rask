using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     <see cref="ElementCount" /> div elements, each carrying <c>Class</c>, <c>Id</c>,
///     <c>Style</c>, and <c>(attrCount - 3)</c> data-* attributes. Stresses
///     <c>Component.WriteAttributes</c> and the safe-ASCII fast path in
///     <c>HtmlSerializer.AppendEncoded</c> — the per-attribute encode + StringBuilder
///     append is the dominant cost on attribute-heavy markup, e.g. design-system
///     wrapper libraries that emit data-test-id, data-state, data-component, etc. on
///     every element.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class AttributeHeavyElements
{
    public const int ElementCount = 100;

    public static Component BuildRask(int attrCount)
    {
        var dataAttrCount = Math.Max(0, attrCount - 3);
        var elements = new List<Component>(ElementCount);
        for (var i = 0; i < ElementCount; i++)
        {
            var data = new Dictionary<string, string?>(dataAttrCount);
            for (var d = 0; d < dataAttrCount; d++)
            {
                data[$"k{d}"] = $"v{i}-{d}";
            }

            elements.Add(Div
                .Class("row item")
                .Id($"r{i}")
                .Style("display:block")
                .Data(data));
        }

        return Div.Class("container")[elements];
    }

    /// <summary>
    ///     Same shape as <see cref="BuildRask" /> but element index 50 has one of its
    ///     data-* values flipped — gives the diff codec one <c>SetAttribute</c> op to
    ///     emit.
    /// </summary>
    public static Component BuildRaskMutateOne(int attrCount, int mutationSalt)
    {
        var dataAttrCount = Math.Max(0, attrCount - 3);
        const int mutateIndex = ElementCount / 2;
        var elements = new List<Component>(ElementCount);
        for (var i = 0; i < ElementCount; i++)
        {
            var data = new Dictionary<string, string?>(dataAttrCount);
            for (var d = 0; d < dataAttrCount; d++)
            {
                var v = i == mutateIndex && d == 0 ? $"v{i}-{d}-{mutationSalt}" : $"v{i}-{d}";
                data[$"k{d}"] = v;
            }

            elements.Add(Div
                .Class("row item")
                .Id($"r{i}")
                .Style("display:block")
                .Data(data));
        }

        return Div.Class("container")[elements];
    }

    public sealed class BlazorAttributeHeavy : ComponentBase
    {
        [Parameter] public int AttrCount { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            var dataAttrCount = Math.Max(0, AttrCount - 3);

            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "container");
            for (var i = 0; i < ElementCount; i++)
            {
                b.OpenElement(2, "div");
                b.AddAttribute(3, "class", "row item");
                b.AddAttribute(4, "id", $"r{i}");
                b.AddAttribute(5, "style", "display:block");
#pragma warning disable ASP0006 // computed sequence numbers are intentional for variable-length attr loops
                for (var d = 0; d < dataAttrCount; d++)
                {
                    b.AddAttribute(6 + d, $"data-k{d}", $"v{i}-{d}");
                }
#pragma warning restore ASP0006

                b.CloseElement();
            }

            b.CloseElement();
        }
    }

    public sealed class BlazorAttributeHeavyMutateOne : ComponentBase
    {
        [Parameter] public int AttrCount { get; set; }
        [Parameter] public int MutationSalt { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            var dataAttrCount = Math.Max(0, AttrCount - 3);
            const int mutateIndex = ElementCount / 2;

            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "container");
            for (var i = 0; i < ElementCount; i++)
            {
                b.OpenElement(2, "div");
                b.AddAttribute(3, "class", "row item");
                b.AddAttribute(4, "id", $"r{i}");
                b.AddAttribute(5, "style", "display:block");
#pragma warning disable ASP0006
                for (var d = 0; d < dataAttrCount; d++)
                {
                    var v = i == mutateIndex && d == 0 ? $"v{i}-{d}-{MutationSalt}" : $"v{i}-{d}";
                    b.AddAttribute(6 + d, $"data-k{d}", v);
                }
#pragma warning restore ASP0006

                b.CloseElement();
            }

            b.CloseElement();
        }
    }
}
