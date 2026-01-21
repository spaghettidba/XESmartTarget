using Microsoft.SqlServer.XEvent.XELite;
using NLog;
using System.Data;
using System.Text.RegularExpressions;
using XESmartTarget.Core.Responses;

namespace XESmartTarget.Core.Utils
{
    class XEventDataTableAdapter
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public DataTable eventsTable { get; }
        public List<OutputColumn> OutputColumns { get; set; }
        public string? Filter { get; set; }

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
                if (!eventsTable.Columns.Contains("collection_time") && (OutputColumns.Count == 0 || OutputColumns.Exists(x => x.Name == "collection_time") || (Filter != null && Filter.Contains("collection_time"))))
                {
                    DataColumn cl_dt = new DataColumn("collection_time", typeof(DateTime))
                    {
                        DefaultValue = DateTime.Now
                    };
                    cl_dt.ExtendedProperties.Add("auto_column", true);
                    SetColHiddenProperty(cl_dt);
                    eventsTable.Columns.Add(cl_dt);
                }

                //
                // Add Collection Time ISO
                //
                if (!eventsTable.Columns.Contains("collection_time_iso") && (OutputColumns.Count == 0 || OutputColumns.Exists(x => x.Name == "collection_time_iso") || (Filter != null && Filter.Contains("collection_time_iso"))))
                {
                    DataColumn cl_dt = new DataColumn("collection_time_iso", typeof(String))
                    {
                        DefaultValue = DateTime.Now.ToString("o")
                    };
                    cl_dt.ExtendedProperties.Add("auto_column", true);
                    SetColHiddenProperty(cl_dt);
                    eventsTable.Columns.Add(cl_dt);
                }

