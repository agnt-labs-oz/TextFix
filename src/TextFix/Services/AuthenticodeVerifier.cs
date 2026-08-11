using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using TextFix.Interop;

namespace TextFix.Services;

/// <summary>
/// Verifies a downloaded executable's Authenticode signature before it may be launched.
/// </summary>
/// <remarks>
/// Two independent checks, both required:
///
/// 1. <b>The signature is valid</b> — WinVerifyTrust walks the embedded signature and
///    its certificate chain. This proves the file is exactly what some publisher
///    signed, i.e. it was not corrupted or swapped in transit.
/// 2. <b>The publisher is the one we expect</b> — validity alone is not enough, because
///    anyone can validly sign an installer. The certificate's subject CN must equal the
///    pinned name. Extracted with <see cref="X509Certificate2.GetNameInfo"/> rather
///    than a substring test on the DN, since other DN components can legally contain
///    the pinned literal.
///
/// Revocation is deliberately NOT checked (WTD_REVOKE_NONE). The threat this guards
/// against is a tampered or substituted download; a validly-signed-then-revoked Ollama
/// certificate is not that threat, and a revocation lookup adds a network dependency
/// that can hang the setup dialog behind corporate proxies. Reconsider if this class is
/// ever reused for something whose threat model includes revoked publishers.
/// </remarks>
public static class AuthenticodeVerifier
{
    public sealed record Result(bool IsValid, string Detail);

    public static Result Verify(string filePath, string requiredSubjectCn)
    {
        if (!File.Exists(filePath))
            return new Result(false, "File does not exist.");

        var status = VerifySignature(filePath);
        if (status != 0)
            return new Result(false, status switch
            {
                // TRUST_E_NOSIGNATURE — by far the likeliest failure: a truncated or
                // substituted download simply has no valid embedded signature.
                unchecked((int)0x800B0100) => "The file is not signed, or the signature is corrupt.",
                // TRUST_E_SUBJECT_FORM_UNKNOWN — the file is so damaged Windows cannot
                // even parse it as an executable. Verified empirically: garbage bytes
                // with an .exe name land here, not on NOSIGNATURE.
                unchecked((int)0x800B0003) => "The file is not a recognizable signed executable.",
                unchecked((int)0x800B0101) => "The signing certificate has expired.",
                unchecked((int)0x800B0109) => "The signature's certificate chain does not lead to a trusted root.",
                _ => $"Signature verification failed (0x{status:X8}).",
            });

        try
        {
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            var cn = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (!string.Equals(cn, requiredSubjectCn, StringComparison.Ordinal))
                return new Result(false, $"Signed by \"{cn}\" — expected \"{requiredSubjectCn}\".");

            return new Result(true, $"Valid signature from \"{cn}\".");
        }
        catch (Exception ex)
        {
            return new Result(false, $"Could not read the signing certificate: {ex.Message}");
        }
    }

    /// <summary>Returns WinVerifyTrust's status: 0 for a valid embedded signature.</summary>
    private static int VerifySignature(string filePath)
    {
        var pathPtr = Marshal.StringToHGlobalUni(filePath);
        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
            pcwszFilePath = pathPtr,
        };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
        Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

        var data = new NativeMethods.WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
            dwUIChoice = NativeMethods.WTD_UI_NONE,
            fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE, // see class remarks
            dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
            pFile = fileInfoPtr,
            dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
        };

        try
        {
            return NativeMethods.WinVerifyTrust(
                IntPtr.Zero, in NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
        }
        finally
        {
            // A VERIFY call allocates provider state that only a CLOSE call releases —
            // skipping this is the classic WinVerifyTrust handle leak.
            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(
                IntPtr.Zero, in NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
            Marshal.FreeHGlobal(fileInfoPtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }
}
