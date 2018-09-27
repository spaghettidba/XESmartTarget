using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class GroupedTableAppenderResponse : TableAppenderResponse
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public GroupedTableAppenderResponse() : base()
        {
        }

        protected override void Upload()
        {
            logger.Trace("Writing grouped XE data");

            int numRows, originalRows;

            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = ConnectionString;
                conn.Open();

                DataTable groupedData = null;
                lock (EventsTable)
                {
                    originalRows = EventsTable.Rows.Count;
                    groupedData = GroupBy();
                    EventsTable.Rows.Clear();
                }

                if (!TargetTableCreated)
                {
                    CreateTargetTable(groupedData);
                }

                DataTableTSQLAdapter adapter = new DataTableTSQLAdapter(groupedData, conn)
                {
                    DestinationTableName = TableName
                };
                numRows = adapter.MergeToServer(_outputColumns);
                originalRows = groupedData.Rows.Count;

            }
            logger.Info(String.Format("{0} rows aggregated, {1} rows written", originalRows, numRows));
        }


        protected override void CreateTargetTable(DataTable data)
        {
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = ConnectionString;
                conn.Open();

                DataTableTSQLAdapter adapter = new DataTableTSQLAdapter(data, conn)
                {
                    DestinationTableName = TableName
                };
                if (!adapter.CheckTableExists())
                {
                    adapter.CreateFromDataTable();
                }
            }
        }


        private DataTable GroupBy()
        {

            DataView dv = new DataView(EventsTable);

            IEnumerable<string> GroupByCols = _outputColumns.Where(col => !(col is AggregatedOutputColumn) && !(col.Hidden)).Select(col => col.Name);

            //getting distinct values for group columns
            DataTable dtGroup = dv.ToTable(true, GroupByCols.ToArray());

            //Extract AggregatedColumns
            List<AggregatedOutputColumn> outCols = new List<AggregatedOutputColumn>(_outputColumns.Where(col => col is AggregatedOutputColumn).Select(col => new AggregatedOutputColumn(col)));

            //adding columns for the aggregations
            foreach (var col in outCols)
            {
                Type t = null;
                // set the base type on the aggregation: 
                // - MIN /MAX means use the base column type
                // - all other aggregations mean Int32
                if(!(col.Aggregation == AggregatedOutputColumn.AggregationType.Max || col.Aggregation == AggregatedOutputColumn.AggregationType.Min))
                {
                    t = typeof(Int32);
                }
                else if(EventsTable.Columns.Contains(col.BaseColumn))
                {
                    t = EventsTable.Columns[col.BaseColumn].DataType;
                }
                else
                {
                    throw new InvalidExpressionException(String.Format("The base column '{0}' for the aggregated column '{1}' was not found. Please correct your expression.",col.BaseColumn, col.Alias));
                }
                dtGroup.Columns.Add(col.Alias,t);
            }

            try
            {
                //looping thru distinct values for the group
                foreach (DataRow dr in dtGroup.Rows)
                {
                    foreach (var col in outCols)
                    {
                        string filterString = " 1 = 1 ";
                        foreach (var grp in GroupByCols)
                        {
                            filterString += String.Format(" AND {0} = '{1}' ", grp, dr[grp].ToString());
                        }
                        dr[col.Alias] = EventsTable.Compute(col.Expression, filterString);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Trace(e, "Something went wrong during the aggregation.");
                throw e;
            }

            // Set all columns as visible
            foreach(DataColumn dc in dtGroup.Columns)
            {
                if (dc.ExtendedProperties.Contains("hidden"))
                {
                    dc.ExtendedProperties["hidden"] =  false;
                }
                else
                {
                    dc.ExtendedProperties.Add("hidden", false);
                }
            }

            return dtGroup;
        }

    }
}
