namespace Rask.Generators.Tests;

// A small enum property offers a step per member, so a value reads as a word: `Widget.Primary` beside
// `Widget.Tone(Tone.Primary)`. These pin the three things that keep that from going wrong — a large
// enum's members are names rather than styles, two properties of one enum have no answer to which a
// step would set, and a member whose name the component already uses would be a duplicate member.
public class BuilderEnumStepTests
{
    private const string Src = """
                               using Rask.Core;
                               namespace Demo;

                               public enum Tone { Neutral, Primary, Danger }

                               public enum Weight { Light, Bold }

                               // Nine members: one past the line, so its values are treated as names.
                               public enum IconName { Gear, Trash, Search, Check, Close, Plus, Minus, Pencil, Star }

                               [System.Flags]
                               public enum Edges { None = 0, Top = 1, Bottom = 2 }

                               public partial class Widget : Component
                               {
                                   public Tone? Tone { get; set; }
                                   public Weight? Weight { get; set; }
                                   public IconName? Icon { get; set; }
                                   public Edges? Edges { get; set; }
                                   public bool? Bold { get; set; }
                                   public System.DayOfWeek? FirstDay { get; set; }
                               }

                               public partial class TwoIcons : Component
                               {
                                   public Tone? Tone { get; set; }
                                   public Tone? Accent { get; set; }
                               }
                               """;

    private static string Setters() => BuilderGeneratorHarness.Run(Src).Source("RaskBuilderSetters.g.cs");

    [Fact]
    public void A_small_enum_property_offers_a_step_per_member()
    {
        var output = Setters();

        Assert.Contains("Neutral => __b.Tone(global::Demo.Tone.Neutral);", output, StringComparison.Ordinal);
        Assert.Contains("Primary => __b.Tone(global::Demo.Tone.Primary);", output, StringComparison.Ordinal);
        Assert.Contains("Danger => __b.Tone(global::Demo.Tone.Danger);", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_enum_setter_stays_because_a_step_can_only_name_a_value_the_source_knows()
    {
        // `Tone(order.IsUrgent ? Tone.Danger : Tone.Neutral)` is decided at run time and no step can say it.
        Assert.Contains(
            "Tone(this global::Demo.Widget __b, global::Demo.Tone? value)", Setters(), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_properties_of_one_enum_get_no_steps_at_all()
    {
        // An Icon and a TrailingIcon of one type have no answer to which `.Search` would set, so the
        // question is refused rather than answered arbitrarily — for BOTH of them.
        var output = Setters();

        Assert.DoesNotContain("Neutral => __b.Accent(", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__b.Accent(global::Demo.Tone.Neutral)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_large_enums_members_are_names_rather_than_styles_so_they_get_no_steps()
    {
        // `Widget.Gear` would say the widget IS a gear rather than that it shows one.
        Assert.DoesNotContain("Gear => __b.Icon(", Setters(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_flags_enum_gets_no_steps_because_its_values_combine_rather_than_choose()
    {
        // `.Top.Bottom` would read as two steps that each REPLACE the other, since a step assigns.
        Assert.DoesNotContain("Top => __b.Edges(", Setters(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_BCL_enum_gets_no_steps_because_its_members_are_values_not_descriptions()
    {
        // `Widget.Sunday` says the widget is Sunday, not that its week starts there.
        Assert.DoesNotContain("Sunday => __b.FirstDay(", Setters(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_whose_name_the_component_already_uses_is_skipped()
    {
        // Widget has a `Bold` property, so Weight.Bold cannot also be a step — that is CS0102. The rest
        // of the enum is unaffected.
        var output = Setters();

        Assert.DoesNotContain("Bold => __b.Weight(", output, StringComparison.Ordinal);
        Assert.Contains("Light => __b.Weight(global::Demo.Weight.Light);", output, StringComparison.Ordinal);
    }
}
