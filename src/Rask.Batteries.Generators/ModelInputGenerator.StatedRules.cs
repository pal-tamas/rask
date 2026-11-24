using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators;

// A generated model borrows the rules its aggregate states in code (FluentValidation's RuleFor, in the aggregate's own
// static Rules or in an AbstractValidator class): each primitive member carries the rules of the aggregate's member of
// the same name, and each list of rows the rules its row's model borrows. Registered with Rask.Validation.FluentValidation,
// which every host references; at run time a model whose aggregate states nothing gets no validator at all.
public sealed partial class ModelInputGenerator
{
    private const string Validators = "global::Rask.RaskValidators";

    // How a member borrows: as the same type, or as the nullable twin a model holds a value type in.
    private static string StatedStep(ITypeSymbol type) =>
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ? "Value" : "Member";

    private static void AppendStatedRules(StringBuilder s, Entity entity)
    {
        var members = entity.Members.Where(static m => m.Stated is not null).ToList();
        if (!entity.BorrowsStatedRules || (members.Count == 0 && entity.Children.Count == 0))
        {
            return;
        }

        var model = FormModelType(entity);
        var name = "__" + entity.Name + ModelSuffix + "StatedRules";

        s.AppendLine();
        s.Append("/// <summary>The rules ").Append(entity.Name).Append(ModelSuffix)
            .AppendLine(" borrows from what its aggregate states in code, member by member.</summary>");
        s.Append("file static class ").AppendLine(name);
        s.AppendLine("{");
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        s.AppendLine("    internal static void Register() =>");
        s.Append("        ").Append(Validators).Append(".Replace(typeof(").Append(name).AppendLine("), new global::Rask.ValidatorEntry[]");
        s.AppendLine("        {");
        s.Append("            ").Append(Validators).Append(".Project<").Append(model).Append(", ").Append(entity.FullyQualifiedName)
            .AppendLine(">(static __p => __p");
        foreach (var member in members)
        {
            s.Append("                .").Append(member.Stated).Append("(static __m => __m.").Append(member.Name)
                .Append(", \"").Append(member.Name).AppendLine("\")");
        }

        foreach (var child in entity.Children)
        {
            s.Append("                .Rows(static __m => __m.").Append(child.Name).AppendLine(")");
        }

        s.AppendLine("            ),");
        s.AppendLine("        });");
        s.AppendLine("}");
    }
}
