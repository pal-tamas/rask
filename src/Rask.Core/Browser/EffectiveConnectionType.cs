namespace Rask.Core.Browser;

/// <summary>The browser's effective connection quality (<c>NetworkInformation.effectiveType</c>).</summary>
public enum EffectiveConnectionType
{
    /// <summary>Unknown / not reported by the browser.</summary>
    Unknown,

    /// <summary><c>slow-2g</c>.</summary>
    Slow2g,

    /// <summary><c>2g</c>.</summary>
    TwoG,

    /// <summary><c>3g</c>.</summary>
    ThreeG,

    /// <summary><c>4g</c> (or better).</summary>
    FourG
}
