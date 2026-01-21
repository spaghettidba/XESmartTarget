using DouglasCrockford.JsMin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Data;
using System.Dynamic;

namespace XESmartTarget.Core.Utils
{
    public class DataTableJsonAdapter
    {
        private DataTable Table;

        public string[] OutputColumns { get; set; } = Array.Empty<string>();
        public Dictionary<string,object> StaticAttributes { get; set; } = new Dictionary<string,object>();
        public bool Minify { get; set; }
        public enum OutputFormatEnum { 
            SingleObject,
            ObjectArray,
            IndependentObjects
        }

        public OutputFormatEnum OutputFormat { get; set; }

        public DataTableJsonAdapter(DataTable eventsTable)
        {
            this.Table = eventsTable;
        }

        public void WriteToStream(TextWriter writer)
        {
            if(Table.Rows.Count == 0)
            {
                return;
            }

            var converter = new ExpandoObjectConverter();
            var minifier = new JsMinifier();

            if (OutputColumns != null)
            {
                var validColumns = OutputColumns.Where(c => Table.Columns.Contains(c)).ToArray();
                if (validColumns.Length > 0)
                {
                    Table = Table.DefaultView.ToTable(false, validColumns);
                }
            }

            List<Object>? outputList = null;
            if(OutputFormat == OutputFormatEnum.ObjectArray)
            {
                outputList = new List<Object>();
            }

            foreach (DataRow dr in Table.Rows)
            {
                // create dynamic object from current row
                dynamic data = new ExpandoObject();

                IDictionary<string, object> outputObject = (IDictionary<string, object>)data;
                foreach (DataColumn dc in Table.Columns)
                {
                    var value = dr[dc.ColumnName];
                    if (value != null && value != DBNull.Value)
                    {
                        outputObject.Add(dc.ColumnName, value);
                    }
                }
                foreach (var k in StaticAttributes.Keys)
                {
                    outputObject.Add(k, StaticAttributes[k]);
                }

                if (OutputFormat == OutputFormatEnum.ObjectArray)
                {
                    outputList?.Add(outputObject);
                }
                else if(OutputFormat== OutputFormatEnum.IndependentObjects) 
                {
                    string outString = JsonConvert.SerializeObject(outputObject, converter);
                    if (Minify) 
                    {
                        outString = minifier.Minify(outString);
                    }
                    writer.WriteLine(outString);
                }
                
            }

            if (OutputFormat == OutputFormatEnum.ObjectArray && outputList != null)
            {
                string outString = JsonConvert.SerializeObject(outputList.ToArray(), converter);
                if (Minify)
                {
                    outString = minifier.Minify(outString);
                }
                writer.WriteLine(outString);
            }
            else if (OutputFormat == OutputFormatEnum.SingleObject)
            {
                dynamic data = new ExpandoObject();
                IDictionary<string, object> outputObject = (IDictionary<string, object>)data;
                outputObject.Add("data", outputList ?? new List<Object>());

                string outString = JsonConvert.SerializeObject(outputObject, converter);
                if (Minify)
                {
                    outString = minifier.Minify(outString);
                }
                writer.WriteLine(outString);
            }

            writer.Flush();
        }

    }
}
