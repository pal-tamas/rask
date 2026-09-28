namespace Rask.Cli.Dev;

/// <summary>A minted certificate and the private key that goes with it, both as PEM text.</summary>
internal readonly record struct DevCertificate(string CertificatePem, string PrivateKeyPem);
