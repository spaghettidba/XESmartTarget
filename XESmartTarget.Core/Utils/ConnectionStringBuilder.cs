using Microsoft.Data.SqlClient;

namespace XESmartTarget.Core.Utils
{
    public class ConnectionStringBuilder
    {
        public string ServerName { get; set; }
        public string DatabaseName { get; set; } = "master";
        public string UserName { get; set; }
        public string Password { get; set; }
        public int ConnectionTimeout { get; set; } = 15;
        public bool TrustServerCertificate { get; set; } = true;

        public string Build()
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = ServerName,
                InitialCatalog = string.IsNullOrEmpty(DatabaseName) ? "master" : DatabaseName,
                ConnectTimeout = ConnectionTimeout,
                TrustServerCertificate = TrustServerCertificate
            };

            if (string.IsNullOrEmpty(UserName))
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = UserName;
                builder.Password = Password;
            }

            return builder.ConnectionString;
        }
    }
}