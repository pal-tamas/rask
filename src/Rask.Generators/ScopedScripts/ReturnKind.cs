namespace Rask.Generators.ScopedScripts;

internal enum ReturnKind
{
    Void,
    Value,
    Object,

    /// <summary>A C# tuple, read element by element from the array the script returns.</summary>
    Tuple,
}
