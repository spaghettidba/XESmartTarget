using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
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

        public List<string> OutputColumns
        {
            get
            {
                return new List<string>(_outputColumns.Select(l => l.Name));
            }
            // when setting outputcolumns, internal outputcolumns are updated too
            // nothing keeps them in sync expect this setter, so adding/removing/updating items
            // in the collection won't be replicated automatically
            // this is an acceptable limitation, given how parameters are usually set 
            // once, by means of a .json configuration file
            set
            {
                _outputColumns = new List<OutputColumn>();
                foreach (var o in value)
                {
                    var aggr = AggregatedOutputColumn.TryParse(o);
                    if (aggr != null)
                    {
                        _outputColumns.Add(aggr);
                        // look up the base column
                        if (aggr.BaseColumn != null && aggr.BaseColumn != "*")
                        {
                            // add the base column if not found
                            var item = _outputColumns.FirstOrDefault(col => col.Name == aggr.BaseColumn || col.Alias == aggr.BaseColumn);
                            if(item == null)
                            {
                                _outputColumns.Add(new OutputColumn() { Name = aggr.BaseColumn, Alias = aggr.BaseColumn, Hidden = true });
                            }
                        }
                    }
                    else
                    {
                        _outputColumns.Add(new OutputColumn(o));
                    }
                }
            }
        }

        // internal representation of output columns. The public facing collection is made of strings
        // but the internal representation requires additional properties that are stored in this collection
        protected List<OutputColumn> _outputColumns { get; set; } = new List<OutputColumn>(); 
        protected DataTable EventsTable { get => eventsTable; set => eventsTable = value; }

        protected Task Uploader;

        private XEventDataTableAdapter xeadapter;

        protected string ConnectionString
        {
            get
            {
                int ConnectionTimeout = 15;
                string s = "Server=" + SmartFormatHelper.Format(ServerName,Tokens) + ";";
                s += "Database=" + SmartFormatHelper.Format(DatabaseName,Tokens) + ";";
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

        protected bool UploaderStarted = false;
        protected bool TargetTableCreated = false;

        protected void Enqueue(PublishedEvent evt)
        {
            if(xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(eventsTable);
                xeadapter.Filter = this.Filter;
                // initialize the XE adapter to read only non aggregated columns
                xeadapter.OutputColumns = new List<OutputColumn>(this._outputColumns.Where(col => !(col is AggregatedOutputColumn)));
            }
            xeadapter.ReadEvent(evt);

            if(!UploaderStarted)
            {
                StartUploadTask();
            }
        }

        protected virtual void StartUploadTask()
        {
            // Let this method create the target table only if the member is 
            // an instance of this class
            //
            if (AutoCreateTargetTable && this.GetType().Name == "TableAppenderResponse")
            {
                logger.Info("Creating target table {0}.{1}.{2}", SmartFormatHelper.Format(ServerName,Tokens), SmartFormatHelper.Format(DatabaseName, Tokens), SmartFormatHelper.Format(TableName, Tokens));
                CreateTargetTable(eventsTable);
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


        protected virtual void Upload()
        {
            logger.Trace("Writing XE data");

            int numRows;

            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = ConnectionString;
                conn.Open();

                if (!TargetTableCreated && AutoCreateTargetTable)
                {
                    CreateTargetTable(eventsTable);
                }

                lock(EventsTable)
                {
                    DataTableTSQLAdapter adapter = new DataTableTSQLAdapter(EventsTable, conn)
                    {
                        DestinationTableName = SmartFormatHelper.Format(TableName, Tokens)
                    };
                    adapter.WriteToServer();
                    numRows = EventsTable.Rows.Count;
                    EventsTable.Rows.Clear();
                }

            }
            logger.Info(String.Format("{0} rows written",numRows));
        }


        protected virtual void CreateTargetTable(DataTable data)
        {
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = ConnectionString;
                conn.Open();

                DataTableTSQLAdapter adapter = new DataTableTSQLAdapter(data, conn)
                {
                    DestinationTableName = SmartFormatHelper.Format(TableName, Tokens)
                };
                if (!adapter.CheckTableExists())
                {
                    adapter.CreateFromDataTable();
                }
            }
        }
    }
}

