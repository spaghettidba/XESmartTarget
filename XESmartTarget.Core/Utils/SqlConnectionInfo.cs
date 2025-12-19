using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;

namespace XESmartTarget.Core.Utils
{
    public class SqlConnectionInfo
    {
        [Required]
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public int? Port { get; set; }
        public bool UseIntegratedSecurity { get; set; }
        public bool Encrypt { get; set; }
        public bool TrustServerCertificate { get; set; } = true;
        public string ApplicationName { get; set; }
        public bool UserInstance { get; set; }
        public int LoadBalanceTimeout { get; set; }
        public int MaxPoolSize { get; set; } = 100;
        public int MinPoolSize { get; set; }
        public bool MultipleActiveResultSets { get; set; }
        public bool MultiSubnetFailover { get; set; }
        public int PacketSize { get; set; } = 8000;
        public bool Replication { get; set; }
        public string TransactionBinding { get; set; } = "Implicit Unbind";
        public string TypeSystemVersion { get; set; }
        public string UserID { get => UserName; set => UserName = value; }
        public bool PersistSecurityInfo { get; set; }
        public bool Pooling { get; set; } = true;
        public bool IntegratedSecurity { get => UseIntegratedSecurity; set => UseIntegratedSecurity = value; }
        public string InitialCatalog { get => DatabaseName; set => DatabaseName = value; }
        public string ApplicationIntent { get; set; } = "ReadWrite";
        public string WorkstationID { get; set; }
        public int ConnectRetryCount { get; set; } = 1;
        public int ConnectRetryInterval { get; set; } = 10;
        public string AttachDBFilename { get; set; }
        public string CurrentLanguage { get; set; }
        public string DataSource { get => ServerName; set => ServerName = value; }
        public bool Enlist { get; set; } = true;
        public string FailoverPartner { get; set; }
        public int? ConnectTimeout { get; set; } = 15;
        public string PoolBlockingPeriod { get; set; } = "Auto";
        private SqlAuthenticationMethod _authMethod = SqlAuthenticationMethod.NotSpecified;

        public string Authentication
        {
            get => _authMethod.ToString();
            set
            {
                if (Enum.TryParse<SqlAuthenticationMethod>(value, true, out var result))
                {
                    _authMethod = result;
                }
                else
                {
                    throw new ArgumentException(
                        $"Invalid authentication method:  {value}. " +
                        $"Valid values are: {string.Join(", ", Enum.GetNames(typeof(SqlAuthenticationMethod)))}");
                }
            }
        }

        // strongly-typed property for direct access to the enum
        internal SqlAuthenticationMethod AuthenticationMethod
        {
            get => _authMethod;
            set => _authMethod = value;
        }




        public static SqlConnectionInfo Parse(string connectionString)
        {
            SqlConnectionInfo info;
            try
            {
                info = new SqlConnectionInfo(connectionString);
            }
            catch
            {
                info = new SqlConnectionInfo() { ServerName = connectionString };
            }
            return info;
        }

        public SqlConnectionInfo() { }

        public SqlConnectionInfo(SqlConnectionInfo info)
        {
            ServerName = info.ServerName;
            Port = info.Port;
            DatabaseName = info.InitialCatalog;
            UseIntegratedSecurity = info.IntegratedSecurity;
            UserName = info.UserID;
            Password = info.Password;
            Encrypt = info.Encrypt;
            TrustServerCertificate = info.TrustServerCertificate;
            ApplicationName = info.ApplicationName;
            UserInstance = info.UserInstance;
            LoadBalanceTimeout = info.LoadBalanceTimeout;
            MinPoolSize = info.MinPoolSize;
            MaxPoolSize = info.MaxPoolSize;
            MultipleActiveResultSets = info.MultipleActiveResultSets;
            MultiSubnetFailover = info.MultiSubnetFailover;
            PacketSize = info.PacketSize;
            Replication = info.Replication;
            TransactionBinding = info.TransactionBinding;
            PersistSecurityInfo = info.PersistSecurityInfo;
            Pooling = info.Pooling;
            ApplicationIntent = info.ApplicationIntent;
            WorkstationID = info.WorkstationID;
            ConnectRetryCount = info.ConnectRetryCount;
            ConnectRetryInterval = info.ConnectRetryInterval;
            AttachDBFilename = info.AttachDBFilename;
            CurrentLanguage = info.CurrentLanguage;
            Enlist = info.Enlist;
            FailoverPartner = info.FailoverPartner;
            ConnectTimeout = info.ConnectTimeout;
            PoolBlockingPeriod = info.PoolBlockingPeriod;
            AuthenticationMethod = info.AuthenticationMethod;
        }


        public SqlConnectionInfo(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);

            var ds = builder.DataSource.Split(',');
            ServerName = ds[0];

            //se servername contiene un prefisso tra questi bisogna rimuoverlo
            var prefixes = new Dictionary<string, int>
                {
                    { "tcp:", 4 },
                    { "np:", 3 },
                    { "admin:", 6 },
                    { "lpc:", 4 }
                };

            foreach (var prefix in prefixes)
            {
                if (ServerName.StartsWith(prefix.Key, StringComparison.OrdinalIgnoreCase))
                {
                    ServerName = ServerName.Substring(prefix.Value);
                    break;
                }
            }

            if (ds.Length > 1)
            {
                int.TryParse(ds[ds.Length - 1], out int port);
                Port = port;
            }

