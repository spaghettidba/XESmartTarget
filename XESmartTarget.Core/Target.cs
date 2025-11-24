using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.XEvent.XELite;
using NLog;
using System.Diagnostics;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core
{
    public class Target
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        public Target()
        {
        }
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
                        worker.Responses.Add(pr);
                    }

                    foreach (Response r in worker.Responses)
                    {
                        Debug.Print(ServerName + " " + r.GetType().Name + " " + r.Tokens.Count);
                        r.Tokens.Add("ServerName", conn.ServerName);
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

        private class TargetWorker
        {
            internal List<Response> Responses { get; set; } = new List<Response>();
            internal string SessionName { get; set; }            
            internal string ConnectionString => ConnectionInfo.ConnectionString;
            internal SqlConnectionInfo ConnectionInfo { get; set; } = new();
            internal string PreExecutionScript { get; set; }
            internal string PostExecutionScript { get; set; }           

            internal bool FailOnProcessingError { get; set; } = false;
            private bool stopped = false;

            internal async void Process()
            {
                if (!String.IsNullOrEmpty(PreExecutionScript))
                {
                    logger.Info($"Running Pre-Execution script '{SessionName}' on server '{ConnectionInfo.ServerName}'");
                }

                logger.Info($"Connecting to XE session '{SessionName}' on server '{ConnectionInfo.ServerName}'");

                bool connectedOnce = false;
                bool shouldContinue = true;
                int attempts = 0;

                XELiveEventStreamer? eventStream = null;

                while (shouldContinue)
                {
                    try
                    {
                        if (attempts < 240) attempts++; // connect attempts will be at least every 1 hour (240 * 15 sec = 3600 sec = 1 hour)

                        eventStream = ConnectSessionStream(ConnectionString);
                        connectedOnce = true;
                        attempts = 0;
                    }
                    catch (Exception e)
                    {
                        eventStream = null;
                        if (attempts == 1)
                        {
                            logger.Error($"Error connecting to '{ConnectionInfo.ServerName}'");
                            logger.Error(e.InnerException ?? e);
                        }
                        else
                        {
                            string msg = e.Message;
                            if (e.InnerException != null)
                            {
                                msg = e.InnerException.Message;
                            }
                            logger.Error($"Error connecting to '{ConnectionInfo.ServerName}', attempt {attempts}");
                            logger.Error(msg);
                        }

                        if (FailOnProcessingError)
                        {
                            throw;
                        }
                        else
                        {
                            Thread.Sleep(attempts * 15000); // linear reconnect backoff
                            continue;
                        }
                    }
                    try
                    {
                        ProcessStreamData(eventStream);
                    }
                    catch (Exception e)
                    {
                        eventStream = null;
                        logger.Error($"Error processing event data from '{ConnectionInfo.ServerName}'");
                        logger.Error(e);
                        if (FailOnProcessingError)
                        {
                            throw;
                        }
                        else
                        {
                            shouldContinue = connectedOnce;
                        }
                    }
                }
            }

            private void ProcessStreamData(XELiveEventStreamer eventStream)
            {
                var cancellationTokenSource = new CancellationTokenSource();

                Task waitTask = Task.Run(() =>
                {
                    while (!stopped)
                    {
                        Thread.Sleep(1000);
                    }
                    cancellationTokenSource.Cancel();                    
                });

                Task eventTask = eventStream.ReadEventStream(xevent =>
                {
                    // Pass events to the responses
                    foreach (Response r in Responses)
                    {
                        // filter out unwanted events
                        // if no events are specified, will process all
                        if (r.Events.Count > 0)
                        {
                            if (!r.Events.Contains(xevent.Name, StringComparer.CurrentCultureIgnoreCase))
                            {
                                continue;
                            }
                        }
                        try
                        {
                            r.Process(xevent);
                        }
                        catch (Exception e)
                        {
                            if (FailOnProcessingError)
                            {
                                throw;
                            }
                            else
                            {
                                logger.Error(e.Message);
                                logger.Error(e.StackTrace);
                                logger.Error(e, ConnectionInfo.ServerName);
                            }
                        }
                    }
                    return Task.CompletedTask;
                },
                cancellationTokenSource.Token);

                try
                {
                    Task.WaitAny(waitTask, eventTask);
                
                }
                catch (Exception e)
                {
                    logger.Error(e);
                    throw;
                }
                if (eventTask.IsFaulted)
                {
                    throw eventTask.Exception;
                }
            }

            private XELiveEventStreamer ConnectSessionStream(string connectionString)
            {
                XELiveEventStreamer? eventStream;
                try
                {
                    using (var conn = new SqlConnection(connectionString))
                    {
                        conn.Open();
                        var cmd = conn.CreateCommand();
                        cmd.CommandText = @"IF SERVERPROPERTY('EngineEdition') = 5 
                                            BEGIN
	                                            SELECT name FROM sys.dm_xe_database_sessions WHERE name = @sessionName
                                            END
                                            ELSE
                                            BEGIN
	                                            SELECT name FROM sys.dm_xe_sessions WHERE name = @sessionName
                                            END ";
                        cmd.Parameters.Add(new SqlParameter("@sessionName", SessionName));
                        var name = cmd.ExecuteScalar();
                        if(name == null)
                        {
                            throw new ArgumentException($"Connected to {ConnectionInfo.ServerName}. Session {SessionName} not found.");
                        }
                        conn.Close();
                        logger.Info($"Connected to session {SessionName} on {ConnectionInfo.ServerName}.");
                    }
                    eventStream = new XELiveEventStreamer(connectionString, SessionName);
                }
                catch (Exception e)
                {
                    eventStream = null;
                    var ioe = new InvalidOperationException($"Unable to connect to the Extended Events session {SessionName} on server {ConnectionInfo.ServerName}", e);
                    throw ioe;
                }

                return eventStream;
            }
        }
    }
}
