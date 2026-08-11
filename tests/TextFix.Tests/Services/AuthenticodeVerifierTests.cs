using System.IO;
using TextFix.Services;

namespace TextFix.Tests.Services;

public class AuthenticodeVerifierTests
{
    /// <summary>
    /// The installed Ollama binary — a real, validly-signed file. Tests that need a
    /// genuine signature use it and quietly pass when it is absent (CI has no signed
    /// fixture; shipping a signed binary in the repo would be worse than the gap).
    /// The positive path is therefore only exercised on machines with Ollama installed.
    /// </summary>
    private static string? SignedFile()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Ollama", "ollama.exe");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Verify_RejectsAMissingFile()
    {
        var result = AuthenticodeVerifier.Verify(
            Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.exe"), "Ollama Inc.");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_RejectsAnUnsignedFile()
    {
        // The likeliest real-world failure: a truncated or substituted download has no
        // valid embedded signature. Garbage bytes with an .exe name model it exactly.
        var path = Path.Combine(Path.GetTempPath(), $"unsigned-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x90, 0x00, 0xDE, 0xAD, 0xBE, 0xEF]);
        try
        {
            var result = AuthenticodeVerifier.Verify(path, "Ollama Inc.");

            Assert.False(result.IsValid);
            // Windows reports unparseable bytes as TRUST_E_SUBJECT_FORM_UNKNOWN, not
            // as "no signature" — verified empirically, see the verifier's mapping.
            Assert.Contains("not a recognizable", result.Detail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Verify_AcceptsTheRealOllamaBinary()
    {
        var signed = SignedFile();
        if (signed is null) return; // no signed fixture on this machine

        var result = AuthenticodeVerifier.Verify(signed, "Ollama Inc.");

        Assert.True(result.IsValid, result.Detail);
    }

    [Fact]
    public void Verify_RejectsAValidSignature_FromTheWrongPublisher()
    {
        // The check that makes pinning real: a perfectly valid signature must still
        // fail when the signer is not the publisher we expect. Chain validity alone
        // proves only that SOMEBODY signed the file.
        var signed = SignedFile();
        if (signed is null) return;

        var result = AuthenticodeVerifier.Verify(signed, "Contoso Ltd.");

        Assert.False(result.IsValid);
        Assert.Contains("Ollama Inc.", result.Detail); // names who actually signed it
    }
}
