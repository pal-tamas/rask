using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Rask.Tests;

/// <summary>
///     The one model <see cref="ApiValidationTests" /> sends everywhere: to a controller, to a minimal
///     API, and through a form's own <c>EditContext</c>. One type with one validator is the whole point —
///     "my rules run in the form but not on the endpoint" is the failure being tested away.
/// </summary>
public sealed class Order
{
    /// <summary>The order reference. Required, and checked for uniqueness asynchronously.</summary>
    [Required]
    public string? Reference { get; set; }

    /// <summary>How many. Between 1 and 100.</summary>
    [Range(1, 100)]
    public int Quantity { get; set; } = 1;
}

/// <summary>What the asynchronous rule looks at, and the evidence that it ran.</summary>
/// <remarks>
///     Static rather than injected deliberately: the assertion this suite has to be able to make is
///     "the <c>MustAsync</c> body actually executed", and a counter it increments is the only thing that
///     can say so. A test that only sees a 400 cannot tell an async rule from a synchronous one.
/// </remarks>
public static class OrderRules
{
    private static int _runs;

    /// <summary>The reference the asynchronous rule rejects.</summary>
    public const string TakenReference = "taken";

    /// <summary>How many times the <c>MustAsync</c> body has run.</summary>
    public static int Runs => Volatile.Read(ref _runs);

    /// <summary>Counts one run of the asynchronous rule.</summary>
    public static void Ran() => Interlocked.Increment(ref _runs);
}

/// <summary>
///     An ordinary <c>AbstractValidator&lt;T&gt;</c> — no attribute, no registration. The generator finds
///     it at compile time, which is what makes it reachable from a form and from an endpoint alike.
/// </summary>
public sealed class OrderValidator : AbstractValidator<Order>
{
    /// <summary>Declares the rules.</summary>
    public OrderValidator() =>
        RuleFor(order => order.Reference)
            .MustAsync(async (reference, cancellationToken) =>
            {
                OrderRules.Ran();

                // A real rule here asks a database. Yielding is enough to make the point that this rule
                // cannot run on a synchronous pass, which is why neither ModelState nor
                // Validator.TryValidateObject could ever have enforced it.
                await Task.Yield();

                return !string.Equals(reference, OrderRules.TakenReference, StringComparison.Ordinal);
            })
            .WithMessage("That reference is already used.");
}

/// <summary>A service with a member that would fail validation if a service were ever validated.</summary>
/// <remarks>
///     The guard for the rule that decides which bound arguments are the caller's. Walking a container's
///     objects — a <c>DbContext</c>, above all — would be both a correctness and a performance disaster,
///     so an endpoint that injects this must still answer 200.
/// </remarks>
public sealed class Ledger
{
    /// <summary>Never set, and never validated.</summary>
    [Required]
    public string? Name { get; set; }
}

/// <summary>An ordinary API controller taking a body. Nothing here is validation-aware.</summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    /// <summary>Echoes the order back, so reaching the handler is observable.</summary>
    /// <param name="body">The order.</param>
    /// <returns>The same order.</returns>
    [HttpPost("")]
    public ActionResult<Order> Place(Order body) => body;
}
