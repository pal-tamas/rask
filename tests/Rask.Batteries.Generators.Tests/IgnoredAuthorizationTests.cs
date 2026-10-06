using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

// #1187: a handler's authorization is read by name at compile time, so one the generators cannot read is refused.
public sealed class IgnoredAuthorizationTests
{
    // The attributes are matched by name, so the test declares its own rather than pulling in ASP.NET.
    private const string Preamble = """
        using System;
        using System.Threading.Tasks;
        using Rask.Cqrs;
        namespace Demo;

        public interface IAuthorizeData;
        [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class AuthorizeAttribute : Attribute, IAuthorizeData { public string? Policy { get; set; } public string? Roles { get; set; } }
        [AttributeUsage(AttributeTargets.All)] public sealed class AllowAnonymousAttribute : Attribute;
        public sealed class AdminOnlyAttribute : AuthorizeAttribute { public AdminOnlyAttribute() => Roles = "admin"; }
        public sealed record Wipe : ICommand;

        """;

    [Fact]
    public void A_handler_carrying_an_attribute_derived_from_Authorize_is_a_compile_error()
    {
        var run = Run("""
            [AdminOnly]
            public sealed class WipeHandler : ICommandHandler<Wipe>
            {
                public Task Handle(Wipe command) => Task.CompletedTask;
            }
            """);

        var error = Assert.Single(run.Diagnostics, d => d.Id == "RASK101");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("AdminOnly", Written(run, error));
        Assert.Contains("'AdminOnlyAttribute' on 'WipeHandler' is ignored", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_attribute_implementing_IAuthorizeData_on_a_handler_is_a_compile_error()
    {
        var run = Run("""
            public sealed class StaffAttribute : Attribute, IAuthorizeData;
            [Staff]
            public sealed class WipeHandler : ICommandHandler<Wipe>
            {
                public Task Handle(Wipe command) => Task.CompletedTask;
            }
            """);

        var error = Assert.Single(run.Diagnostics, d => d.Id == "RASK101");

        Assert.Equal("Staff", Written(run, error));
        Assert.Contains("'StaffAttribute' on 'WipeHandler' is ignored", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_record_carrying_a_derived_Authorize_is_a_compile_error()
    {
        var run = Run("""
            [AdminOnly]
            public sealed record Wiped(Guid Id) : IEvent;
            """);

        var error = Assert.Single(run.Diagnostics, d => d.Id == "RASK101");

        Assert.Equal("AdminOnly", Written(run, error));
        Assert.Contains("'AdminOnlyAttribute' on 'Wiped' is ignored", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_derived_Authorize_on_a_handlers_base_class_is_a_compile_error()
    {
        var run = Run("""
            public sealed record Purge : ICommand;
            [AdminOnly]
            public abstract class AdminHandler;
            public sealed class WipeHandler : AdminHandler, ICommandHandler<Wipe>
            {
                public Task Handle(Wipe command) => Task.CompletedTask;
            }
            public sealed class PurgeHandler : AdminHandler, ICommandHandler<Purge>
            {
                public Task Handle(Purge command) => Task.CompletedTask;
            }
            """);

        var error = Assert.Single(run.Diagnostics, d => d.Id == "RASK101");

        Assert.Equal("AdminOnly", Written(run, error));
        Assert.Contains("'AdminOnlyAttribute' on 'AdminHandler' is ignored", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_Authorize_on_the_Handle_method_is_a_compile_error()
    {
        var run = Run("""
            public sealed class WipeHandler : ICommandHandler<Wipe>
            {
                [Authorize(Roles = "admin")]
                public Task Handle(Wipe command) => Task.CompletedTask;
            }
            """);

        var error = Assert.Single(run.Diagnostics, d => d.Id == "RASK101");

        Assert.Equal("Authorize(Roles = \"admin\")", Written(run, error));
        Assert.Contains("'AuthorizeAttribute' on 'WipeHandler.Handle' is ignored", error.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("move it there", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_Authorize_on_a_handler_reports_nothing()
    {
        var run = Run("""
            [Authorize(Roles = "admin")]
            public sealed class WipeHandler : ICommandHandler<Wipe>
            {
                public Task Handle(Wipe command) => Task.CompletedTask;
            }
            [Authorize, AllowAnonymous]
            public sealed record Wiped(Guid Id) : IEvent;
            """);

        var reported = run.Diagnostics.Where(d => d.Id == "RASK101");

        Assert.Empty(reported);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    // Both generators in one driver, as a build with a transport runs them: an application is reported once, not once each.
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            Preamble + source,
            [new CqrsDispatchGenerator(), new CqrsCodecGenerator()],
            "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Wire", "Microsoft.Extensions.DependencyInjection.Abstractions");

    // The source text the diagnostic points at.
    private static string Written(GeneratorRun run, Diagnostic diagnostic) =>
        run.Compilation.SyntaxTrees[0].GetText().ToString(diagnostic.Location.SourceSpan);
}
