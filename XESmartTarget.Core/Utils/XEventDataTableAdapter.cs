using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Utils
{
    class XEventDataTableAdapter
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public DataTable eventsTable { get; }
        public List<string> OutputColumns { get; set; }
        public string Filter { get; set; }


        public XEventDataTableAdapter(DataTable table)
        {
            eventsTable = table;
        }


        private void Prepare()
        {
            lock (eventsTable)
            {
                //
                // Add Collection Time column
                //
                if (!eventsTable.Columns.Contains("collection_time") && (OutputColumns.Count == 0 || OutputColumns.Contains("collection_time")))
                {
                    DataColumn cl_dt = new DataColumn("collection_time", typeof(DateTime))
                    {
                        DefaultValue = DateTime.Now
                    };
                    cl_dt.ExtendedProperties.Add("auto_column", true);
                    eventsTable.Columns.Add(cl_dt);
                }


                //
                // Add Name column
                //
                if (!eventsTable.Columns.Contains("Name") && (OutputColumns.Count == 0 || OutputColumns.Contains("Name")))
                {
                    eventsTable.Columns.Add("Name", typeof(String));
                    eventsTable.Columns["Name"].ExtendedProperties.Add("auto_column", true);
                }
            }
        }

        public void ReadEvent(PublishedEvent evt)
        {
            Prepare();
            //
            // Read event data
            //
            lock (eventsTable)
            {
                foreach (PublishedEventField fld in evt.Fields)
                {
                    if (!eventsTable.Columns.Contains(fld.Name) && (OutputColumns.Count == 0 || OutputColumns.Contains(fld.Name)))
                    {
                        Type t;
                        DataColumn dc;
                        bool disallowed = false;
                        if (DataTableTSQLAdapter.AllowedDataTypes.Contains(fld.Type.ToString()))
                        {
                            t = fld.Type;
                        }
                        else
                        {
                            t = Type.GetType("System.String");
                        }
                        dc = eventsTable.Columns.Add(fld.Name, t);
                        dc.ExtendedProperties.Add("subtype", "field");
                        dc.ExtendedProperties.Add("disallowedtype", disallowed);
                        dc.ExtendedProperties.Add("calculated", false);
                    }
                }

                foreach (PublishedAction act in evt.Actions)
                {
                    if (!eventsTable.Columns.Contains(act.Name) && (OutputColumns.Count == 0 || OutputColumns.Contains(act.Name)))
                    {
                        Type t;
                        DataColumn dc;
                        bool disallowed = false;
                        if (DataTableTSQLAdapter.AllowedDataTypes.Contains(act.Type.ToString()))
                        {
                            t = act.Type;
                        }
                        else
                        {
                            t = Type.GetType("System.String");
                        }
                        dc = eventsTable.Columns.Add(act.Name, t);
                        dc.ExtendedProperties.Add("subtype", "action");
                        dc.ExtendedProperties.Add("disallowedtype", disallowed);
                        dc.ExtendedProperties.Add("calculated", false);
                    }
                }


                // add calculated columns
                for(int i=0;i<OutputColumns.Count;i++)
                {
                    string outCol = OutputColumns[i];
                    if (!eventsTable.Columns.Contains(outCol))
                    {
                        if (Regex.IsMatch(outCol,@"\s+AS\s+",RegexOptions.IgnoreCase))
                        {
                            var tokens = Regex.Split(outCol, @"\s+AS\s+", RegexOptions.IgnoreCase); 
                            string colName = tokens[0];
                            string colDefinition = tokens[1];

                            DataColumn dc;
                            dc = eventsTable.Columns.Add();
                            dc.ColumnName = colName;
                            dc.Expression = colDefinition;
                            dc.ExtendedProperties.Add("subtype", "calculated");
                            dc.ExtendedProperties.Add("disallowedtype", false);
                            dc.ExtendedProperties.Add("calculated", true);

                            //change OutputColumns
                            OutputColumns[i] = colName;
                        }

                    }
                }
            }

            DataTable tmpTab = eventsTable.Clone();
            DataRow row = tmpTab.NewRow();
            if (row.Table.Columns.Contains("Name"))
            {
                row.SetField("Name", evt.Name);
            }
            if (row.Table.Columns.Contains("collection_time"))
            {
                row.SetField("collection_time", evt.Timestamp.LocalDateTime);
            }

            foreach (PublishedEventField fld in evt.Fields)
            {
                if (row.Table.Columns.Contains(fld.Name))
                {
                    if ((bool)row.Table.Columns[fld.Name].ExtendedProperties["disallowedtype"])
                    {
                        row.SetField(fld.Name, fld.Value.ToString());
                    }
                    else
                    {
                        row.SetField(fld.Name, fld.Value);
                    }
                }
            }

            foreach (PublishedAction act in evt.Actions)
            {
                if (row.Table.Columns.Contains(act.Name))
                {
                    if ((bool)row.Table.Columns[act.Name].ExtendedProperties["disallowedtype"])
                    {
                        row.SetField(act.Name, act.Value.ToString());
                    }
                    else
                    {
                        row.SetField(act.Name, act.Value);
                    }
                }
            }

            if (!String.IsNullOrEmpty(Filter))
            {

                DataView dv = new DataView(tmpTab);
                dv.RowFilter = Filter;

                tmpTab.Rows.Add(row);

                lock (eventsTable)
                {
                    foreach (DataRow dr in dv.ToTable().Rows)
                    {
                        eventsTable.ImportRow(dr);
                    }
                }
            }
            else
            {
                tmpTab.Rows.Add(row);
                lock (eventsTable)
                {
                    foreach (DataRow dr in tmpTab.Rows)
                    {
                        eventsTable.ImportRow(dr);
                    }
                }
            }

        }
    }
}