                //
                // Add Name column
                //
                if (!eventsTable.Columns.Contains("name") && (OutputColumns.Count == 0 || OutputColumns.Exists(x => x.Name == "name") || (Filter != null && Filter.Contains("name"))))
                {
                    eventsTable.Columns.Add("name", typeof(String));
                    eventsTable.Columns["name"].ExtendedProperties.Add("auto_column", true);
                }
            }
        }

        // sets the column as hidden when not present in the list of visible columns
        private void SetColHiddenProperty(DataColumn cl_dt)
        {
            OutputColumn currentCol = OutputColumns.FirstOrDefault(x => x.Name == cl_dt.ColumnName);
            if (currentCol != null)
            {
                cl_dt.ExtendedProperties.Add("hidden", currentCol.Hidden);
            }
            else
            {
                cl_dt.ExtendedProperties.Add("hidden", false);
            }
        }

        public void ReadEvent(IXEvent xevent)
        {
            Prepare();
            //
            // Read event data
            //
            lock (eventsTable)
            {
                foreach (var fld in xevent.Fields)
                {
                    if (
                        !eventsTable.Columns.Contains(fld.Key)
                        && (
                            OutputColumns.Count == 0
                            || OutputColumns.Exists(x => x.Name == fld.Key)
                            || OutputColumns.Exists(x => x.Calculated && Regex.IsMatch(x.Name, @"\s+AS\s+.*" + fld.Key, RegexOptions.IgnoreCase))
                            || (Filter != null && Filter.Contains(fld.Key))
                        )
                    )
                    {
                        OutputColumn col = OutputColumns.FirstOrDefault(x => x.Name == fld.Key);

                        if (col == null)
                        {
                            col = new OutputColumn()
                            {
                                Name = fld.Key,
                                ColumnType = OutputColumn.ColType.Column
                            };
                        }

                        Type t;
                        DataColumn dc;
                        bool disallowed = false;
                        if (DataTableTSQLAdapter.AllowedDataTypes.Contains(fld.Value.GetType().ToString()))
                        {
                            t = fld.Value.GetType();
                        }
                        else
                        {
                            t = Type.GetType("System.String");
                        }
                        dc = eventsTable.Columns.Add(fld.Key, t);
                        dc.ExtendedProperties.Add("subtype", "field");
                        dc.ExtendedProperties.Add("disallowedtype", disallowed);
                        dc.ExtendedProperties.Add("calculated", false);
                        dc.ExtendedProperties.Add("coltype", col.ColumnType);
                        SetColHiddenProperty(dc);
                    }
                }

                foreach (var act in xevent.Actions)
                {
                    if (
                        !eventsTable.Columns.Contains(act.Key)
                        && (
                            OutputColumns.Count == 0
                            || OutputColumns.Exists(x => x.Name == act.Key)
                            || OutputColumns.Exists(x => x.Calculated && Regex.IsMatch(x.Name, @"\s+AS\s+.*" + act.Key, RegexOptions.IgnoreCase))
                        )
                    )
                    {
                        OutputColumn col = OutputColumns.FirstOrDefault(x => x.Name == act.Key);

                        if (col == null)
                        {
                            col = new OutputColumn()
                            {
                                Name = act.Key,
                                ColumnType = OutputColumn.ColType.Column
                            };
                        }

                        Type t;
                        DataColumn dc;
                        bool disallowed = false;
                        if (DataTableTSQLAdapter.AllowedDataTypes.Contains(act.Value.GetType().ToString()))
                        {
                            t = act.Value.GetType();
                        }
                        else
                        {
                            t = Type.GetType("System.String");
                        }
                        dc = eventsTable.Columns.Add(act.Key, t);
                        dc.ExtendedProperties.Add("subtype", "action");
                        dc.ExtendedProperties.Add("disallowedtype", disallowed);
                        dc.ExtendedProperties.Add("calculated", false);
                        dc.ExtendedProperties.Add("coltype", col.ColumnType);
                        SetColHiddenProperty(dc);
                    }
                }

                // add calculated columns
                for (int i = 0; i < OutputColumns.Count; i++)
                {
                    string outCol = OutputColumns[i].Name;
                    if (!eventsTable.Columns.Contains(outCol))
                    {
                        if (Regex.IsMatch(outCol, @"\s+AS\s+", RegexOptions.IgnoreCase))
                        {
                            var tokens = Regex.Split(outCol, @"\s+AS\s+", RegexOptions.IgnoreCase);
                            string colName = tokens[0];
                            string colDefinition = tokens[1];

                            if (!eventsTable.Columns.Contains(colName))
                            {
                                DataColumn dc;
                                dc = eventsTable.Columns.Add();
                                dc.ColumnName = colName;
                                dc.Expression = colDefinition;
                                dc.ExtendedProperties.Add("subtype", "calculated");
                                dc.ExtendedProperties.Add("disallowedtype", false);
                                dc.ExtendedProperties.Add("calculated", true);
                                dc.ExtendedProperties.Add("coltype", OutputColumns[i].ColumnType);
                                SetColHiddenProperty(dc);
                            }
                            //change OutputColumns
                            OutputColumns[i].Name = colName;
                        }
                    }
                }
            }

            DataTable tmpTab = eventsTable.Clone();
            DataRow row = tmpTab.NewRow();
            if (row.Table.Columns.Contains("name"))
            {
                row.SetField("name", xevent.Name);
            }
            if (row.Table.Columns.Contains("collection_time"))
            {
                row.SetField("collection_time", xevent.Timestamp.LocalDateTime);
            }
            if (row.Table.Columns.Contains("collection_time_iso"))
            {
                row.SetField("collection_time_iso", xevent.Timestamp.ToString("o"));
            }

            foreach (var fld in xevent.Fields)
            {
                if (row.Table.Columns.Contains(fld.Key))
                {
                    if ((bool)row.Table.Columns[fld.Key].ExtendedProperties["disallowedtype"])
                    {
                        row.SetField(fld.Key, fld.Value.ToString());
                    }
                    else
                    {
                        row.SetField(fld.Key, fld.Value);
                    }
                }
            }

            foreach (var act in xevent.Actions)
            {
                if (row.Table.Columns.Contains(act.Key))
                {
                    if ((bool)row.Table.Columns[act.Key].ExtendedProperties["disallowedtype"])
                    {
                        row.SetField(act.Key, act.Value.ToString());
                    }
                    else
                    {
                        row.SetField(act.Key, act.Value);
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
