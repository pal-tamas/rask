using AVFoundation;
using Foundation;
using Rask.Core.Browser;
using Speech;

namespace Rask.Native;

// Native iOS backend for ISpeechRecognition — SFSpeechRecognizer + AVAudioEngine instead of the WebView's
// webkitSpeechRecognition, which WKWebView does not implement. Registered by ApplePlatform. Needs the
// NSSpeechRecognitionUsageDescription + NSMicrophoneUsageDescription Info.plist keys; StartAsync requests
// authorization and streams microphone audio into the recogniser, pushing each transcription to the callback.
internal sealed class NativeSpeechRecognition : ISpeechRecognition
{
    public ValueTask<bool> IsSupportedAsync()
    {
        var recognizer = new SFSpeechRecognizer();
        return ValueTask.FromResult(recognizer.Available);
    }

    public async ValueTask<IAsyncDisposable> StartAsync(
        Func<RecognitionResult, Task> onResult, SpeechRecognitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(onResult);

        if (!await RequestAuthorizationAsync())
        {
            throw new InvalidOperationException("Speech recognition was not authorized.");
        }

        var session = new Session(onResult, options);
        session.Start();
        return session;
    }

    private static Task<bool> RequestAuthorizationAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        SFSpeechRecognizer.RequestAuthorization(status =>
            tcs.TrySetResult(status == SFSpeechRecognizerAuthorizationStatus.Authorized));
        return tcs.Task;
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly AVAudioEngine _engine = new();
        private readonly SFSpeechRecognizer _recognizer;
        private readonly Func<RecognitionResult, Task> _onResult;
        private readonly bool _interim;
        private readonly bool _continuous;
        private SFSpeechAudioBufferRecognitionRequest? _request;
        private SFSpeechRecognitionTask? _task;
        private bool _stopped;
        private bool _disposed;

        public Session(Func<RecognitionResult, Task> onResult, SpeechRecognitionOptions? options)
        {
            _onResult = onResult;
            _interim = options?.InterimResults ?? false;
            _continuous = options?.Continuous ?? false;
            _recognizer = options?.Lang is { } lang
                ? new SFSpeechRecognizer(NSLocale.FromLocaleIdentifier(lang))
                : new SFSpeechRecognizer();
        }

        public void Start()
        {
            var audioSession = AVAudioSession.SharedInstance();
            audioSession.SetCategory(AVAudioSessionCategory.Record, AVAudioSessionCategoryOptions.DuckOthers);
            audioSession.SetActive(true, out _);

            _request = new SFSpeechAudioBufferRecognitionRequest { ShouldReportPartialResults = _interim };

            var inputNode = _engine.InputNode;
            _task = _recognizer.GetRecognitionTask(_request, (result, error) =>
            {
                if (result is not null)
                {
                    _ = _onResult(new RecognitionResult(result.BestTranscription.FormattedString, result.Final, 0));

                    // Match the browser/Android contract: without Continuous, stop after the first final
                    // utterance rather than streaming until DisposeAsync (AVAudioEngine has no built-in stop).
                    if (result.Final && !_continuous)
                    {
                        StopListening();
                    }
                }
            });

            var format = inputNode.GetBusOutputFormat(0);
            inputNode.InstallTapOnBus(0, 1024, format, (buffer, when) =>
            {
                _ = when;
                _request?.Append(buffer);
            });

            _engine.Prepare();
            _engine.StartAndReturnError(out _);
        }

        private void StopListening()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _engine.InputNode.RemoveTapOnBus(0);
            _engine.Stop();
            _request?.EndAudio();
            _task?.Cancel();
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return default;
            }

            _disposed = true;
            StopListening();
            return default;
        }
    }
}
