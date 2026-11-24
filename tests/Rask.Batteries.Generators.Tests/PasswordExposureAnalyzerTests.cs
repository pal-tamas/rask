using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK173 and RASK174: a password value object is input only — never stored on an aggregate or an entity, never
/// carried outward by a result, a subscription's item or an event — and a command that takes one is what it is for.
/// </summary>
public class PasswordExposureAnalyzerTests
{
    private const string Values = """
        using System;
        using System.Collections.Generic;
        using Rask.Cqrs;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;

        public sealed partial record SignInPassword(string Value) : Password(Value, MinLength: 8);
        public sealed partial record ContactEmail(string Value) : Email(Value);
        public sealed partial record Credentials(ContactEmail Email, SignInPassword Password) : Value;

        """;

    private static Task<IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic>> Run(string source) =>
        ModelStateAnalyzerHarness.RunAsync(new PasswordExposureAnalyzer(), Values + source);

    [Fact]
    public async Task An_aggregate_that_stores_a_password_is_RASK173()
    {
        var diagnostics = await Run("""
            public sealed class User : Aggregate<Guid>
            {
                private User() { }
                public ContactEmail Email { get; private set; } = null!;
                public SignInPassword Password { get; private set; } = null!;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("RASK173", diagnostic.Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Password", diagnostic.Flagged());
        Assert.Contains("'User.Password' stores the password 'SignInPassword'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_password_inside_a_value_object_or_a_list_the_entity_holds_is_RASK173()
    {
        var diagnostics = await Run("""
            public sealed class Account : Aggregate<Guid>
            {
                private Account() { }
                public Credentials? Login { get; private set; }
                public List<SignInPassword> Previous { get; private set; } = [];
            }
            """);

        Assert.Equal(["Login", "Previous"], diagnostics.Select(d => d.Flagged()));
        Assert.All(diagnostics, d => Assert.Equal("RASK173", d.Id));
    }

    [Fact]
    public async Task A_computed_property_and_a_plain_class_holding_a_password_are_not_reported()
    {
        var diagnostics = await Run("""
            public sealed class User : Aggregate<Guid>
            {
                private User() { }
                public string PasswordHash { get; private set; } = "";
                public SignInPassword? Typed => null;
            }

            public sealed class SignInForm
            {
                public SignInPassword? Password { get; set; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_command_that_takes_a_password_is_not_reported()
    {
        var diagnostics = await Run("""
            public sealed record SignIn(ContactEmail Email, SignInPassword Password) : ICommand<string>;
            public sealed record ChangePassword(SignInPassword Old, SignInPassword New) : ICommand;
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_result_an_item_or_an_event_that_carries_a_password_is_RASK174()
    {
        var diagnostics = await Run("""
            public sealed record Login(ContactEmail Email, SignInPassword Password);
            public sealed record FindLogin(Guid Id) : IQuery<Login>;
            public sealed record Reset(Guid Id) : ICommand<IReadOnlyList<SignInPassword>>;
            public sealed record PasswordChanged(SignInPassword Password) : IEvent;
            """);

        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("RASK174", d.Id));
        Assert.Contains("'FindLogin' sends the password 'SignInPassword' back through 'IQuery<Login>'", diagnostics[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("through 'IEvent'", diagnostics[2].GetMessage(), StringComparison.Ordinal);
    }
}
