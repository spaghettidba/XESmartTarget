using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms.Design;
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

        public override void Process(PublishedEvent evt)
        {
            Enqueue(evt);
        }

        protected void Enqueue(PublishedEvent evt)
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
                lock (Lock)
                {
                    if (_outputFormat == OutputFormatEnum.Json)
                    {
                        DataTableJsonAdapter adapter = new DataTableJsonAdapter(EventsTable)
                        {
                            OutputFormat = DataTableJsonAdapter.OutputFormatEnum.IndependentObjects,
                            OutputColumns = xeadapter.OutputColumns.Select(x => x.Name).ToArray()
                        };
                        if(OutputMeasurement != null)
                        {
                            adapter.StaticAttributes.Add("OutputMeasurement", OutputMeasurement);
                        }
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
