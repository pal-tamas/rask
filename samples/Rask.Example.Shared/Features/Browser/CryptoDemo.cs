using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="ICrypto" /> — native randomness (UUID, bytes) and hashing (SHA-256) from C#.</summary>
public sealed partial class CryptoDemo(ICrypto crypto) : Component
{
    private string _text = "hello";
    private string? _uuid;
    private string? _hash;
    private string? _bytes;
    private string? _status;

    protected override Component? Render() =>
        Div.Class("card shadow-sm border-0")[
            Div.Class("card-body")[
                BsStack.Gap(2).WrapItems(true).Class(Margin.Bottom(2))[
                    Button.Class("btn btn-outline-primary btn-sm").Id("crypto-uuid").OnClickAsync(Uuid)["Random UUID"],
                    Button.Class("btn btn-outline-primary btn-sm").Id("crypto-bytes").OnClickAsync(Bytes)[
                        "Random bytes"]
                ],
                Div.Class("small text-secondary")["UUID: ", Code.Id("crypto-uuid-value")[_uuid ?? "(none)"]],
                Div.Class("small text-secondary mb-2")["Bytes: ", Code.Id("crypto-bytes-value")[_bytes ?? "(none)"]],
                Input<string>()
                    .Id("crypto-text")
                    .Class("form-control form-control-sm mb-2")
                    .Value(_text)
                    .OnInput(v => _text = v),
                Button.Class("btn btn-primary btn-sm mb-2").Id("crypto-hash").OnClickAsync(Hash)["SHA-256"],
                Div.Class("small text-secondary text-break")["Hash: ", Code.Id("crypto-hash-value")[_hash ?? "(none)"]],
                Div.Class("small text-secondary")["Status: ", Code.Id("crypto-status")[_status ?? "(idle)"]]
            ]
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
