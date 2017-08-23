using Microsoft.SqlServer.XEvent.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core
{
    public class Target
    {
        public Target()
        {

        }

        public List<Response> Responses { get; set; } = new List<Response>();
        public string ServerName { get; set; }
        public string SessionName { get; set; }

        public void Start()
        {
            string connectionString = "Data Source=" + ServerName + "; Initial Catalog=master; Integrated Security = SSPI";

            QueryableXEventData eventStream = new QueryableXEventData(
                connectionString, 
                SessionName, 
                EventStreamSourceOptions.EventStream, 
                EventStreamCacheOptions.DoNotCache); 

            foreach(PublishedEvent xevent in eventStream)
            {
                // Pass events to the responses
                foreach (Response r in Responses)
                {
                    r.Process(xevent);
                }
            }

        }
    }
}
