using CsvHelper;
using DouglasCrockford.JsMin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace XESmartTarget.Core.Utils
{
    public class DataTableJsonAdapter
    {
        private DataTable Table;

        public string[] OutputColumns { get; set; }
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
            var converter = new ExpandoObjectConverter();
            var minifier = new JsMinifier();

            if (OutputColumns != null)
            {
                Table = Table.DefaultView.ToTable(false, OutputColumns);
            }

            List<Object> outputList = null;
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
                    outputObject.Add(dc.ColumnName, dr[dc.ColumnName]);
                }
                foreach (var k in StaticAttributes.Keys)
                {
                    outputObject.Add(k, StaticAttributes[k]);
                }

                if (OutputFormat == OutputFormatEnum.ObjectArray)
                {
                    outputList.Add(outputObject);
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

            if (OutputFormat == OutputFormatEnum.ObjectArray)
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
                outputObject.Add("data", outputList);

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
