using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                        FailOnProcessingError = FailOnProcessingError
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
            internal bool FailOnProcessingError { get; set; } = false;
            private bool stopped = false;


            internal void Process()
            {

                string connectionString = "Data Source=" + ServerName + ";";
                if (String.IsNullOrEmpty(DatabaseName))
                {
                    connectionString += "Initial Catalog = master; ";
                }
                else
                {
                    connectionString += "Initial Catalog = " + DatabaseName + "; ";
                }
                if (String.IsNullOrEmpty(UserName))
                {
                    connectionString += "Integrated Security = SSPI; ";
                }
                else
                {
                    connectionString += "User Id = " + UserName + "; ";
                    connectionString += "Password = " + Password + "; ";
                }

                logger.Info(String.Format("Connecting to XE session '{0}' on server '{1}'", SessionName, ServerName));

                QueryableXEventData eventStream = null;
                try
                {
                    eventStream = new QueryableXEventData(
                        connectionString,
                        SessionName,
                        EventStreamSourceOptions.EventStream,
                        EventStreamCacheOptions.DoNotCache);
                }
                catch (Exception e)
                {
                    var ioe = new InvalidOperationException(String.Format("Unable to connect to the Extended Events session {0} on server {1}", SessionName, ServerName), e);
                    logger.Error(ioe);
                    throw ioe;
                }

                logger.Info($"Connected to {ServerName}.");


                try
                {
                    foreach (PublishedEvent xevent in eventStream)
                    {
                        if (stopped)
                        {
                            break;
                        }
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
                                    logger.Error(e, ServerName);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e, ServerName);
                    throw;
                }
            }

        }
        
    }
}
