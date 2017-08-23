using Microsoft.SqlServer.XEvent.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core
{
    public abstract class Response
    {
        public Response()
        {

        }

        public string Filter { get; set; }
        public List<string> Events { get; set; } = new List<string>();

        public abstract void Process(PublishedEvent evt);

        // Returns whether the event is subscribed to this response or not
        public Boolean IsSubscribed(PublishedEvent evt)
        {
            return Events.Contains("*") || Events.Contains(evt.Name);
        }

    }
}
