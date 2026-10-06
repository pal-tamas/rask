using System.Text;

namespace Rask.Site.Features;

/// <summary>MDN's <c>Crypto</c> from Rask.Web — native randomness (UUID, bytes) and hashing (SHA-256) from C#.</summary>
public sealed partial class CryptoDemo : Component
{
    private string _text = "hello";
    private string? _uuid;
    private string? _hash;
    private string? _bytes;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Id("crypto-uuid").OnClick(Uuid)["Random UUID"],
                    Ui.Button.Id("crypto-bytes").OnClick(Bytes)["Random bytes"]
                ],
                Div.Class("text-sm text-ui-muted")["UUID: ", Code.Id("crypto-uuid-value")[_uuid ?? "(none)"]],
                Div.Class("text-sm text-ui-muted mb-2")["Bytes: ", Code.Id("crypto-bytes-value")[_bytes ?? "(none)"]],
                Ui.Input
                    .Value(_text)
                    .Label("Text to hash")
                    .Id("crypto-text")
                    .Class("mb-2")
                    .OnInput(v => _text = v),
                Ui.Button.Primary.Class("mb-2").Id("crypto-hash").OnClick(Hash)["SHA-256"],
                Div.Class("text-sm text-ui-muted text-break")["Hash: ", Code.Id("crypto-hash-value")[_hash ?? "(none)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("crypto-status")[_status ?? "(idle)"]]
            ];

    // crypto.randomUUID()
    private async Task Uuid()
    {
        try { _uuid = await Crypto.RandomUUID(); _status = "UUID generated"; }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    // crypto.getRandomValues(new Uint8Array(8)): the browser fills the array and answers with it.
    private async Task Bytes()
    {
        try
        {
            _bytes = Convert.ToHexStringLower(await Crypto.GetRandomValues(new byte[8]));
            _status = "Bytes generated";
        }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    // crypto.subtle.digest("SHA-256", bytes): the hash comes back as a byte[].
    private async Task Hash()
    {
        try
        {
            _hash = Convert.ToHexStringLower(await Crypto.Subtle.Digest("SHA-256", Encoding.UTF8.GetBytes(_text)));
            _status = "Hashed";
        }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }
}
