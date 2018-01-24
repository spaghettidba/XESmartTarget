using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    public class CsvAppenderResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        
        public CsvAppenderResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public string OutputFile { get; set; }

        public List<string> OutputColumns { get; set; } = new List<string>(); 
        protected DataTable EventsTable { get => eventsTable; set => eventsTable = value; }
        private DataTable eventsTable = new DataTable("events");
        private XEventDataTableAdapter xeadapter;

        private bool headersWritten = false;

        public override void Process(PublishedEvent evt)
        {
            Enqueue(evt);
        }


        protected void Enqueue(PublishedEvent evt)
        {
            if (xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(eventsTable);
                xeadapter.Filter = this.Filter;
                xeadapter.OutputColumns = this.OutputColumns;
            }
            xeadapter.ReadEvent(evt);

            WriteToFile();

        }

        private void WriteToFile()
        {
            lock (EventsTable)
            {
                DataTableCSVAdapter adapter = new DataTableCSVAdapter(EventsTable)
                {
                    OutputFile = this.OutputFile
                };
                if (!headersWritten)
                {
                    adapter.WriteHeaders();
                    headersWritten = true;
                }
                adapter.WriteToFile();
                EventsTable.Rows.Clear();
            }
        }
    }
}
