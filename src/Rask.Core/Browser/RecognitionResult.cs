namespace Rask.Core.Browser;

/// <summary>One recognition result (a <c>SpeechRecognitionResult</c>).</summary>
/// <param name="Transcript">The recognised text.</param>
/// <param name="IsFinal">Whether this is a final result (<c>true</c>) or an interim hypothesis (<c>false</c>).</param>
/// <param name="Confidence">Recognition confidence, <c>0.0</c>–<c>1.0</c>; <c>0</c> when the platform omits it.</param>
public sealed record RecognitionResult(string Transcript, bool IsFinal, double Confidence);
