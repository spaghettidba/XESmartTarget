using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System;
using System.Data;
using System.IO;
using XESmartTarget.Core.Responses;

namespace XESmartTarget.Core.Utils
{
    public class DataTableLineProtocolAdapter
    {
        private DataTable eventsTable;

        public DataTableLineProtocolAdapter(DataTable eventsTable)
        {
            this.eventsTable = eventsTable;
        }

        public string OutputMeasurement { get; set; }

        internal void WriteToStdOut(string OutputMeasurement)
        {
            this.OutputMeasurement = OutputMeasurement;

            using (BufferedStream f = new BufferedStream(Console.OpenStandardOutput()))
            {
                using (TextWriter textWriter = new StreamWriter(f) { AutoFlush = true })
                {
                    WriteToStream(textWriter);

                    textWriter.Flush();
                }
            }
        }

        internal void WriteToStream(TextWriter writer)
        {
            foreach (DataRow dr in eventsTable.Rows)
            {
                var dtOffs = new DateTimeOffset((DateTime)dr["collection_time"]);
                var point = PointData.Measurement(OutputMeasurement).Timestamp(dtOffs, WritePrecision.Ns);

                foreach (DataColumn dc in eventsTable.Columns)
                {
                    OutputColumn.ColType prop = OutputColumn.ColType.Column;
                    if (dc.ExtendedProperties.ContainsKey("coltype"))
                    {
                        prop = (OutputColumn.ColType)dc.ExtendedProperties["coltype"];
                    }
                    if (prop == OutputColumn.ColType.Tag)
                    {
                        point = point.Tag(dc.ColumnName, dr[dc.ColumnName].ToString());
                    }
                    else if (prop == OutputColumn.ColType.Field)
                    {
                        var colValue = dr[dc.ColumnName];
                        if (colValue is bool)
                        {
                            point = point.Field(dc.ColumnName, Convert.ToBoolean(colValue));
                        }
                        else if (colValue is decimal)
                        {
                            point = point.Field(dc.ColumnName, Convert.ToDecimal(colValue));
                        }
                        else if (colValue is double)
                        {
                            point = point.Field(dc.ColumnName, Convert.ToDouble(colValue));
                        }
                        else if (colValue is float)
                        {
                            point = point.Field(dc.ColumnName, Convert.ToSingle(colValue));
                        }
                        else if (colValue is long || colValue is int || colValue is short || colValue is sbyte || colValue is Int16 || colValue is Int32 || colValue is Int64)
                        {
                            point = point.Field(dc.ColumnName, Convert.ToInt64(colValue));
                        }
                        else if (colValue is string)
                        {
                            point = point.Field(dc.ColumnName, Convert.ToString(colValue));
                        }
                        else if (colValue is ulong || colValue is uint || colValue is ushort || colValue is byte || colValue is UInt16 || colValue is UInt32 || colValue is UInt64)
                        {
                            point = point.Field(dc.ColumnName, Convert.ToUInt64(colValue));
                        }

                    }
                }

                // If no field has been defined, add a static field
                if (!point.HasFields())
                {
                    point = point.Field("counter", 1);
                }
                writer.WriteLine(point.ToLineProtocol());
            }
        }
    }
}