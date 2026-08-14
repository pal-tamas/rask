using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Speech;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for ISpeechRecognition — the platform SpeechRecognizer instead of the WebView's
// webkitSpeechRecognition. Registered by AndroidPlatform. Needs the RECORD_AUDIO permission. SpeechRecognizer
// must be created and driven on the main thread, so every interaction is posted to the main looper; each
// (partial) result is pushed to the callback. Android's recognizer stops after each utterance, so continuous
// mode restarts it until the session is disposed.
internal sealed class NativeSpeechRecognition(Context context) : ISpeechRecognition
{
    public ValueTask<bool> IsSupportedAsync() =>
        ValueTask.FromResult(SpeechRecognizer.IsRecognitionAvailable(context));

    public ValueTask<IAsyncDisposable> StartAsync(
        Func<RecognitionResult, Task> onResult, SpeechRecognitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(onResult);
        return new ValueTask<IAsyncDisposable>(new Session(context, onResult, options));
    }

    private sealed class Session : Java.Lang.Object, IRecognitionListener, IAsyncDisposable
    {
        private readonly Context _context;
        private readonly Func<RecognitionResult, Task> _onResult;
        private readonly bool _continuous;
        private readonly bool _interim;
        private readonly string? _lang;
        private readonly Handler _main = new(Looper.MainLooper!);
        private SpeechRecognizer? _recognizer;
        private bool _stopped;

        public Session(Context context, Func<RecognitionResult, Task> onResult, SpeechRecognitionOptions? options)
        {
            _context = context;
            _onResult = onResult;
            _continuous = options?.Continuous ?? false;
            _interim = options?.InterimResults ?? false;
            _lang = options?.Lang;
            _main.Post(StartListening);
        }

        private void StartListening()
        {
            _recognizer ??= SpeechRecognizer.CreateSpeechRecognizer(_context);
            if (_recognizer is null)
            {
                return;
            }

            _recognizer.SetRecognitionListener(this);
            using var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, _interim);
            if (_lang is not null)
            {
                intent.PutExtra(RecognizerIntent.ExtraLanguage, _lang);
            }

            _recognizer.StartListening(intent);
        }

        private void Emit(Bundle? results, bool isFinal)
        {
            var matches = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (matches is { Count: > 0 })
            {
                _ = _onResult(new RecognitionResult(matches[0]!, isFinal, 0));
            }
        }

        public void OnResults(Bundle? results)
        {
            Emit(results, true);
            if (_continuous && !_stopped)
            {
                _main.Post(StartListening);
            }
        }

        public void OnPartialResults(Bundle? partialResults) => Emit(partialResults, false);

        public void OnError([GeneratedEnum] SpeechRecognizerError error)
        {
            // Restart on a recoverable no-match / timeout while continuous; otherwise let the session idle.
            if (_continuous && !_stopped &&
                error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout)
            {
                _main.Post(StartListening);
            }
        }

        // Remaining IRecognitionListener members carry nothing we surface.
        public void OnReadyForSpeech(Bundle? @params) { }

        public void OnBeginningOfSpeech() { }

        public void OnRmsChanged(float rmsdB) { }

        public void OnBufferReceived(byte[]? buffer) { }

        public void OnEndOfSpeech() { }

        public void OnEvent(int eventType, Bundle? @params) { }

        public ValueTask DisposeAsync()
        {
            _stopped = true;
            _main.Post(() =>
            {
                _recognizer?.StopListening();
                _recognizer?.Destroy();
                _recognizer = null;
            });
            return default;
        }
    }
}
