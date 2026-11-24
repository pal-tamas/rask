using FluentValidation;

namespace Rask;

internal static class BorrowedContext
{
    // A check that reads the aggregate — Must((s, v) => v < s.Limit) — finds none in a form and cannot be judged there:
    // it passes the form, and the save, which asks the aggregate itself, judges it.
#pragma warning disable S1696, CA1031 // the null the check met is the aggregate a form does not have, said above
    internal static bool Asks(Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (NullReferenceException)
        {
            return true;
        }
    }

    internal static async Task<bool> AsksAsync(Func<Task<bool>> check)
    {
        try
        {
            return await check().ConfigureAwait(false);
        }
        catch (NullReferenceException)
        {
            return true;
        }
    }
#pragma warning restore S1696, CA1031

    internal static void CarryOver<TOwner, TModel>(ValidationContext<TOwner> borrowed, ValidationContext<TModel> context)
    {
        foreach (var (name, value) in borrowed.MessageFormatter.PlaceholderValues)
        {
            context.MessageFormatter.AppendArgument(name, value);
        }
    }
}
