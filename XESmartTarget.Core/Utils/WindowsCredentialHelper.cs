using System.Runtime.InteropServices;

namespace XESmartTarget.Core.Utils
{
    public class WindowsCredentialHelper
    {
        [DllImport("Advapi32.dll", SetLastError = true, EntryPoint = "CredReadW", CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, CredentialType type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", SetLastError = true, EntryPoint = "CredWriteW", CharSet = CharSet.Unicode)]
        private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] UInt32 flags);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern bool CredFree([In] IntPtr cred);

        [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredDelete(string target, CredentialType type, int flags);


        private enum CredentialType
        {
            GENERIC = 1,
            DOMAIN_PASSWORD = 2,
            DOMAIN_CERTIFICATE = 3,
            DOMAIN_VISIBLE_PASSWORD = 4,
            GENERIC_CERTIFICATE = 5,
            DOMAIN_EXTENDED = 6,
            MAXIMUM = 7,  // Maximum supported cred type
            MAXIMUM_EX = (MAXIMUM + 1000),  // Allow new applications to run on old OSes
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public CredentialType Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        public static (string username, string password) ReadCredential(string target)
        {
            if (CredRead(target, CredentialType.GENERIC, 0, out IntPtr credPointer))
            {
                CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credPointer);

                string? username = Marshal.PtrToStringUni(cred.UserName);
                string? password = Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);

                CredFree(credPointer);

                return (username ?? string.Empty, password ?? string.Empty);
            }
            else
                return ("", "");
        }
    }
}
