namespace Rask;

/// <summary>A control that asks what day it is, and can be told — by a test, or by a parity page pinned to Flux's.</summary>
internal interface IUiClock
{
    /// <summary>The day "today" is; the system clock's while unset.</summary>
    DateOnly? Today { get; set; }
}
