namespace Rask.Core.Tests;

// The test app's shared validation resources, Messages/ValidationMessages.resx and its Hungarian twin: what an app
// names in AddValidation(o => o.LocalizerProvider = (_, f) => f.Create(typeof(ValidationMessages))). In the root
// namespace and outside Messages/, so the resources keep the name the localizer looks for.
public sealed class ValidationMessages;
