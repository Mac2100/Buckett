using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Buckett.Services;

/// Secret access keys are stored only in the Windows Credential Manager — the
/// Windows counterpart of the macOS Keychain. Credentials live in the user's
/// vault, encrypted at rest by the OS (DPAPI), never written to disk by the app
/// and never sent anywhere except the storage provider's API endpoint during
/// request signing.
public static class CredentialStore
{
    private const string ServicePrefix = "com.mac2100.Buckett";

    private static string TargetName(Guid accountID) => $"{ServicePrefix}:{accountID:D}";

    public static bool SetSecret(string secret, Guid accountID)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobHandle = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobHandle, blob.Length);
            var credential = new NativeCredential
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetName(accountID),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobHandle,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = accountID.ToString("D"),
                Comment = "Buckett S3 secret access key"
            };
            return CredWrite(ref credential, 0);
        }
        catch (Exception error)
        {
            Log.Warn($"failed to store credential: {error.Message}");
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(blobHandle);
        }
    }

    public static string? Secret(Guid accountID)
    {
        var handle = IntPtr.Zero;
        try
        {
            if (!CredRead(TargetName(accountID), CRED_TYPE_GENERIC, 0, out handle)) return null;
            var credential = Marshal.PtrToStructure<NativeCredential>(handle);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        catch (Exception error)
        {
            Log.Warn($"failed to read credential: {error.Message}");
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) CredFree(handle);
        }
    }

    public static void DeleteSecret(Guid accountID)
    {
        try
        {
            CredDelete(TargetName(accountID), CRED_TYPE_GENERIC, 0);
        }
        catch (Exception error)
        {
            Log.Warn($"failed to delete credential: {error.Message}");
        }
    }

    // MARK: - advapi32 interop

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);
}
