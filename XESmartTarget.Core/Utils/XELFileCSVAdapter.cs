using CsvHelper;
using Microsoft.SqlServer.XEvent.XELite;
using NLog;
using System.Globalization;

namespace XESmartTarget.Core.Utils
{
    public class XELFileCSVAdapter
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public string InputFile { get; set; }
        public string OutputFile { get; set; }

        private class CsvColumn
        {
            public string Name { get; set; }
            public char Type { get; set; }
            public int Position { get; set; }
        }

        public async Task Convert()
        {
            Dictionary<string, CsvColumn> columns = new Dictionary<string, CsvColumn>();
            logger.Trace(String.Format("Parsing starting {0}", DateTime.Now));

            var eventStreamer = new XEFileEventStreamer(InputFile);

            Task eventTask = eventStreamer.ReadEventStream(xevent =>
                {
                    foreach (var action in xevent.Actions)
                    {
                        if (!columns.ContainsKey(action.Key))
                        {
                            columns.Add(action.Key, new CsvColumn
                            {
                                Name = action.Key,
                                Type = 'a',
                                Position = columns.Count
                            });
                        }
                    }

                    foreach (var field in xevent.Fields)
                    {
                        if (!columns.ContainsKey(field.Key))
                        {
                            columns.Add(field.Key, new CsvColumn
                            {
                                Name = field.Key,
                                Type = 'f',
                                Position = columns.Count
                            });
                        }
                    }

                    return Task.CompletedTask;
                },
                CancellationToken.None  
            );
            Task.WaitAll(eventTask);

            logger.Trace(String.Format("Parsing finished {0}", DateTime.Now));

            List<CsvColumn> orderedColumns = new List<CsvColumn>(columns.Count);
            foreach (CsvColumn col in columns.Values)
            {
                orderedColumns.Insert(col.Position, col);
            }

            logger.Trace(String.Format("Starting output {0}", DateTime.Now));

            eventStreamer = new XEFileEventStreamer(InputFile);
            using (BufferedStream f = new BufferedStream(new FileStream(OutputFile, FileMode.Append, FileAccess.Write), 4096000))
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
                        await csv.NextRecordAsync();

                        Task eventTask2 = eventStreamer.ReadEventStream(xevent =>
                            {
                                csv.WriteField(xevent.Name);
                                csv.WriteField(xevent.Timestamp);
                                csv.WriteField(xevent.Timestamp.ToUniversalTime());

                                foreach (CsvColumn col in orderedColumns)
                                {
                                    if (col.Type == 'f')
                                    {
                                        if (xevent.Fields.TryGetValue(col.Name, out var fieldValue))
                                            csv.WriteField(fieldValue);
                                        else
                                            csv.WriteField("");
                                    }
                                    else
                                    {
                                        if (xevent.Actions.TryGetValue(col.Name, out var actionValue))
                                            csv.WriteField(actionValue);
                                        else
                                            csv.WriteField("");
                                    }
                                }
                                return csv.NextRecordAsync();
                            },
                            CancellationToken.None 
                        );
                        Task.WaitAll(eventTask2);
                    }
                }
            }
            logger.Trace(String.Format("Output finished {0}", DateTime.Now));
        }
    }
}
