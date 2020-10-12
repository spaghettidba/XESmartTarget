using CsvHelper;
using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Utils
{
    public class XELFileCSVAdapter
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public String InputFile { get; set; }
        public String OutputFile { get; set; }

        private class CsvColumn
        {
            public String Name { get; set; }
            public char Type { get; set; }
            public int Position { get; set; }
        }

        public void Convert()
        {
            Dictionary<String, CsvColumn> columns = new Dictionary<string, CsvColumn>();
            logger.Trace(String.Format("Parsing starting {0}", DateTime.Now));

            using (QueryableXEventData eventStream = new QueryableXEventData(InputFile))
            {

                // Read the file once to get all the columns
                foreach (PublishedEvent xevent in eventStream)
                {
                    foreach (PublishedAction action in xevent.Actions)
                    {
                        if (!columns.ContainsKey(action.Name))
                            columns.Add(action.Name, new CsvColumn() { Name = action.Name, Type = 'a', Position = columns.Count });
                    }

                    foreach (PublishedEventField field in xevent.Fields)
                    {
                        if (!columns.ContainsKey(field.Name))
                            columns.Add(field.Name, new CsvColumn() { Name = field.Name, Type = 'f', Position = columns.Count });
                    }
                }
            }

            logger.Trace(String.Format("Parsing finished {0}",DateTime.Now));

            List<CsvColumn> orderedColumns = new List<CsvColumn>(columns.Count);
            foreach(CsvColumn col in columns.Values)
            {
                orderedColumns.Insert(col.Position, col);
            }

            logger.Trace(String.Format("Starting output {0}", DateTime.Now));

            using (QueryableXEventData eventStream = new QueryableXEventData(InputFile))
            {
                using (BufferedStream f = new BufferedStream(new FileStream(OutputFile, FileMode.Append, FileAccess.Write),4096000))
                {
                    using (TextWriter textWriter = new StreamWriter(f))
                    {
                        using (var csv = new CsvWriter(textWriter, CultureInfo.CurrentCulture))
                        {
                            // Write Headers
                            csv.WriteField("name");
                            csv.WriteField("timestamp");
                            csv.WriteField("timestamp(UTC)");
                            foreach (CsvColumn col in orderedColumns)
                            {
                                csv.WriteField(col.Name);
                            }

                            csv.NextRecordAsync();


                            // Write Data
                            foreach (PublishedEvent xevent in eventStream)
                            {
                                csv.WriteField(xevent.Name);
                                csv.WriteField(xevent.Timestamp);
                                csv.WriteField(xevent.Timestamp.ToUniversalTime());
                                foreach (CsvColumn col in orderedColumns)
                                {
                                    if (col.Type == 'f')
                                    {
                                        PublishedEventField theValue = null;
                                        if (xevent.Fields.TryGetValue(col.Name, out theValue))
                                            csv.WriteField(theValue.Value);
                                        else
                                            csv.WriteField("");
                                    }
                                    else
                                    {
                                        PublishedAction theValue = null;
                                        if (xevent.Actions.TryGetValue(col.Name, out theValue))
                                            csv.WriteField(theValue.Value);
                                        else
                                            csv.WriteField("");
                                    }
                                }

                                csv.NextRecordAsync();
                            }
                        }
                    }
                }
            }
            logger.Trace(String.Format("Output finished {0}", DateTime.Now));
        }

    }
}
