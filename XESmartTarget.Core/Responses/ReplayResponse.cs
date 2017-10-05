using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.XEvent.Linq;
using System.Data.SqlClient;
using NLog;
using System.Data;
using System.Threading;
using System.Collections.Concurrent;

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
        public int DelaySeconds { get; set; } = 0;
        public int ReplayIntervalSeconds { get; set; } = 0;

        private DataTable eventsTable = new DataTable("events");
        private ConcurrentDictionary<int, ReplayWorker> ReplayWorkers = new ConcurrentDictionary<int, ReplayWorker>();

        private Task ReplayTask = null;

        public ReplayResponse()
        {

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
                    DataColumn cl_dt = new DataColumn("collection_time", typeof(DateTime));
                    eventsTable.Columns.Add(cl_dt);
                }


                //
                // Add Name column
                //
                if (!eventsTable.Columns.Contains("Name"))
                {
                    eventsTable.Columns.Add("Name", typeof(String));
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
                        DataColumn dc = eventsTable.Columns.Add(fld.Name, fld.Type);
                    }
                }

                foreach (PublishedAction act in evt.Actions)
                {
                    if (!eventsTable.Columns.Contains(act.Name))
                    {
                        DataColumn dc = eventsTable.Columns.Add(act.Name, act.Type);
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
                    row.SetField(fld.Name, fld.Value);
                }
            }

            foreach (PublishedAction act in evt.Actions)
            {
                if (row.Table.Columns.Contains(act.Name))
                {
                    row.SetField(act.Name, act.Value);
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


        public override void Process(PublishedEvent evt)
        {

            ReadEvent(evt);

            if (DelaySeconds == 0 && ReplayIntervalSeconds == 0)
            {
                // run replay synchronously
                Replay();
            }
            else
            {
                // start replay asynchronously
                if (ReplayTask == null)
                {
                    ReplayTask = Task.Factory.StartNew(() => ReplayTaskMain());
                }
            }

        }



        private void ReplayTaskMain()
        {
            Thread.Sleep(DelaySeconds * 1000);
            while (true)
            {
                Replay();
                Thread.Sleep(ReplayIntervalSeconds * 1000);
            }
        }

        private void Replay()
        {
            lock (eventsTable)
            {
                foreach (DataRow dr in eventsTable.Rows)
                {
                    string commandText = null;
                    if (dr["Name"].ToString() == "rpc_completed")
                    {
                        commandText = dr["statement"].ToString();
                    }
                    else if (dr["Name"].ToString() == "sql_batch_completed")
                    {
                        commandText = dr["batch_text"].ToString();
                    }
                    else
                    {
                        //ignore events not suitable for replay
                        logger.Debug(String.Format("Skipping event {0}", dr["Name"].ToString()));
                        return;
                    }

                    ReplayCommand command = new ReplayCommand() { CommandText = commandText };

                    if (dr.Table.Columns.Contains("database_name") && !String.IsNullOrEmpty((string)dr["database_name"]))
                    {
                        string dbname = dr["database_name"].ToString();
                        command.Database = dbname;
                    }

                    int session_id = -1;
                    if (dr.Table.Columns.Contains("session_id"))
                    {
                        session_id = Convert.ToInt32(dr["session_id"]);
                    }
                    else
                    {
                        throw new Exception("Unable to replay if session_id is not collected. Please add this action to your session.");
                    }

                    ReplayWorker rw = null;
                    if(ReplayWorkers.TryGetValue(session_id, out rw))
                    {
                        rw.AppendCommand(command);
                    }
                    else
                    {
                        rw = new ReplayWorker()
                        {
                            ServerName = ServerName,
                            UserName = UserName,
                            Password = Password,
                            DatabaseName = DatabaseName,
                            ReplayIntervalSeconds = ReplayIntervalSeconds,
                            StopOnError = StopOnError,
                            Name = session_id.ToString()
                        };
                        ReplayWorkers.TryAdd(session_id, rw);
                        rw.Start();
                        rw.AppendCommand(command);

                        logger.Info(String.Format("Started new Replay Worker for session_id {0}", session_id));
                    }


                }

                eventsTable.Rows.Clear();
            }
        }

        class ReplayCommand
        {
            public string CommandText { get; set; }
            public string Database { get; set; }
        }



        class ReplayWorker
        {
            private SqlConnection conn { get; set; }
            public string ServerName { get; set; }
            public string UserName { get; set; }
            public string Password { get; set; }
            public string DatabaseName { get; set; }
            public int ReplayIntervalSeconds { get; set; } = 0;
            public bool StopOnError { get; set; } = false;
            public string Name { get; set; }

            private bool stopped = false;
            private ConcurrentQueue<ReplayCommand> Commands = new ConcurrentQueue<ReplayCommand>();
            private Task runner;


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

            public void Start()
            {
                runner = Task.Factory.StartNew(() => Run());
            }

            public void Run()
            {
                if (conn == null)
                {
                    InitializeConnection();
                }
                while (!stopped)
                {
                    if(Commands.Count == 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    ReplayCommand cmd = null;
                    if(Commands.TryDequeue(out cmd))
                    {
                        ExecuteCommand(cmd);
                    }
                    Thread.Sleep(ReplayIntervalSeconds * 1000);
                }

            }

            private void ExecuteCommand(ReplayCommand command)
            {
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    conn.Open();
                }

                try
                {
                    logger.Trace(String.Format("Changing database to {0}", command.Database));
                    conn.ChangeDatabase(command.Database);

                    SqlCommand cmd = new SqlCommand(command.CommandText);
                    cmd.Connection = conn;
                    cmd.ExecuteNonQuery();
                    logger.Trace(String.Format("SUCCES - {0}", command.CommandText));
                }
                catch (SqlException e)
                {
                    if (StopOnError)
                    {
                        logger.Error(String.Format("Error: {0}", command.CommandText));
                        throw;
                    }
                    else
                    {
                        logger.Warn(String.Format("Error: {0}", command.CommandText));
                        logger.Warn(String.Format("Error: {0}", e.Message));
                        logger.Trace(e);
                    }
                }
            }

            public void Stop()
            {
                stopped = true;
            }

            public void AppendCommand(ReplayCommand cmd)
            {
                Commands.Enqueue(cmd);
            }

            public void AppendCommand(string commandText, string databaseName)
            {
                Commands.Enqueue(new ReplayCommand() { CommandText = commandText, Database = databaseName });
            }

        }

    }
}
