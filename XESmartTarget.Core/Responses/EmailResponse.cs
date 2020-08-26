using System;
using System.Collections.Generic;
using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System.Data;
using XESmartTarget.Core.Utils;
using System.Net.Mail;
using System.IO;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class EmailResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public string SMTPServer { get; set; }
        public string Sender { get; set; }
        public string To { get; set; }
        public string Cc { get; set; }
        public string Bcc { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public bool HTMLFormat { get; set; }
        public string Attachment { get; set; }
        public string AttachmentFileName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }

        protected DataTable EventsTable = new DataTable("events");
        private XEventDataTableAdapter xeadapter;

        public EmailResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public override void Process(PublishedEvent evt)
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

                    using (MailMessage msg = new MailMessage(Sender, To, formattedSubject, formattedBody))
                    {
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
                                at.ContentDisposition.FileName = AttachmentFileName;
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

    }
}
