using CsvHelper;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Utils
{
    public class DataTableCSVAdapter
    {
        private DataTable Table { get; set; }
        public String OutputFile { get; set; }

        public DataTableCSVAdapter(DataTable table)
        {
            Table = table;
        }

        public DataTableCSVAdapter(DataTable table, String outFile)
        {
            OutputFile = outFile;
            Table = table;
        }


        public void WriteToFile()
        {
            using (BufferedStream f = new BufferedStream(new FileStream(OutputFile, FileMode.Append, FileAccess.Write), 16384))
            {
                TextWriter textWriter = new StreamWriter(f);
                var csv = new CsvWriter(textWriter);

                // Write Data
                foreach (DataRow dr in Table.Rows)
                {
                    foreach (DataColumn dc in Table.Columns)
                    {
                        csv.WriteField(dr[dc]);
                    }
                    csv.NextRecord();
                }
                csv.Flush();
                f.Flush();
            }

        }



        public void WriteHeaders()
        {
            using (BufferedStream f = new BufferedStream(new FileStream(OutputFile, FileMode.Append, FileAccess.Write), 4096))
            {
                TextWriter textWriter = new StreamWriter(f);
                var csv = new CsvWriter(textWriter);

                // Write Headers
                foreach (DataColumn dc in Table.Columns)
                {
                    csv.WriteField(dc.ColumnName);
                }
                csv.NextRecord();
                csv.Flush();
                f.Flush();
            }


        }
    }
}
