using System.Security.Cryptography;
using System.Text;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class DeterministicGuid
{
    // Stable namespace assigned only to this correction tool.
    internal static readonly Guid NamespaceId = new("02e29185-5f14-5f40-b5f7-8c584e8b22e8");

    public static Guid Create(string graphManifestSha256, Guid runId, string entityKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphManifestSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        return CreateVersion5(NamespaceId,
            $"reference-average-history-correction-v2/{graphManifestSha256}/{runId:D}/{entityKind}");
    }

    internal static Guid CreateVersion5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var guidBytes = hash[..16];
        SwapByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
