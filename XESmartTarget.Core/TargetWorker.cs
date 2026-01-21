using NLog;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.XEvent.XELite;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core
{
    
    public class TargetWorker
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
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
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        IF SERVERPROPERTY('EngineEdition') = 5 
                        BEGIN
                            SELECT 
                                @@SERVERNAME AS server_name, 
                                SERVERPROPERTY('EngineEdition') AS engine_edition, 
                                name 
                            FROM sys.dm_xe_database_sessions 
                            WHERE name = @sessionName
                        END
                        ELSE
                        BEGIN
                            SELECT 
                                @@SERVERNAME AS server_name, 
                                SERVERPROPERTY('EngineEdition') AS engine_edition, 
                                name 
                            FROM sys.dm_xe_sessions 
                            WHERE name = @sessionName
                        END
                        """;
                    cmd.Parameters.Add(new SqlParameter("@sessionName", SessionName));
                    string? serverName = null, sessionName = null;
                    int engineEdition;
                    using (var rdr = cmd.ExecuteReader())
                    if (rdr.Read())
                    {
                        serverName = rdr.GetString(0);
                        engineEdition = rdr.GetInt32(1);
                        sessionName = rdr.GetString(2);
                    }
                    if (sessionName == null)
                    {
                        throw new ArgumentException($"Connected to {ConnectionInfo.ServerName}. Session {SessionName} not found.");
                    }
                        
                    // we need to store the actual server name in the tokens
                    // actual server name might be different from the one used to connect
                    // which is particularly true in case of AG listeners
                    // Also Azure SQL Managed Instance and Azure SQL Database
                    // might return different server names
                    foreach (Response r in Responses)
                    {
                        if (!r.Tokens.ContainsKey("ActualServerName"))
                        {
                            r.Tokens.Add("ActualServerName", serverName);
                        }
                        else
                        {
                            r.Tokens["ActualServerName"] = serverName;
                        }
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

        public void Stop()
        {
            stopped = true;
            
            // Stop all responses
            foreach (var response in Responses)
            {
                try
                {
                    response.Stop();
                }
                catch (Exception e)
                {
                    logger.Error($"Error stopping response {response.Id}");
                    logger.Error(e);
                }
            }
        }
    }
    
}
