using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.XEvent.Linq;
using System.Data.SqlClient;
using NLog;

namespace XESmartTarget.Core.Responses
{
    class ReplayResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public string ServerName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string DatabaseName { get; set; }
        public bool StopOnError { get; set; }

        private SqlConnection conn;

        public ReplayResponse()
        {

        }

        public override void Process(PublishedEvent evt)
        {
            if(conn == null)
            {
                InitializeConnection();
            }
            
            string commandText = null;
            if(evt.Name == "rpc_completed")
            {
                commandText = evt.Fields["statement"].Value.ToString();
            }
            else if(evt.Name == "sql_batch_completed")
            {
                commandText = evt.Fields["batch_text"].Value.ToString();
            }
            else
            {
                //ignore events not suitable for replay
                logger.Debug(String.Format("Skipping event {0}",evt.Name));
                return;
            }

            //TODO: implement filtering

            if(conn.State != System.Data.ConnectionState.Open)
            {
                conn.Open();
            }

            PublishedAction dbAct = null;
            if (evt.Actions.TryGetValue("database_name", out dbAct))
            {
                string dbname = dbAct.Value.ToString();
                logger.Trace(String.Format("Changing database to {0}", dbname));
                conn.ChangeDatabase(dbname);
            }

            try
            {
                SqlCommand cmd = new SqlCommand(commandText);
                cmd.Connection = conn;
                cmd.ExecuteNonQuery();
            }
            catch(SqlException e)
            {
                if (StopOnError)
                {
                    throw;
                }
                else
                {
                    logger.Warn(String.Format("Error: {0}", e.Message) ); 
                    logger.Trace(e);
                }
            }
        }

        private void InitializeConnection()
        {
            logger.Info(String.Format("Connecting to server {0} for replay...", ServerName));
            string connString = BuildConnectionString();
            conn = new SqlConnection(connString);
            conn.Open();
            logger.Info("Connected");
        }

        private string BuildConnectionString()
        {
            string connectionString = "Data Source=" + ServerName + ";";
            if (String.IsNullOrEmpty(DatabaseName))
            {
                connectionString += "Initial Catalog = master; ";
            }
            else
            {
                connectionString += "Initial Catalog = " + DatabaseName + "; ";
            }
            if (String.IsNullOrEmpty(UserName))
            {
                connectionString += "Integrated Security = SSPI; ";
            }
            else
            {
                connectionString += "User Id = " + UserName + "; ";
                connectionString += "Password = " + Password + "; ";
            }
            return connectionString;
        }
    }
}
