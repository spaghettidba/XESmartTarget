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
            ReadEvent(evt);

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
                Upload();
                Thread.Sleep(UploadIntervalSeconds * 1000);
            }
        }


        protected void Upload()
        {
            logger.Info("Writing XE data");

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


        private void PrepareDataTable()
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

        private void ReadEvent(PublishedEvent evt)
        {
            PrepareDataTable();
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

            if(!String.IsNullOrEmpty(Filter))
            {
                
                DataView dv = new DataView(tmpTab);
                dv.RowFilter = Filter;

                tmpTab.Rows.Add(row);

                lock (eventsTable)
                {
                    foreach (DataRow dr in dv.ToTable().Rows)
                    {
                        EventsTable.ImportRow(dr);
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
                        EventsTable.ImportRow(dr);
                    }
                }
            } 
            
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
