namespace Rask;

/// <summary>One country of <see cref="UiPhoneCountries" />.</summary>
/// <param name="Code">Its ISO 3166-1 alpha-2 code, upper case.</param>
/// <param name="DialCode">Its calling code, without the plus.</param>
/// <param name="Name">Its English name.</param>
internal sealed record UiPhoneCountry(string Code, string DialCode, string Name);
