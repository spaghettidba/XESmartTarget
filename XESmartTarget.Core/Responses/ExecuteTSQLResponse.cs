using NLog;
using Microsoft.SqlServer.XEvent.XELite;
using Microsoft.Data.SqlClient;
using System.Data;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class ExecuteTSQLResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public string TSQL { get; set; }
        public string ServerName
        {
            get => ConnectionInfo.ServerName;
            set => ConnectionInfo.ServerName = value;
        }

        public string DatabaseName
        {
            get => ConnectionInfo.DatabaseName;
            set => ConnectionInfo.DatabaseName = value;
        }

        public string UserName
        {
            get => ConnectionInfo.UserName;
            set => ConnectionInfo.UserName = value;
        }

        public string Password
        {
            get => ConnectionInfo.Password;
            set => ConnectionInfo.Password = value;
        }

        public int? ConnectTimeout
        {
            get => ConnectionInfo.ConnectTimeout;
            set => ConnectionInfo.ConnectTimeout = value;
        }

        public bool TrustServerCertificate
        {
            get => ConnectionInfo.TrustServerCertificate;
            set => ConnectionInfo.TrustServerCertificate = value;
        }

        private string ConnectionString => ConnectionInfo.ConnectionString;
        private SqlConnectionInfo ConnectionInfo { get; set; } = new();   

        protected DataTable EventsTable = new DataTable("events");
        private XEventDataTableAdapter xeadapter;

        public override void Process(IXEvent evt)
        {
            if (xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(EventsTable);
                xeadapter.Filter = this.Filter;
                xeadapter.OutputColumns = new List<OutputColumn>();
            }
            xeadapter.ReadEvent(evt);

            lock (EventsTable)
            {
                foreach (DataRow dr in EventsTable.Rows)
                {
                    Dictionary<string, object> eventTokens = new Dictionary<string, object>();
                    foreach (DataColumn dc in EventsTable.Columns)
                    {
                        if (dr[dc] is byte[] bytes)
                        {
                            string hex = Convert.ToHexString(bytes);
                            eventTokens.Add(dc.ColumnName, "0x" + hex);
                        }
                        else
                        {
                            eventTokens.Add(dc.ColumnName, dr[dc]);
                        }
                    }
                    // also add the Response tokens
                    foreach (string k in Tokens.Keys)
                    {
                        if (!eventTokens.ContainsKey(k))
                        {
                            eventTokens.Add(k, Tokens[k]);
                        }
                    }
                    string formattedTSQL = SmartFormatHelper.Format(TSQL, eventTokens);

                    Task t = Task.Factory.StartNew(() => ExecuteTSQL(formattedTSQL));
                }

                EventsTable.Clear();
            }
        }

        private void ExecuteTSQL(string TSQLString)
        {
            logger.Trace("Executing TSQL command");
            using (SqlConnection conn = new SqlConnection())
            {
                try
                {
                    conn.ConnectionString = ConnectionString;
                    conn.Open();
                }
                catch (Exception e)
                {
                    logger.Error(String.Format("Error: {0}", e.Message));
                    throw;
                }

                try
                {
                    SqlCommand cmd = new SqlCommand(TSQLString);
                    cmd.Connection = conn;
                    cmd.ExecuteNonQuery();
                    logger.Trace(String.Format("SUCCES - {0}", TSQLString));
                }
                catch (SqlException e)
                {
                    logger.Error(e, String.Format("Error: {0}", TSQLString));
                    throw;
                }
            }
        }
    }
}

