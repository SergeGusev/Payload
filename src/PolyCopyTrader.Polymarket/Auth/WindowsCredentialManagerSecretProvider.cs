using System.Runtime.InteropServices;
using System.Text;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class WindowsCredentialManagerSecretProvider : ISecretProvider
{
    private const uint GenericCredentialType = 1;

    public Task<string?> GetSecretAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ct.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<string?>(null);
        }

        if (!CredRead(name, GenericCredentialType, 0, out var credentialPtr))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(null);
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            var value = DecodeCredentialBlob(bytes);
            return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private static string DecodeCredentialBlob(byte[] bytes)
    {
        var utf8 = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        if (IsMostlyPrintable(utf8))
        {
            return utf8;
        }

        return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    private static bool IsMostlyPrintable(string value)
    {
        return value.Length > 0 && value.Count(char.IsControl) <= Math.Max(1, value.Length / 10);
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPtr);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct NativeCredential
    {
        public readonly uint Flags;
        public readonly uint Type;
        public readonly IntPtr TargetName;
        public readonly IntPtr Comment;
        public readonly long LastWritten;
        public readonly uint CredentialBlobSize;
        public readonly IntPtr CredentialBlob;
        public readonly uint Persist;
        public readonly uint AttributeCount;
        public readonly IntPtr Attributes;
        public readonly IntPtr TargetAlias;
        public readonly IntPtr UserName;
    }
}
