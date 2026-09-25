namespace Rask.Server.Tests.App;

/// <summary>
/// Serialised on purpose, and only these. Every test in the collection builds a <see cref="RaskApp"/>, and
/// <c>RaskApp.Build</c> writes PROCESS-WIDE state: <c>RaskValidation.AutoValidate</c> is a static property (a
/// Form has no options object to consult and exists on both hosts), so an app built with
/// <c>c.Validation.Off()</c> turns validation off for every other test running at that moment. Run in
/// parallel, that is a flake that reads as "validation is unreliable". A collection that disables
/// parallelisation runs alone, and leaves the rest of this suite parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RaskAppCollection
{
    public const string Name = "RaskApp";
}
