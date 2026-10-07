using System.Runtime.InteropServices;
using System.Text;

namespace InvisibleAI.Companion.Settings;

public interface ICredentialStore { string? Read(); void Write(string secret); void Delete(); }
public sealed class CredentialStore(string target) : ICredentialStore
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint buffer);
    public string? Read()
    {
        if (!CredRead(target, 1, 0, out var ptr))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new InvalidOperationException("Windows Credential Manager is unavailable.");
        }
        try { var c = Marshal.PtrToStructure<Credential>(ptr); return Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2); }
        finally { CredFree(ptr); }
    }
    public void Write(string secret)
    {
        secret = secret.Trim();
        if (secret.Length < 8 || secret.Length > 2000) throw new ArgumentException("Credential must contain 8–2000 characters.");
        var ptr = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var c = new Credential { Type = 1, TargetName = target, UserName = Environment.UserName,
                Persist = 2, CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(secret), CredentialBlob = ptr };
            if (!CredWrite(ref c, 0)) throw new InvalidOperationException("Could not save the credential in Windows Credential Manager.");
        }
        finally { for (int i = 0; i < secret.Length * 2; i++) Marshal.WriteByte(ptr, i, 0); Marshal.FreeCoTaskMem(ptr); }
    }
    public void Delete()
    { if (!CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new InvalidOperationException("Could not remove the credential."); }
}
