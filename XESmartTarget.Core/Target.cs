using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
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
        public string ServerName { get; set; }
        public string SessionName { get; set; }

        public void Start()
        {
            string connectionString = "Data Source=" + ServerName + "; Initial Catalog=master; Integrated Security = SSPI";

            logger.Info(String.Format("Connecting to XE session '{0}' on server '{1}'", SessionName,ServerName));

            QueryableXEventData eventStream = new QueryableXEventData(
                connectionString, 
                SessionName, 
                EventStreamSourceOptions.EventStream, 
                EventStreamCacheOptions.DoNotCache);

            logger.Info("Connected.");

            try
            {
                foreach (PublishedEvent xevent in eventStream)
                {
                    // Pass events to the responses
                    foreach (Response r in Responses)
                    {
                        // filter out unwanted events
                        // if no events are specified, will process all
                        if (r.Events.Count > 0)
                        {
                            if (!r.Events.Contains(xevent.Name))
                            {
                                continue;
                            }
                        }
                        r.Process(xevent);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e);
                throw;
            }

        }
    }
}
