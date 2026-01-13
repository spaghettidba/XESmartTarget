using NLog;
using System.Diagnostics;
using System.Runtime.Intrinsics.Arm;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core
{
    public class Target
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        public Target()
        {
        }
        // List of responses to process the incoming events
        // Does not do any work itself, but conntains the defitions of the responses
        // Which are cloned for each target worker.
        // This allows reading the definitions once and reusing them for multiple target connections
        public List<Response> Responses { get; set; } = new List<Response>();
        public string[] ServerName
        {
            get => ConnectionInfo.Select(s => s.ServerName).ToArray();
            set
            {
                ConnectionInfo.Clear();
                for(int i=0; i<value.Length; i++)
                    ConnectionInfo.Add(new SqlConnectionInfo { ServerName = value[i] });
            }
        }
        public string SessionName { get; set; }
        public string UserName
        {
            get => ConnectionInfo.FirstOrDefault()?.UserName;
            set { 
                    foreach (var conn in ConnectionInfo)
                        conn.UserName = value; 
                }
        }
        public string Password
        {
            get => ConnectionInfo.FirstOrDefault()?.Password;
            set
            {
                foreach (var conn in ConnectionInfo)
                    conn.Password = value;
            }
        }
        public string DatabaseName
        {
            get => ConnectionInfo.FirstOrDefault()?.DatabaseName;
            set
            {
                foreach (var conn in ConnectionInfo)
                    conn.DatabaseName = value;
            }
        }
        public int? ConnectTimeout
        {
            get => ConnectionInfo.FirstOrDefault()?.ConnectTimeout ?? 15;
            set
            {
                foreach (var conn in ConnectionInfo)
                    conn.ConnectTimeout = value;
            }
        }
        public bool TrustServerCertificate
        {
            get => ConnectionInfo.FirstOrDefault()?.TrustServerCertificate ?? true;
            set
            {
                foreach (var conn in ConnectionInfo)
                    conn.TrustServerCertificate = value;
            }
        }

        public string Authentication
        {
            get => ConnectionInfo.FirstOrDefault()?.Authentication;
            set
            {
                foreach (var conn in ConnectionInfo)
                    conn.Authentication = value;
            }
        }

        public List<SqlConnectionInfo> ConnectionInfo { get; set; } = new();
        public bool FailOnProcessingError { get; set; } = false;
        public string PreExecutionScript { get; set; }
        public string PostExecutionScript { get; set; }

        private bool stopped = false;
        private List<Task> allTasks = new List<Task>();

        public void Start()
        {
            try
            {
                foreach (var conn in ConnectionInfo)
                {
                    TargetWorker worker = new TargetWorker()
                    {
                        ConnectionInfo = conn,
                        SessionName = SessionName,
                        FailOnProcessingError = FailOnProcessingError,
                        PreExecutionScript = PreExecutionScript,
                        PostExecutionScript = PostExecutionScript,
                    };

                    foreach (Response r in Responses)
                    {
                        Response pr = (Response)r.Clone();
                        // Set tokens immediately after cloning, before any tasks start
                        pr.Tokens["ServerName"] = conn.ServerName;
                        // Generate unique ID for the response instance
                        // Makes it easier to track in logs and debugging
                        pr.Id = "R-" + pr.GetType().Name + "-" + conn.ServerName + "-" + Guid.NewGuid().ToString();
                        worker.Responses.Add(pr);
                    }

                    allTasks.Add(new Task(() => worker.Process()));
                }
                foreach (var t in allTasks)
                {
                    t.Start();
                }
                foreach (var t in allTasks)
                {
                    t.Wait();
                }
            }
            catch (Exception e)
            {
                logger.Error(e);
                throw;
            }
            logger.Info("Quitting");
        }
        public void Stop()
        {
            stopped = true;
        }
    }
}
