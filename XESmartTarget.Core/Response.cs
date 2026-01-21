using Microsoft.SqlServer.XEvent.XELite;

namespace XESmartTarget.Core
{
    public abstract class Response : ICloneable
    {
        public Response()
        {

        }

        internal string Id { get; set; } = string.Empty;

        public string? Filter { get; set; }
        public List<string> Events { get; set; } = new List<string>();

        public Dictionary<string, string> Tokens = new Dictionary<string, string>();

        public abstract void Process(IXEvent evt);

        // Returns whether the event is subscribed to this response or not
        public Boolean IsSubscribed(IXEvent evt)
        {
            return Events.Count == 0 || Events.Contains("*") || Events.Contains(evt.Name);
        }

        // Make Clone abstract to force all subclasses to implement proper deep copying
        public abstract object Clone();

        // Helper method for subclasses to call for base cloning
        protected Response CloneBase()
        {
            var clone = (Response)this.MemberwiseClone();
            // Deep copy the Tokens dictionary to avoid sharing references
            clone.Tokens = new Dictionary<string, string>(this.Tokens);
            clone.Events = new List<string>(this.Events);
            return clone;
        }

        // Virtual method to allow responses to clean up background tasks
        public virtual void Stop()
        {
            // Default implementation does nothing
            // Subclasses with background tasks should override this
        }
    }
}
