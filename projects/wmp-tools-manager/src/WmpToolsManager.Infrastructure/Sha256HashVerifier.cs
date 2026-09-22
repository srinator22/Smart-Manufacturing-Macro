// Purpose: Compute the SHA-256 of a downloaded artifact so it can be compared with the published
//   digest. ADR-0005 accepts no code-signing certificate, so this digest plus HTTPS to the pinned
//   repository is the whole integrity story.
// Inputs: A path to a file already written into the state root.
// Outputs: Lowercase hex, matching the case SHA256SUMS.txt is written in, or the reason it is absent.
// Dependencies: System.Security.Cryptography and System.IO.
// Assumptions: The file is streamed rather than loaded, because a release package is tens of megabytes
//   and this runs inside an Inventor command.
// Validation source: scripts/release/build-release.ps1, which writes
//   (Get-FileHash -Algorithm SHA256).Hash.ToLowerInvariant().

using System.Security.Cryptography;
using WmpToolsManager.Application;

namespace WmpToolsManager.Infrastructure;

public sealed class Sha256HashVerifier : IHashVerifier
{
    public HashResult ComputeSha256(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new HashResult(Convert.ToHexStringLower(SHA256.HashData(stream)), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new HashResult(null, $"'{filePath}' could not be hashed: {exception.Message}");
        }
    }
}
