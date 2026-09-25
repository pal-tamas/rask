namespace Rask.Testing;

/// <summary>Severity of a captured framework diagnostic.</summary>
public enum DiagnosticLevel
{
    /// <summary>Something worth knowing that is not a fault.</summary>
    Information,

    /// <summary>A degradation the app survives — a budget exceeded, a feature falling back.</summary>
    Warning,

    /// <summary>A fault the framework caught and did not let escape.</summary>
    Error,
}
