using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class TableAppenderResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        
        public TableAppenderResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public string ServerName { get; set; }
        public string DatabaseName { get; set; }
        public string TableName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool AutoCreateTargetTable { get; set; }
        public int UploadIntervalSeconds { get; set; } = 10;
        public List<string> OutputColumns { get; set; } = new List<string>(); 
        protected DataTable EventsTable { get => eventsTable; set => eventsTable = value; }

        protected Task Uploader;

        private XEventDataTableAdapter xeadapter;

        protected string ConnectionString
        {
            get
            {
                int ConnectionTimeout = 15;
                string s = "Server=" + ServerName + ";";
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

        private DataTable eventsTable = new DataTable("events");

        // This activates optimizations that allow to skip checks on the columns from events
        // Two possible actions when the more than on e event shows up in the queue: 
        // 1 - disable this flag, raise a warning and keep going without the optimizations
        // 2 - throw error.
        public bool IsSingleEvent = true;
        public bool FailOnSingleEventViolation = false;

        public override void Process(PublishedEvent evt)
        {
            Enqueue(evt);
        }

        private bool UploaderStarted = false;
        private bool TargetTableCreated = false;

        protected void Enqueue(PublishedEvent evt)
        {
            if(xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(eventsTable);
                xeadapter.Filter = this.Filter;
                xeadapter.OutputColumns = this.OutputColumns;
            }
            xeadapter.ReadEvent(evt);

            if(!UploaderStarted)
            {
                StartUploadTask();
            }
        }

        protected void StartUploadTask()
        {
            if (AutoCreateTargetTable)
            {
                logger.Info("Creating target table {0}.{1}.{2}",ServerName,DatabaseName,TableName);
                CreateTargetTable();
                TargetTableCreated = true;
            }

            if(Uploader == null)
            {
                Uploader = Task.Factory.StartNew(() => UploadTaskMain());
            }
            UploaderStarted = true;
        }


        protected void UploadTaskMain()
        {
            while (true)
            {
                try
                {
                    Upload();
                    Thread.Sleep(UploadIntervalSeconds * 1000);
                }
                catch(Exception e)
                {
                    logger.Error("Error uploading to the target table");
                    logger.Error(e);
                }
            }
        }


        protected void Upload()
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

                lock(EventsTable)
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
            logger.Info(String.Format("{0} rows written",numRows));
        }


        protected void CreateTargetTable()
        {
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = ConnectionString;
                conn.Open();

                DataTableTSQLAdapter adapter = new DataTableTSQLAdapter(eventsTable, conn)
                {
                    DestinationTableName = TableName
                };
                if (!adapter.CheckTableExists())
                {
                    adapter.CreateFromDataTable();
                }
            }
        }
    }
}
