using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XESmartTarget.Core.Utils;
using Xunit;
using Assert = Xunit.Assert;

namespace XESmartTarget.Tests.Utils
{
    /// <summary>
    /// Tests for LinuxCredentialHelper encrypted-storage (cred.enc) path.
    /// The encrypted file format is: first 16 bytes = AES IV, remaining bytes = AES-256-CBC
    /// ciphertext (PKCS7) of UTF-8 JSON.  The key is SHA-256 of the trimmed /etc/machine-id.
    /// </summary>
    public class LinuxCredentialHelperTests : IDisposable
    {
        // Mirror the same directory the helper uses at runtime.
        private static readonly string CredDir =
            Path.Combine(AppContext.BaseDirectory, "XECredentials");

        private static readonly string CredEncPath = Path.Combine(CredDir, "cred.enc");
        private static readonly string CredJsonPath = Path.Combine(CredDir, "cred.json");

        public LinuxCredentialHelperTests()
        {
            Directory.CreateDirectory(CredDir);
            // Start each test with a clean slate.
            DeleteIfExists(CredEncPath);
            DeleteIfExists(CredJsonPath);
        }

        public void Dispose()
        {
            DeleteIfExists(CredEncPath);
            DeleteIfExists(CredJsonPath);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static byte[] GetMachineKey()
        {
            string machineId = File.ReadAllText("/etc/machine-id").Trim();
            return SHA256.HashData(Encoding.UTF8.GetBytes(machineId));
        }

        private static void WriteEncryptedCreds(object credentials)
        {
            string json = JsonSerializer.Serialize(credentials);
            byte[] plaintext = Encoding.UTF8.GetBytes(json);

            byte[] key = GetMachineKey();

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            byte[] ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

            using var fs = new FileStream(CredEncPath, FileMode.Create, FileAccess.Write);
            fs.Write(aes.IV, 0, aes.IV.Length);
            fs.Write(ciphertext, 0, ciphertext.Length);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // -----------------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------------

        [Fact]
        public void ReadCredential_EncryptedFile_ReturnsCorrectBasicCredentials()
        {
            WriteEncryptedCreds(new[]
            {
                new { Target = "https://example.com/api", UserName = "alice", Password = "s3cr3t", AuthScheme = (string?)null }
            });

            var (username, password, authScheme) = LinuxCredentialHelper.ReadCredential("https://example.com/api");

            Assert.Equal("alice", username);
            Assert.Equal("s3cr3t", password);
            Assert.Equal("Basic", authScheme);   // null AuthScheme should default to "Basic"
        }

        [Fact]
        public void ReadCredential_EncryptedFile_ReturnsApiKeyScheme()
        {
            WriteEncryptedCreds(new[]
            {
                new { Target = "https://example.com/api", UserName = "myuser", Password = "apikey123", AuthScheme = "ApiKey" }
            });

            var (username, password, authScheme) = LinuxCredentialHelper.ReadCredential("https://example.com/api");

            Assert.Equal("myuser", username);
            Assert.Equal("apikey123", password);
            Assert.Equal("ApiKey", authScheme);
        }

        [Fact]
        public void ReadCredential_EncryptedFile_TargetMatchingIsCaseInsensitive()
        {
            WriteEncryptedCreds(new[]
            {
                new { Target = "HTTPS://EXAMPLE.COM/API", UserName = "bob", Password = "pass", AuthScheme = (string?)null }
            });

            var (username, _, _) = LinuxCredentialHelper.ReadCredential("https://example.com/api");

            Assert.Equal("bob", username);
        }

        [Fact]
        public void ReadCredential_EncryptedFile_UnknownTargetReturnsEmpty()
        {
            WriteEncryptedCreds(new[]
            {
                new { Target = "https://example.com/api", UserName = "alice", Password = "s3cr3t", AuthScheme = (string?)null }
            });

            var (username, password, authScheme) = LinuxCredentialHelper.ReadCredential("https://other.com/api");

            Assert.Equal("", username);
            Assert.Equal("", password);
            Assert.Equal("Basic", authScheme);
        }

        [Fact]
        public void ReadCredential_EncryptedFilePreferredOverPlainJson()
        {
            // Write a cred.json that would return "wronguser" if read.
            var jsonItems = new[]
            {
                new { Target = "https://example.com/api", UserName = "wronguser", Password = "wrong", AuthScheme = (string?)null }
            };
            File.WriteAllText(CredJsonPath, JsonSerializer.Serialize(jsonItems));

            // Write a cred.enc with the correct user.
            WriteEncryptedCreds(new[]
            {
                new { Target = "https://example.com/api", UserName = "rightuser", Password = "right", AuthScheme = (string?)null }
            });

            var (username, _, _) = LinuxCredentialHelper.ReadCredential("https://example.com/api");

            Assert.Equal("rightuser", username);
        }

        [Fact]
        public void ReadCredential_PlainJsonUsedWhenNoEncryptedFile()
        {
            var jsonItems = new[]
            {
                new { Target = "https://example.com/api", UserName = "jsonuser", Password = "jsonpass", AuthScheme = "Basic" }
            };
            File.WriteAllText(CredJsonPath, JsonSerializer.Serialize(jsonItems));

            var (username, password, authScheme) = LinuxCredentialHelper.ReadCredential("https://example.com/api");

            Assert.Equal("jsonuser", username);
            Assert.Equal("jsonpass", password);
            Assert.Equal("Basic", authScheme);
        }

        [Fact]
        public void ReadCredential_NeitherFileExists_ReturnsEmpty()
        {
            var (username, password, authScheme) = LinuxCredentialHelper.ReadCredential("https://example.com/api");

            Assert.Equal("", username);
            Assert.Equal("", password);
            Assert.Equal("Basic", authScheme);
        }
    }
}
