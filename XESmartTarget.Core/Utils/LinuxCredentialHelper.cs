using NLog;
using System.Text.Json;

namespace XESmartTarget.Core.Utils
{
    public class LinuxCredentialHelper
    {
        private static readonly string CredentialsFilePath = Path.Combine("/tmp/QMonitor/", "cred.json");
        private static readonly object _locker = new object();
        private static Logger _logger = LogManager.GetCurrentClassLogger();        

        private static List<CredentialItem> LoadCredentialItems()
        {
            lock (_locker)
            {
                try
                {
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
                    _logger.Error("Error reading cred.json: " + ex.Message);
                    return new List<CredentialItem>();
                }
            }
        }
        
        public static (string username, string password) ReadCredential(string target)
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
                    return (item.UserName ?? "", item.Password ?? "");
                }
                else
                {
                    _logger.Warn($"No credential found for {target}!");
                    return ("", "");
                }
            }
        }

        private class CredentialItem
        {
            public string Target { get; set; }
            public string UserName { get; set; }
            public string Password { get; set; }
        }
    }
}
