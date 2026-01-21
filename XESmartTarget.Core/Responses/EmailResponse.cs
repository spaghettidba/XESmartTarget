using NLog;
using System.Data;
using XESmartTarget.Core.Utils;
using System.Net.Mail;
using Microsoft.SqlServer.XEvent.XELite;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class EmailResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public string SMTPServer { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Cc { get; set; } = string.Empty;
        public string Bcc { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool HTMLFormat { get; set; }
        public string Attachment { get; set; } = string.Empty;
        public string AttachmentFileName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        protected DataTable EventsTable = new DataTable("events");
        private XEventDataTableAdapter? xeadapter;

        public EmailResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public override void Process(IXEvent evt)
        {
            if (xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(EventsTable);
                xeadapter.Filter = this.Filter;
                xeadapter.OutputColumns = new List<OutputColumn>();
            }
            xeadapter.ReadEvent(evt);

            lock (EventsTable)
            {

                foreach (DataRow dr in EventsTable.Rows)
                {
                    string formattedBody = Body;
                    string formattedSubject = Subject;

                    Dictionary<string, object> eventTokens = new Dictionary<string, object>();
                    foreach (DataColumn dc in EventsTable.Columns)
                    {
                        eventTokens.Add(dc.ColumnName, dr[dc]);
                    }
                    // also add the Response tokens
                    foreach(string t in Tokens.Keys)
                    {
                        if(!eventTokens.ContainsKey(t))
                            eventTokens.Add(t, Tokens[t]);
                    }
                    formattedBody = SmartFormatHelper.Format(Body, eventTokens);
                    formattedSubject = SmartFormatHelper.Format(Subject, eventTokens);

                    using (MailMessage msg = new MailMessage() { From = new MailAddress(Sender), Subject = formattedSubject, Body = formattedBody })
                    {
                        foreach(var addrTo in To.Split(';'))
                        {
                            msg.To.Add(new MailAddress(addrTo));
                        }

                        using (MemoryStream attachStream = new MemoryStream())
                        {


                            if (!String.IsNullOrEmpty(Attachment) && dr.Table.Columns.Contains(Attachment))
                            {

                                StreamWriter wr = new StreamWriter(attachStream);
                                wr.Write(dr[Attachment].ToString());
                                wr.Flush();
                                attachStream.Position = 0;

                                System.Net.Mime.ContentType ct = new System.Net.Mime.ContentType(System.Net.Mime.MediaTypeNames.Text.Plain);
                                Attachment at = new Attachment(attachStream, ct);
                                if (at.ContentDisposition != null)
                                {
                                    at.ContentDisposition.FileName = AttachmentFileName;
                                }
                                msg.Attachments.Add(at);

                            }
                            msg.IsBodyHtml = HTMLFormat;

                            using (SmtpClient client = new SmtpClient(SMTPServer))
                            {
                                if (!String.IsNullOrEmpty(UserName))
                                {
                                    client.Credentials = new System.Net.NetworkCredential(UserName, Password);
                                }
                                // could be inefficient: sends synchronously
                                client.Send(msg);
                            }

                        }

                    }

                }

                EventsTable.Clear();

            }


        }

        public override object Clone()
        {
            EmailResponse clone = (EmailResponse)this.CloneBase();
            clone.EventsTable = new DataTable("events");
            clone.xeadapter = null;
            return clone;
        }

    }
}
