using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;
using Rask.Cqrs;

namespace Rask.Core.Tests.Security;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ValidationSwitchCollection
{
    public const string Name = "ValidationSwitch";
}

// RaskValidation.AutoValidate is the forms' switch. The server's request validators read it too, so an app that
// stopped its forms validating on their own also let every dispatched request reach its handler unchecked.
[Collection(ValidationSwitchCollection.Name)]
public sealed class ServerRequestValidationTests
{
    [Fact]
    public async Task The_server_still_validates_a_request_after_forms_are_told_not_to()
    {
        var before = RaskValidation.AutoValidate;
        using var services = new ServiceCollection().BuildServiceProvider();
        var validator = new DataAnnotationsRequestValidator<Rename>(services);
        IReadOnlyList<RequestValidationError> errors;

        RaskValidation.AutoValidate = false;
        try
        {
            errors = await validator.Validate(new Rename(Name: null));
        }
        finally
        {
            RaskValidation.AutoValidate = before;
        }

        Assert.Contains(errors, static e => e.Field == nameof(Rename.Name));
    }

    private sealed record Rename([property: Required] string? Name);
}
