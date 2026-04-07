using System.Runtime.InteropServices;
using System.Text;

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

        public static (string username, string password, string authScheme) ReadCredential(string target)
        {
            if (CredRead(target, CredentialType.GENERIC, 0, out IntPtr credPointer))
            {
                CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credPointer);

                string? username = Marshal.PtrToStringUni(cred.UserName);
                string? password = Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);
                string? authScheme = Marshal.PtrToStringUni(cred.Comment);

                CredFree(credPointer);

                return (username ?? string.Empty, password ?? string.Empty, string.IsNullOrEmpty(authScheme) ? "Basic" : authScheme);
            }
            else
                return ("", "", "Basic");
        }

        public static void WriteCredential(string target, string username, string password, string authScheme = "Basic")
        {
            byte[] byteArray = Encoding.Unicode.GetBytes(password);
            IntPtr commentPtr = IntPtr.Zero;
            IntPtr targetNamePtr = IntPtr.Zero;
            IntPtr credentialBlobPtr = IntPtr.Zero;
            IntPtr userNamePtr = IntPtr.Zero;
            try
            {
                commentPtr = Marshal.StringToHGlobalUni(authScheme);
                targetNamePtr = Marshal.StringToHGlobalUni(target);
                credentialBlobPtr = Marshal.AllocHGlobal(byteArray.Length);
                userNamePtr = Marshal.StringToHGlobalUni(username);

                Marshal.Copy(byteArray, 0, credentialBlobPtr, byteArray.Length);

                CREDENTIAL credential = new CREDENTIAL
                {
                    Flags = 0,
                    Type = CredentialType.GENERIC,
                    TargetName = targetNamePtr,
                    Comment = commentPtr,
                    CredentialBlobSize = (uint)byteArray.Length,
                    CredentialBlob = credentialBlobPtr,
                    Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
                    UserName = userNamePtr,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = IntPtr.Zero,
                    LastWritten = default
                };

                if (!CredWrite(ref credential, 0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (commentPtr != IntPtr.Zero) Marshal.FreeHGlobal(commentPtr);
                if (targetNamePtr != IntPtr.Zero) Marshal.FreeHGlobal(targetNamePtr);
                if (credentialBlobPtr != IntPtr.Zero) Marshal.FreeHGlobal(credentialBlobPtr);
                if (userNamePtr != IntPtr.Zero) Marshal.FreeHGlobal(userNamePtr);
            }
        }

    }
}
