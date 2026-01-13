using Microsoft.SqlServer.XEvent.XELite;
using NLog;
using SmartFormat.Core.Formatting;
using System.Data;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class OutputStreamAppenderResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private static object Lock = new object();
        public List<string> OutputColumns { get; set; } = new List<string>();
        public string OutputMeasurement { get; set; }
        public DataTableJsonAdapter.OutputFormatEnum JsonOutputFormat { get; set; }

        private OutputFormatEnum _outputFormat;
        public string OutputFormat {
            get 
            { 
                return _outputFormat.ToString(); 
            }
            set 
            {
                try
                {
                    _outputFormat = (OutputFormatEnum)Enum.Parse(typeof(OutputFormatEnum), value);
                }
                catch
                {
                    throw new ArgumentException($"'{value}' is not a valid OutputFormat.");
                }
            }
        }

        protected TextWriter Writer { get; set; }
        private string _output;

        protected virtual string Output
        {
            get 
            {
                return _output;
            }
            set 
            {
                _output = value;
                if(_output.Equals("stderr",StringComparison.CurrentCultureIgnoreCase))
                {
                    Writer = Console.Error;
                }
                else if (_output.Equals("stdout", StringComparison.CurrentCultureIgnoreCase))
                {
                    Writer = Console.Out;
                }
                else if (isValidPath(_output))
                {
                    Writer = new StreamWriter(new FileStream(_output, FileMode.Append, FileAccess.Write));
                }

            }
        }

        private bool isValidPath(string value)
        {
            try
            {
                Path.GetFullPath(value);
                return Path.IsPathRooted(value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected enum OutputFormatEnum
        {
            LineProtocol,
            Json,
            Csv
        }

        protected DataTable EventsTable { get; set; } = new DataTable("events");
        private XEventDataTableAdapter xeadapter;
        protected Task writerTask;
        private bool writerTaskStarted;

        public OutputStreamAppenderResponse()
        {
            Output = "stdout";
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public override object Clone()
        {
            var clone = (OutputStreamAppenderResponse)CloneBase();
            // Create a new EventsTable for this clone to prevent sharing
            clone.EventsTable = new DataTable("events");
            // Reset the adapter so it will be initialized with the new EventsTable
            clone.xeadapter = null;
            clone.writerTask = null;
            clone.writerTaskStarted = false;
            return clone;
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
                xeadapter.OutputColumns = new List<OutputColumn>(OutputColumns.Select(col => new OutputColumn(col)));
            }
            xeadapter.ReadEvent(evt);
            if (!writerTaskStarted)
            {
                StartWriterTask();
            }
        }

        private void StartWriterTask()
        {
            if (writerTask == null)
            {
                writerTask = Task.Factory.StartNew(() => WriteTaskMain());
            }
            writerTaskStarted = true;
        }

        private void WriteTaskMain()
        {
            while (true)
            {
                try
                {
                    Write();
                    Thread.Sleep(100);
                }
                catch (Exception e)
                {
                    logger.Error("Error writing to the output stream");
                    logger.Error(e);
                }
            }
        }

        protected void Write()
        {
            lock (EventsTable)
            {
                if (EventsTable.Rows.Count == 0)
                {
                    return;
                }
                lock (Lock)
                {
                    //
                    // before writing, replace tokens
                    //

                    // 
                    // we need to treat computed columns (readonly) first
                    //
                    var columnsToModify = new List<(DataColumn column, string newExpression)>();

                    foreach (DataColumn dc in EventsTable.Columns)
                    {
                        // readonly means that the column is computed
                        // we need to replace tokens in the expression
                        // this should be fine as the tokens are static per response
                        // and each server/target will have its own response instance

                        if (dc.DataType == typeof(string) && dc.ReadOnly)
                        {
                            string expression = dc.Expression;
                            try
                            {
                                string formatted = SmartFormatHelper.Format(expression, Tokens);
                                columnsToModify.Add((dc, formatted));
                            }
                            catch (FormattingException) 
                            {
                                // Do not log here as this can flood the logs
                                // logger.Warn($"The value {dc.Expression} contains placeholders that cannot be formatted.");
                            }
                        }
                    }

                    foreach (var (column, newExpression) in columnsToModify)
                    {
                        column.Expression = newExpression;
                    }


                    //
                    // now process non computed columns
                    //
                    foreach (DataColumn dc in EventsTable.Columns)
                    {
                        if (dc.DataType == typeof(string))
                        {
                            if (!dc.ReadOnly)
                            {
                                foreach (DataRow dr in EventsTable.Rows)
                                {
                                    string? cellValue = dr[dc] as string;
                                    if (!string.IsNullOrEmpty(cellValue))
                                    {
                                        try
                                        {
                                            dr[dc] = SmartFormatHelper.Format(cellValue, Tokens);
                                        }
                                        catch (FormattingException)
                                        {
                                            // tokens can't be formatted
                                            // but don't flood the logs
                                            // logger.Warn($"The value {cellValue} contains placeholders that cannot be formatted.");
                                            dr[dc] = cellValue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    

                    // proceed with writing
                    if (_outputFormat == OutputFormatEnum.Json)
                    {
                        DataTableJsonAdapter adapter = new DataTableJsonAdapter(EventsTable)
                        {
                            OutputFormat = JsonOutputFormat,
                            OutputColumns = xeadapter.OutputColumns.Select(x => x.Name).ToArray()
                        };
                        adapter.WriteToStream(Writer);
                    }
                    else if(_outputFormat == OutputFormatEnum.LineProtocol)
                    {
                        DataTableLineProtocolAdapter adapter = new DataTableLineProtocolAdapter(EventsTable)
                        {
                            OutputMeasurement = OutputMeasurement
                            // todo: set output fields and tags
                        };
                        adapter.WriteToStream(Writer);
                    }
                    else if (_outputFormat == OutputFormatEnum.Csv)
                    {
                        DataTableCSVAdapter adapter = new DataTableCSVAdapter(EventsTable)
                        {
                            OutputColumns = xeadapter.OutputColumns.Select(x => x.Name).ToArray()
                        };
                        adapter.WriteToStream(Writer);
                    }
                }

                EventsTable.Rows.Clear();
            }
        }
    }
}
