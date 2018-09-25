using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
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

        public enum TargetTableActionType
        {
            Append,
            Delete,
            Merge
        }

        public GroupedTableAppenderResponse()
        {

        }

        public List<string> GroupByColumns { get; set; } // Groupby Columns

        public TargetTableActionType TargetTableAction { get; set; } = TargetTableActionType.Append;

        protected override void Upload()
        {
            logger.Trace("Writing XE data");

            int numRows;

            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = ConnectionString;
                conn.Open();

                if (!TargetTableCreated && AutoCreateTargetTable)
                {
                    CreateTargetTable();
                }

                lock (EventsTable)
                {
                    DataTableTSQLAdapter adapter = new DataTableTSQLAdapter(EventsTable, conn)
                    {
                        DestinationTableName = TableName
                    };
                    adapter.WriteToServer();
                    numRows = EventsTable.Rows.Count;
                    EventsTable.Rows.Clear();
                }

            }
            logger.Info(String.Format("{0} rows written", numRows));
        }
        
    }
}
