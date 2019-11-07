using Microsoft.SqlServer.XEvent.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core
{
    public abstract class Response : ICloneable
    {
        public Response()
        {

        }

        public string Filter { get; set; }
        public List<string> Events { get; set; } = new List<string>();

        public Dictionary<string, string> Tokens = new Dictionary<string, string>();

        public abstract void Process(PublishedEvent evt);

        // Returns whether the event is subscribed to this response or not
        public Boolean IsSubscribed(PublishedEvent evt)
        {
            return Events.Count == 0 || Events.Contains("*") || Events.Contains(evt.Name);
        }

        public object Clone()
        {
            Response res = (Response)this.MemberwiseClone();
            res.Tokens = new Dictionary<string, string>();
            foreach(string s in Tokens.Keys)
            {
                res.Tokens.Add(s, Tokens[s]);
            }
            return res;
        }
    }
}
