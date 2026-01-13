using Microsoft.SqlServer.XEvent.XELite;
using NLog;
using System.Data;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class CsvAppenderResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        
        public CsvAppenderResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public string OutputFile {
            get
            {
                return _outputFile;
            }
            set
            {
                _outputFile = value;
                _formattedOutputFile = SmartFormatHelper.Format(_outputFile, Tokens);
            }
        }

        private string _outputFile;
        private string _formattedOutputFile;

        public bool Overwrite { get; set; } = true;

        public List<string> OutputColumns { get; set; } = new List<string>(); 
        protected DataTable EventsTable { get => eventsTable; set => eventsTable = value; }
        private DataTable eventsTable = new DataTable("events");
        private XEventDataTableAdapter xeadapter;

        private bool writeHeaders = true;

        public override void Process(IXEvent evt)
        {
            Enqueue(evt);
        }


        protected void Enqueue(IXEvent evt)
        {
            if (xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(eventsTable);
                xeadapter.Filter = this.Filter;
                xeadapter.OutputColumns = new List<OutputColumn>(OutputColumns.Select(col => new OutputColumn(col)));

                if (Overwrite && File.Exists(_formattedOutputFile))
                {
                    File.Delete(_formattedOutputFile);
                }
            }
            xeadapter.ReadEvent(evt);

            WriteToFile(writeHeaders);

            writeHeaders = false;

        }

        private void WriteToFile(bool writeHeaders)
        {
            // Put columns in the correct order
            string[] outputColumnNames = (
                from col in xeadapter.OutputColumns
                where eventsTable.Columns.Contains(col.Alias)
                select col.Alias
            ).ToArray();

            lock (EventsTable)
            {
                DataTableCSVAdapter adapter = new DataTableCSVAdapter(EventsTable, _formattedOutputFile, outputColumnNames);
                adapter.WriteToFile(writeHeaders);
                EventsTable.Rows.Clear();
            }
        }

        public override object Clone()
        {
            CsvAppenderResponse clone = (CsvAppenderResponse)CloneBase();
            // Deep copy the EventsTable to avoid sharing references
            clone.EventsTable = new DataTable("events");
            // Deep copy the OutputColumns list to avoid sharing references
            clone.OutputColumns = new List<string>(this.OutputColumns);
            // Reset the adapter so it will be initialized with the new EventsTable
            clone.xeadapter = null;
            return clone;
        }

    }
}
