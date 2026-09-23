using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="ICrypto" /> — native randomness (UUID, bytes) and hashing (SHA-256) from C#.</summary>
public sealed partial class CryptoDemo(ICrypto crypto) : Component
{
    private string _text = "hello";
    private string? _uuid;
    private string? _hash;
    private string? _bytes;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline).Id("crypto-uuid").OnClick(Uuid)["Random UUID"],
                    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline).Id("crypto-bytes").OnClick(Bytes)["Random bytes"]
                ],
                Div.Class("text-sm text-ui-muted")["UUID: ", Code.Id("crypto-uuid-value")[_uuid ?? "(none)"]],
                Div.Class("text-sm text-ui-muted mb-2")["Bytes: ", Code.Id("crypto-bytes-value")[_bytes ?? "(none)"]],
                Ui.Input
                    .Value(_text)
                    .Label("Text to hash")
                    .Id("crypto-text")
                    .Class("mb-2")
                    .OnInput(v => _text = v),
                Ui.Button.Tone(Ui.Tone.Primary).Class("mb-2").Id("crypto-hash").OnClick(Hash)["SHA-256"],
                Div.Class("text-sm text-ui-muted text-break")["Hash: ", Code.Id("crypto-hash-value")[_hash ?? "(none)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("crypto-status")[_status ?? "(idle)"]]
            ];

    private async Task Uuid()
    {
        try { _uuid = await crypto.RandomUuidAsync(); _status = "UUID generated"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Bytes()
    {
        try
        {
            var b = await crypto.RandomBytesAsync(8);
            _bytes = Convert.ToHexStringLower(b);
            _status = "Bytes generated";
        }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Hash()
    {
        try { _hash = await crypto.DigestHexAsync(HashAlgorithm.Sha256, _text); _status = "Hashed"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }
}
