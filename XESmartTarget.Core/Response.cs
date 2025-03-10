using Microsoft.SqlServer.XEvent.XELite;

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

        public abstract void Process(IXEvent evt);

        // Returns whether the event is subscribed to this response or not
        public Boolean IsSubscribed(IXEvent evt)
        {
            return Events.Count == 0 || Events.Contains("*") || Events.Contains(evt.Name);
        }

        public object Clone()
        {
            Response res = (Response)this.MemberwiseClone();
            res.Tokens = new Dictionary<string, string>();
            foreach (string s in Tokens.Keys)
            {
                res.Tokens.Add(s, Tokens[s]);
            }
            return res;
        }
    }
}
