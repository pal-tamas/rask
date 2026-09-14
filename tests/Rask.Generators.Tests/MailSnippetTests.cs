using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

// src/Rask.Mail/NUGET.md — the README nuget.org shows for Rask.Mail, and docs/mail.md, which teaches the same component.
// Both still taught the factory call (`Div()[…]`, `WelcomeEmail(Name: …)`) long after the chain replaced it, and nothing
// compiled either (#1082). The component and the body are mirrored here character-for-character; the sending code around
// the body needs Rask.Mail's Email builder, which this reference set does not carry, so a markup host stands in for it.
public sealed class MailSnippetTests
{
    [Fact]
    public void The_welcome_email_component_and_its_body_compile() => AssertCompiles("""
        using Rask.Core;

        namespace Demo;

        public sealed partial class WelcomeEmail : Component
        {
            public string Name { get; set; } = "";

            protected override Component? Render() =>
                Div[H1[$"Welcome, {Name}!"], P["Thanks for signing up."]];
        }

        [RaskMarkup]
        public static partial class SignUp
        {
            public static Component WelcomeBody(string name) => WelcomeEmail.Name(name);
        }
        """);

    private static void AssertCompiles(string source)
    {
        var errors = BuilderGeneratorHarness.Compile(source)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            "The snippet did not compile:\n" + string.Join(
                "\n", errors.Select(d => $"  {d.Id} {d.Location.GetLineSpan().StartLinePosition}: {d.GetMessage()}")));
    }
}
