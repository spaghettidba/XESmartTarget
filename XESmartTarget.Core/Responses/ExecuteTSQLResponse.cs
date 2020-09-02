using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using Microsoft.SqlServer.XEvent.Linq;
using System.Data.SqlClient;
using System.Data;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class ExecuteTSQLResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();


        public string TSQL { get; set; }
        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }


        protected DataTable EventsTable = new DataTable("events");
        private XEventDataTableAdapter xeadapter;


        protected string ConnectionString
        {
            get
            {
                string formattedServerName = ServerName;
                SmartFormatHelper.Format(ServerName, Tokens);

                int ConnectionTimeout = 15;
                string s = "Server=" + formattedServerName + ";";
                s += "Database=" + DatabaseName + ";";
                if (String.IsNullOrEmpty(UserName))
                {
                    s += "Integrated Security = True;";
                }
                else
                {
                    s += "User Id=" + UserName + ";";
                    s += "Password=" + Password + ";";
                }
                s += "Connection Timeout=" + ConnectionTimeout;
                logger.Debug(s);
                return s;
            }
        }

        public override void Process(PublishedEvent evt)
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
                        eventTokens.Add(dc.ColumnName, dr[dc]);
                    }
                    // also add the Response tokens
                    foreach (string k in Tokens.Keys)
                    {
                        if (!eventTokens.ContainsKey(k))
                            eventTokens.Add(k, Tokens[k]);
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
                catch(Exception e)
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
