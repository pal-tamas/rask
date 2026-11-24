using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators;

// What a model's property's DataAnnotations attributes tell its field (FieldShapeHints), on top of its value object's or
// its enum's shape — the shape of the type it holds (`held`), which a generic wrapper's type argument may be.
public sealed partial class ModelInputGenerator
{
    private static string? AnnotatedShapeOf(IPropertySymbol property, ITypeSymbol held, ValueObjectShape? valueObject) =>
        FieldShapeHints.Annotated(property, ShapeOf(held, valueObject));

    private static void AppendWithin(StringBuilder s, List<ShapedModel> shaped)
    {
        if (FieldShapeHints.UseWithin(shaped.SelectMany(static m => m.Fields.Select(static f => f.Shape))))
        {
            FieldShapeHints.AppendWithin(s);
        }
    }
}
