namespace Rask.Batteries.Generators.Tests;

/// <summary>Finding the app's user type, and keeping its credentials off the form model.</summary>
/// <remarks>
/// <c>Rask.Auth.Authenticatable</c> is stubbed by name rather than referenced: the generator matches it by its name and
/// namespace, and pulling Rask.Auth into the harness would cost every case here for the sake of one type.
/// </remarks>
public class AuthUserGeneratorTests
{
    private const string Authenticatable = """
        namespace Rask.Auth
        {
            public abstract class Authenticatable : Rask.Data.Aggregate<System.Guid>
            {
                public string Email { get; private set; } = "";
                public System.DateTime? EmailConfirmedAt { get; private set; }
            }

            public static class AuthUser
            {
                public static void Use<TUser>() where TUser : Authenticatable, new() { }
            }
        }
        """;

    [Fact]
    public void The_one_user_type_is_named_to_the_accounts()
    {
        var run = GeneratorHarness.Run(
            Authenticatable + """

            namespace Shop
            {
                public sealed class User : Rask.Auth.Authenticatable
                {
                    public string DisplayName { get; private set; } = "";
                }
            }
            """,
            new AuthUserGenerator(),
            "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "global::Rask.Auth.AuthUser.Use<global::Shop.User>();",
            run.GeneratedSource("__RaskAuthUser"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_user_types_are_RASK074_and_wire_nothing()
    {
        var run = GeneratorHarness.Run(
            Authenticatable + """

            namespace Shop
            {
                public sealed class User : Rask.Auth.Authenticatable { }
                public sealed class Admin : Rask.Auth.Authenticatable { }
            }
            """,
            new AuthUserGenerator(),
            "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK074");
        Assert.False(run.HasGeneratedSource("__RaskAuthUser"));
    }

    [Fact]
    public void A_class_that_is_not_an_authenticatable_is_not_a_user_type()
    {
        var run = GeneratorHarness.Run(
            Authenticatable + """

            namespace Shop
            {
                public sealed class Customer : Rask.Data.Aggregate<System.Guid> { }
                public abstract class BaseUser : Rask.Auth.Authenticatable { }
            }
            """,
            new AuthUserGenerator(),
            "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

        Assert.Empty(run.Diagnostics);
        Assert.False(run.HasGeneratedSource("__RaskAuthUser"));
    }

    [Fact]
    public void The_user_s_form_model_carries_its_own_columns_and_never_the_credentials()
    {
        var run = GeneratorHarness.Run(
            Authenticatable + """

            namespace Shop
            {
                public sealed class User : Rask.Auth.Authenticatable
                {
                    public string DisplayName { get; private set; } = "";
                }
            }
            """,
            new ModelInputGenerator(),
            "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

        Assert.Empty(run.GeneratedCompileErrors());

        var model = run.GeneratedSource("Shop.UserModel");
        Assert.Contains("public string? DisplayName { get; set; }", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", model, StringComparison.Ordinal);
    }
}
