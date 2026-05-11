using System.Runtime.InteropServices;
using System.Text;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class WindowsCredentialManagerSecretProvider : ISecretProvider, ISecretWriter
{
    private const uint GenericCredentialType = 1;
    private const uint PersistLocalMachine = 2;

    public Task<string?> GetSecretAsync(string name, CancellationToken ct)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        ct.ThrowIfCancellationRequested();

        if (!IsWindows())
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

    public Task SetSecretAsync(string name, string value, CancellationToken ct)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(value, nameof(value));
        ct.ThrowIfCancellationRequested();

        if (!IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager is only available on Windows.");
        }

        var targetNamePtr = IntPtr.Zero;
        var userNamePtr = IntPtr.Zero;
        var credentialBlobPtr = IntPtr.Zero;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            targetNamePtr = Marshal.StringToCoTaskMemUni(name);
            userNamePtr = Marshal.StringToCoTaskMemUni("polycopytrader");
            credentialBlobPtr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, credentialBlobPtr, bytes.Length);

            var credential = new NativeCredentialWrite
            {
                Type = GenericCredentialType,
                TargetName = targetNamePtr,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = credentialBlobPtr,
                Persist = PersistLocalMachine,
                UserName = userNamePtr
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Failed to write Windows credential '{name}'. Win32Error={Marshal.GetLastWin32Error()}.");
            }

            return Task.CompletedTask;
        }
        finally
        {
            if (credentialBlobPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(credentialBlobPtr);
            }

            if (targetNamePtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(targetNamePtr);
            }

            if (userNamePtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(userNamePtr);
            }
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

    private static bool IsWindows()
    {
        var platform = Environment.OSVersion.Platform;
        return platform == PlatformID.Win32NT ||
            platform == PlatformID.Win32S ||
            platform == PlatformID.Win32Windows ||
            platform == PlatformID.WinCE;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredentialWrite userCredential, uint flags);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredentialWrite
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