            DatabaseName = builder.InitialCatalog;
            UseIntegratedSecurity = builder.IntegratedSecurity;
            UserName = builder.UserID;
            Password = builder.Password;
            Encrypt = builder.Encrypt;
            TrustServerCertificate = builder.TrustServerCertificate;
            ApplicationName = builder.ApplicationName;
            UserInstance = builder.UserInstance;
            LoadBalanceTimeout = builder.LoadBalanceTimeout;
            MinPoolSize = builder.MinPoolSize;
            MaxPoolSize = builder.MaxPoolSize;
            MultipleActiveResultSets = builder.MultipleActiveResultSets;
            MultiSubnetFailover = builder.MultiSubnetFailover;
            PacketSize = builder.PacketSize;
            Replication = builder.Replication;
            TransactionBinding = builder.TransactionBinding;
            PersistSecurityInfo = builder.PersistSecurityInfo;
            Pooling = builder.Pooling;
            ApplicationIntent = builder.ApplicationIntent.ToString();
            WorkstationID = builder.WorkstationID;
            ConnectRetryCount = builder.ConnectRetryCount;
            ConnectRetryInterval = builder.ConnectRetryInterval;
            AttachDBFilename = builder.AttachDBFilename;
            CurrentLanguage = builder.CurrentLanguage;
            Enlist = builder.Enlist;
            FailoverPartner = builder.FailoverPartner;
            ConnectTimeout = builder.ConnectTimeout;
            PoolBlockingPeriod = builder.PoolBlockingPeriod.ToString();
            AuthenticationMethod = builder.Authentication;
        }


        public virtual string ConnectionString
        {
            get
            {
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

                //se servername contiene un prefisso tra questi bisogna rimuoverlo
                var prefixes = new Dictionary<string, int>
                {
                    { "tcp:", 4 },
                    { "np:", 3 },
                    { "admin:", 6 },
                    { "lpc:", 4 }
                };

                foreach (var prefix in prefixes)
                {
                    if (ServerName.StartsWith(prefix.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        ServerName = ServerName.Substring(prefix.Value);
                        break;
                    }
                }

                builder.DataSource = $"{ServerName}" + (Port != null ? $",{Port}" : "");

                if (!string.IsNullOrEmpty(InitialCatalog)) builder.InitialCatalog = DatabaseName;
                if (!string.IsNullOrEmpty(UserID)) builder.UserID = UserName;
                if (!string.IsNullOrEmpty(Password)) builder.Password = Password;
                if (Encrypt) builder.Encrypt = Encrypt;
                if (TrustServerCertificate) builder.TrustServerCertificate = TrustServerCertificate;
                if (!string.IsNullOrEmpty(ApplicationName)) builder.ApplicationName = ApplicationName;
                if (UserInstance) builder.UserInstance = UserInstance;
                if (LoadBalanceTimeout != 0) builder.LoadBalanceTimeout = LoadBalanceTimeout;
                if (MinPoolSize != 0) builder.MinPoolSize = MinPoolSize;
                if (MaxPoolSize != 100) builder.MaxPoolSize = MaxPoolSize;
                if (MultipleActiveResultSets) builder.MultipleActiveResultSets = MultipleActiveResultSets;
                if (MultiSubnetFailover) builder.MultiSubnetFailover = MultiSubnetFailover;
                if (PacketSize != 8000) builder.PacketSize = PacketSize;
                if (Replication) builder.Replication = Replication;
                if (TransactionBinding != "Implicit Unbind") builder.TransactionBinding = TransactionBinding;
                if (PersistSecurityInfo) builder.PersistSecurityInfo = PersistSecurityInfo;
                if (!Pooling) builder.Pooling = Pooling;
                Enum.TryParse(ApplicationIntent, true, out ApplicationIntent intent);
                if (ApplicationIntent != "ReadWrite") builder.ApplicationIntent = intent;
                if (!string.IsNullOrEmpty(WorkstationID)) builder.WorkstationID = WorkstationID;
                if (ConnectRetryCount != 1) builder.ConnectRetryCount = ConnectRetryCount;
                if (ConnectRetryInterval != 10) builder.ConnectRetryInterval = ConnectRetryInterval;
                if (!string.IsNullOrEmpty(AttachDBFilename)) builder.AttachDBFilename = AttachDBFilename;
                if (!string.IsNullOrEmpty(CurrentLanguage)) builder.CurrentLanguage = CurrentLanguage;
                if (!Enlist) builder.Enlist = Enlist;
                if (!string.IsNullOrEmpty(FailoverPartner)) builder.FailoverPartner = FailoverPartner;
                builder.ConnectTimeout = (ConnectTimeout ?? 0);
                Enum.TryParse(PoolBlockingPeriod, true, out PoolBlockingPeriod period);
                if (PoolBlockingPeriod != "Auto") builder.PoolBlockingPeriod = period;

                if (string.IsNullOrEmpty(builder.UserID) && string.IsNullOrEmpty(builder.Password))
                    builder.IntegratedSecurity = true;
                builder.Authentication = AuthenticationMethod;

                return builder.ConnectionString;
            }
        }

        public static SqlConnectionInfo Empty()
        {
            return new SqlConnectionInfo()
            {
                ServerName = Guid.Empty.ToString(),
                DatabaseName = Guid.Empty.ToString(),
                UserName = Guid.Empty.ToString(),
                Password = Guid.Empty.ToString()
            };
        }
    }
}