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

        private List<Task> allTasks = new List<Task>();
        private CancellationTokenSource cts;

        public void Start()
        {
            try
            {
                cts = new CancellationTokenSource();

                foreach (var conn in ConnectionInfo)
                {
                    TargetWorker worker = new TargetWorker()
                    {
                        ConnectionInfo = conn,
                        SessionName = SessionName,
                        FailOnProcessingError = FailOnProcessingError,
                        PreExecutionScript = PreExecutionScript,
                        PostExecutionScript = PostExecutionScript,
                        CancellationToken = cts.Token
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

                    allTasks.Add(Task.Run(() => worker.Process()));
                }
                Task.WaitAll(allTasks.ToArray());
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
            cts?.Cancel();
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

            internal CancellationToken CancellationToken { get; set; }

            private bool connectedOnce = false;

            internal async Task Process()
            {
                if (!String.IsNullOrEmpty(PreExecutionScript))
                {
                    logger.Info($"Running Pre-Execution script '{SessionName}' on server '{ConnectionInfo.ServerName}'");
                }

                logger.Info($"Connecting to XE session '{SessionName}' on server '{ConnectionInfo.ServerName}'");

                bool shouldContinue = true;
                int attempts = 0;

                while (shouldContinue && !CancellationToken.IsCancellationRequested)
                {
                    XELiveEventStreamer eventStream = null;
                    try
                    {
                        if (attempts < 240) attempts++; // connect attempts will be at least every 1 hour (240 * 15 sec = 3600 sec = 1 hour)

                        eventStream = ConnectSessionStream(ConnectionString);
                        connectedOnce = true;
                        attempts = 0;
                        logger.Info($"Connected to '{ConnectionInfo.ServerName}'.");
                    }
                    catch (Exception e)
                    {
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
                        await ProcessStreamDataAsync(eventStream, CancellationToken);
                    }
                    catch (Exception e)
                    {
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
                    finally
                    {
                        eventStream = null;
                    }
                }

                if (!String.IsNullOrEmpty(PostExecutionScript))
                {
                    logger.Info($"Running Post-Execution script '{SessionName}' on server '{ConnectionInfo.ServerName}'");
                }
            }

            private async Task ProcessStreamDataAsync(XELiveEventStreamer eventStream, CancellationToken parentToken)
            {
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
                {
                    var eventTask = eventStream.ReadEventStream(async xevent =>
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
                        await Task.CompletedTask;
                    },
                    linkedCts.Token);

                    try
                    {
                        await Task.WhenAny(eventTask, Task.Delay(Timeout.Infinite, linkedCts.Token));
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception e)
                    {
                        logger.Error(e);
                        throw;
                    }
                    if (eventTask.IsFaulted)
                    {
                        logger.Error("Failed with: {0}", eventTask.Exception);
                    }
                }
            }

            private XELiveEventStreamer ConnectSessionStream(string connectionString)
            {
                XELiveEventStreamer eventStream;
                try
                {
                    eventStream = new XELiveEventStreamer(connectionString, SessionName);
                }
                catch (Exception e)
                {
                    var ioe = new InvalidOperationException($"Unable to connect to the Extended Events session {SessionName} on server {ConnectionInfo.ServerName}", e);
                    throw ioe;
                }

                return eventStream;
            }
        }
    }
}
