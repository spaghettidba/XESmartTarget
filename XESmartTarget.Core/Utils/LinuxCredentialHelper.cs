using NLog;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XESmartTarget.Core.Utils
{
    public class LinuxCredentialHelper
    {
        private static readonly string CredentialsDirectory = Path.Combine(AppContext.BaseDirectory, "XECredentials");
        private static readonly string CredentialsFilePath = Path.Combine(CredentialsDirectory, "cred.json");
        private static readonly string EncryptedCredentialsFilePath = Path.Combine(CredentialsDirectory, "cred.enc");
        private const string MachineIdPath = "/etc/machine-id";

        private static readonly object _locker = new object();
        private static Logger _logger = LogManager.GetCurrentClassLogger();        

        private static List<CredentialItem> LoadCredentialItems()
        {
            lock (_locker)
            {
                try
                {
                    if (File.Exists(EncryptedCredentialsFilePath))
                        return LoadEncryptedCredentialItems();

                    if (!File.Exists(CredentialsFilePath))
                    {
                        return new List<CredentialItem>();
                    }

                    string json = File.ReadAllText(CredentialsFilePath);
                    var items = JsonSerializer.Deserialize<List<CredentialItem>>(json);
                    if (items == null)
                    {
                        return new List<CredentialItem>();
                    }

                    return items;
                }
                catch (Exception ex)
                {
                    _logger.Error("Error reading credentials: " + ex.Message);
                    return new List<CredentialItem>();
                }
            }
        }

        // Returns a 32-byte AES key derived from /etc/machine-id via SHA-256.
        private static byte[] GetMachineKey()
        {
            if (!File.Exists(MachineIdPath))
                throw new FileNotFoundException($"Cannot find {MachineIdPath}; encrypted credentials are not supported on this platform.");

            string machineId = File.ReadAllText(MachineIdPath).Trim();
            return SHA256.HashData(Encoding.UTF8.GetBytes(machineId));
        }

        // Reads and decrypts cred.enc using AES-256-CBC.
        // File layout: first 16 bytes = IV, remaining bytes = ciphertext (PKCS7-padded UTF-8 JSON).
        private static List<CredentialItem> LoadEncryptedCredentialItems()
        {
            byte[] fileBytes = File.ReadAllBytes(EncryptedCredentialsFilePath);

            if (fileBytes.Length < 32)
                throw new InvalidDataException("cred.enc is too short to contain a valid IV (16 bytes) and at least one ciphertext block (16 bytes).");

            byte[] iv = fileBytes[..16];
            byte[] ciphertext = fileBytes[16..];

            byte[] key = GetMachineKey();

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream(ciphertext);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            string json = sr.ReadToEnd();

            var items = JsonSerializer.Deserialize<List<CredentialItem>>(json);
            return items ?? new List<CredentialItem>();
        }
        
        public static (string username, string password, string authScheme) ReadCredential(string target)
        {
            lock (_locker)
            {
                var items = LoadCredentialItems();
                var item = items.FirstOrDefault(x =>
                    x.Target != null &&
                    x.Target.Equals(target, StringComparison.OrdinalIgnoreCase)
                );

                if (item != null)
                {
                    return (item.UserName ?? "", item.Password ?? "", item.AuthScheme ?? "Basic");
                }
                else
                {
                    _logger.Warn($"No credential found for {target}!");
                    return ("", "", "Basic");
                }
            }
        }

        private class CredentialItem
        {
            public string Target { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string? AuthScheme { get; set; }
        }
    }
}
