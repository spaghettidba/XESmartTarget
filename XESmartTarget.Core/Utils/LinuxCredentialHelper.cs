using NLog;
using System.Text.Json;

namespace XESmartTarget.Core.Utils
{
    public class LinuxCredentialHelper
    {
        private static readonly string CredentialsFilePath = Path.Combine("/tmp/QMonitor/", "cred.json");
        private static readonly object _locker = new object();
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        private static Dictionary<string, (string userName, string password)> LoadCredentials()
        {
            try
            {
                _logger.Debug("Reading credentials from: " + CredentialsFilePath);
                if (!File.Exists(CredentialsFilePath))
                {
                    _logger.Debug("File not found!");
                    return new Dictionary<string, (string, string)>();
                }

                string json = File.ReadAllText(CredentialsFilePath);
                var tempDict = JsonSerializer.Deserialize<Dictionary<string, StoredCredential>>(json);
                if (tempDict == null)
                {
                    _logger.Debug("Can't read!");
                    return new Dictionary<string, (string, string)>();
                }

                var dict = new Dictionary<string, (string, string)>();
                foreach (var kvp in tempDict)
                {
                    dict[kvp.Key] = (kvp.Value.UserName ?? "", kvp.Value.Password ?? "");
                }
                return dict;
            }
            catch (Exception ex)
            {
                _logger.Debug("Error " + ex.Message);
                return new Dictionary<string, (string, string)>();
            }
        }

        public static (string username, string password) ReadCredential(string target)
        {
            lock (_locker)
            {
                var allCreds = LoadCredentials();
                if (allCreds.ContainsKey(target))
                {
                    var (usr, pwd) = allCreds[target];
                    _logger.Debug($"Cred {target}: {usr} {pwd}");
                    return (usr, pwd);
                }
                _logger.Debug($"No credential {target}!");
                return ("", "");
            }
        }

        private class StoredCredential //TODO rendere più sicura
        {
            public string UserName { get; set; }
            public string Password { get; set; }
        }
    }
}
