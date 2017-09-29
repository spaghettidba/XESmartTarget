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

        public string TargetServer { get; set; }
        public string TargetDatabase { get; set; }
        public string TargetTable { get; set; }
        public bool AutoCreateTargetTable { get; set; }
        public int UploadIntervalSeconds { get; set; } = 10;
        public string OutputColumns { get; set; } // This is going to be used as the list of columns to output to the database table
        protected DataTable EventsTable { get => eventsTable; set => eventsTable = value; }

        protected Task Uploader;

        protected string ConnectionString
        {
            get
            {
                int ConnectionTimeout = 15;
                string s = String.Format("Server={0};Database={1};Integrated Security=True;Connect Timeout={2}", TargetServer, TargetDatabase, ConnectionTimeout);
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
            // TODO: Translate this comment
            // Fare attenzione a come si caricano gli eventi in questo punto
            // Se si tratta di un solo tipo di evento, le colonne saranno le stesse
            // Altrimenti sarà difficile attribuire le stesse colonne ad eventi diversi
            // Bisognerà anche aggiungere una lista delle colonne da salvare e matcharle
            // Con le colonne della tabella nel db. Posso anche aggiungere delle colonne fittizie
            // come il nome dell'evento, l'ora in cui si è registrato e cose così
            // Meglio usare diverse DataTable per diversi eventi?
            // Come gestisco la concorrenza dei thread? Meglio accodare in modo seriale
            // Se voglio mandare eventi diversi su tabelle diverse, farò in modo di aggiungere
            // Response di tipo diverso per eventi diversi
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
                logger.Trace("Creating target table {0}.{1}.{2}",TargetServer,TargetDatabase,TargetTable);
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
                        DestinationTableName = TargetTable
                    };
                    adapter.WriteToServer();
                    numRows = EventsTable.Rows.Count;
                    EventsTable.Rows.Clear();
                }

            }
            logger.Trace(String.Format("{0} rows written",numRows));
        }


        private void PrepareDataTable()
        {
            lock (eventsTable)
            {
                //
                // Add Collection Time column
                //
                if (!eventsTable.Columns.Contains("collection_time"))
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
                if (!eventsTable.Columns.Contains("Name"))
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
                    if (!eventsTable.Columns.Contains(fld.Name))
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
                    if (!eventsTable.Columns.Contains(act.Name))
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

            DataRow row = eventsTable.NewRow();
            row.SetField("Name", evt.Name);

            foreach (PublishedEventField fld in evt.Fields)
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

            foreach (PublishedAction act in evt.Actions)
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

            lock (eventsTable)
            {
                eventsTable.Rows.Add(row);
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
                    DestinationTableName = TargetTable
                };
                if (!adapter.CheckTableExists())
                {
                    adapter.CreateFromDataTable();
                }
            }
        }
    }
}
