namespace Rask.Core;

// On every enum the MDN emitter writes for an attribute's keywords (Loading, Dir, ReferrerPolicy): its members are values,
// so the chain generator offers no step per member — `Img.Lazy` would read as the image being lazy, not as its loading.
// Matched by name, like BrowserSupport: only Rask.Core declares one.
[AttributeUsage(AttributeTargets.Enum, Inherited = false)]
internal sealed class KeywordsAttribute : Attribute;
