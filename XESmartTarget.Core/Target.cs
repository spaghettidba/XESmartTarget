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
        public string[] ServerName { get; set; }
        public string SessionName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string DatabaseName { get; set; }
        public bool FailOnProcessingError { get; set; } = false;
        public string PreExecutionScript { get; set; }
        public string PostExecutionScript { get; set; }

        private bool stopped = false;
        private List<Task> allTasks = new List<Task>();

        public void Start()
        {
            try
            {
                foreach (var currentServer in ServerName)
                {
                    TargetWorker worker = new TargetWorker()
                    {
                        ServerName = currentServer,
                        SessionName = SessionName,
                        UserName = UserName,
                        Password = Password,
                        DatabaseName = DatabaseName,
                        FailOnProcessingError = FailOnProcessingError,
                        PreExecutionScript = PreExecutionScript,
                        PostExecutionScript = PostExecutionScript
                    };

                    foreach (Response r in Responses)
                    {
                        Response pr = (Response)r.Clone();
                        worker.Responses.Add(pr);
                    }

                    foreach (Response r in worker.Responses)
                    {
                        Debug.Print(ServerName + " " + r.GetType().Name + " " + r.Tokens.Count);
                        r.Tokens.Add("ServerName", worker.ServerName);
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
            internal string ServerName { get; set; }
            internal string SessionName { get; set; }
            internal string UserName { get; set; }
            internal string Password { get; set; }
            internal string DatabaseName { get; set; }

            internal string PreExecutionScript { get; set; }
            internal string PostExecutionScript { get; set; }

            public string ConnectionString
            {
                get
                {
                    var csBuilder = new ConnectionStringBuilder
                    {
                        ServerName = ServerName,
                        DatabaseName = DatabaseName,
                        UserName = UserName,
                        Password = Password,
                        ConnectionTimeout = 15,
                        TrustServerCertificate = true
                    };

                    return csBuilder.Build();
                }
            }

            internal bool FailOnProcessingError { get; set; } = false;
            private bool stopped = false;

            internal async void Process()
            {
                if (!String.IsNullOrEmpty(PreExecutionScript))
                {
                    logger.Info($"Running Pre-Execution script '{SessionName}' on server '{ServerName}'");
                }

                logger.Info($"Connecting to XE session '{SessionName}' on server '{ServerName}'");

                bool connectedOnce = false;
                bool shouldContinue = true;
                int attempts = 0;

                XELiveEventStreamer eventStream = null;

                while (shouldContinue)
                {
                    try
                    {
                        if (attempts < 240) attempts++; // connect attempts will be at least every 1 hour (240 * 15 sec = 3600 sec = 1 hour)

                        eventStream = ConnectSessionStream(ConnectionString);
                        connectedOnce = true;
                        attempts = 0;
                        logger.Info($"Connected to '{ServerName}'.");
                    }
                    catch (Exception e)
                    {
                        if (attempts == 1)
                        {
                            logger.Error($"Error connecting to '{ServerName}'");
                            logger.Error(e.InnerException ?? e);
                        }
                        else
                        {
                            string msg = e.Message;
                            if (e.InnerException != null)
                            {
                                msg = e.InnerException.Message;
                            }
                            logger.Error($"Error connecting to '{ServerName}', attempt {attempts}");
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
                        logger.Error($"Error processing event data from '{ServerName}'");
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
                                logger.Error(e, ServerName);
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
                    logger.Error("Failed with: {0}", eventTask.Exception);
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
                    var ioe = new InvalidOperationException($"Unable to connect to the Extended Events session {SessionName} on server {ServerName}", e);
                    throw ioe;
                }

                return eventStream;
            }
        }
    }
}
