using System.Data;
using NLog;
using XESmartTarget.Core.Utils;
using Microsoft.SqlServer.XEvent.XELite;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class TelegrafAppenderResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public string OutputMeasurement { get; set; }
        public List<string> OutputTags { get; set; } = new List<string>();
        public List<string> OutputFields { get; set; } = new List<string>();

        protected DataTable EventsTable { get; set; } = new DataTable("events");

        private XEventDataTableAdapter xeadapter;

        public TelegrafAppenderResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public override void Process(IXEvent evt)
        {
            Enqueue(evt);
        }

        protected void Enqueue(IXEvent evt)
        {
            if (xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(EventsTable);
                xeadapter.Filter = this.Filter;
                xeadapter.OutputColumns = new List<OutputColumn>(
                    OutputFields.Select(col => new OutputColumn(col) { ColumnType = OutputColumn.ColType.Field })
                    .Union(OutputTags.Select(col => new OutputColumn(col) { ColumnType = OutputColumn.ColType.Tag }))
                );
                xeadapter.OutputColumns.Add(new OutputColumn("collection_time") { ColumnType = OutputColumn.ColType.Column });
            }
            xeadapter.ReadEvent(evt);

            WriteToStdOut();
        }

        private void WriteToStdOut()
        {
            lock (EventsTable)
            {
                DataTableLineProtocolAdapter adapter = new DataTableLineProtocolAdapter(EventsTable);
                adapter.WriteToStdOut(OutputMeasurement);
                EventsTable.Rows.Clear();
            }
        }
    }
}

