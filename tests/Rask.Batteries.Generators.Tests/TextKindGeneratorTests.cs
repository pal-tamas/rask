namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A value object declared from one of the kinds of text — Email, Phone, Url, Password, Multiline — is a Text to the
/// generator: Create, TryCreate, CreateOptional and __Problems over its one text, its column's length (the base's
/// default when it declares none), and a field shape that tells the input what the matching attribute would.
/// </summary>
public class TextKindGeneratorTests
{
    private const string Kind = "global::Rask.Core.Forms.FieldKind";

    private const string Source = """
        using System;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;

        public sealed partial record ContactEmail(string Value) : Email(Value, MaxLength: 200);
        public sealed partial record AnyEmail(string Value) : Email(Value);
        public sealed partial record ContactPhone(string Value) : Phone(Value);
        public sealed partial record Homepage(string Value) : Url(Value, Required: false);
        public sealed partial record SignInPassword(string Value) : Password(Value, MinLength: 8);
        public sealed partial record ChosenPassword(string Value) : Password(Value, MinLength: 12, New: true);
        public sealed partial record Remark(string Value) : Multiline(Value, MaxLength: 4000);
        public sealed partial record Code(string Value) : Email(Value, Upper: true);

        public sealed class Customer : Aggregate<Guid, Model>
        {
            private Customer() { }
            public ContactEmail Email { get; private set; } = ContactEmail.Create("a@b.hu");
            public ContactPhone? Phone { get; private set; }
            public Homepage? Site { get; private set; }
            public Remark? Remark { get; private set; }
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Source,
            [new ValueObjectFactoryGenerator(), new ModelInputGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore"]);

    [Theory]
    [InlineData("ContactEmail", Kind + ".Text, MaxLength: 200) { Input = global::Rask.Core.InputType.Email, AutoComplete = \"email\" });")]
    [InlineData("AnyEmail", Kind + ".Text, MaxLength: 254) { Input = global::Rask.Core.InputType.Email, AutoComplete = \"email\" });")]
    [InlineData("ContactPhone", Kind + ".Text, MaxLength: 32) { Input = global::Rask.Core.InputType.Tel, AutoComplete = \"tel\", InputMode = global::Rask.Core.InputMode.Tel });")]
    [InlineData("Homepage", Kind + ".Text, MaxLength: 2048) { Input = global::Rask.Core.InputType.Url });")]
    [InlineData("SignInPassword", Kind + ".Text, MinLength: 8) { Input = global::Rask.Core.InputType.Password, AutoComplete = \"current-password\" });")]
    [InlineData("ChosenPassword", Kind + ".Text, MinLength: 12) { Input = global::Rask.Core.InputType.Password, AutoComplete = \"new-password\" });")]
    [InlineData("Remark", Kind + ".Text, MaxLength: 4000) { Hint = global::Rask.Core.Forms.FieldHints.MultilineText });")]
    [InlineData("Code", Kind + ".Text, MaxLength: 254) { Input = global::Rask.Core.InputType.Email, AutoComplete = \"email\", Upper = true });")]
    public void Each_kind_of_text_tells_its_field_what_the_matching_attribute_would(string name, string shape)
    {
        var run = Run();

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("\"Value\", new global::Rask.Core.Forms.FieldShape(" + shape, generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ContactEmail", "\"Value\", new(MaxLength: 200));")]
    [InlineData("AnyEmail", "\"Value\", new(MaxLength: 254));")]
    [InlineData("ContactPhone", "\"Value\", new(MaxLength: 32));")]
    [InlineData("Homepage", "\"Value\", new(MaxLength: 2048));")]
    [InlineData("Remark", "\"Value\", new(MaxLength: 4000));")]
    public void A_kind_of_text_gives_its_column_the_length_it_declares_or_its_bases_default(string name, string facets)
    {
        var run = Run();

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Contains(facets, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_kind_of_text_is_made_and_asked_as_a_text()
    {
        var run = Run();

        var generated = run.GeneratedSource("ContactPhone.Value.g.cs");

        Assert.Contains("public static ContactPhone Create(string Value, string? field = null)", generated, StringComparison.Ordinal);
        Assert.Contains("public static bool TryCreate(string Value,", generated, StringComparison.Ordinal);
        Assert.Contains("public static ContactPhone? CreateOptional(string? Value, string? field = null)", generated, StringComparison.Ordinal);
        Assert.Contains("__Problems(string? value)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_of_an_aggregate_holding_kinds_of_text_registers_their_shapes()
    {
        var run = Run();

        var model = run.GeneratedSource("CustomerModel.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("\"Email\" => global::Shop.ContactEmail.__Field,", model, StringComparison.Ordinal);
        Assert.Contains("\"Phone\" => global::Shop.ContactPhone.__Field,", model, StringComparison.Ordinal);
        Assert.Contains("\"Remark\" => global::Shop.Remark.__Field,", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_declared_from_a_kind_of_text_that_is_not_sealed_partial_is_RASK129_naming_its_base()
    {
        var run = GeneratorHarness.Run(
            """
            using Rask.Values;
            namespace Shop;
            public partial record Loose(string Value) : Email(Value);
            """,
            [new ValueObjectFactoryGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire"]);

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK129");

        Assert.Contains("declared from Rask.Values.Email", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
