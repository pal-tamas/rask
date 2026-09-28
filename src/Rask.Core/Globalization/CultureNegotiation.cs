using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>The outcome of negotiation: which cultures to use, and what decided it.</summary>
public readonly record struct CultureNegotiation(
    CultureInfo Culture,
    CultureInfo UICulture,
    CultureSource Source);
