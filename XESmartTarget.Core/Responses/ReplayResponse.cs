using Microsoft.SqlServer.XEvent.XELite;
using Microsoft.Data.SqlClient;
using NLog;
using System.Data;
using System.Collections.Concurrent;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class ReplayResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private SqlConnectionInfo ConnectionInfo { get; set; } = new();

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
            get => ConnectionInfo.ConnectTimeout ?? 15;   
            set => ConnectionInfo.ConnectTimeout = value;
        }
        public bool TrustServerCertificate
        {
            get => ConnectionInfo.TrustServerCertificate;  
            set => ConnectionInfo.TrustServerCertificate = value;
        }

        public bool StopOnError { get; set; }
        public int DelaySeconds { get; set; } = 0;
        public int ReplayIntervalSeconds { get; set; } = 0;

        private DataTable eventsTable = new DataTable("events");
        private ConcurrentDictionary<int, ReplayWorker> ReplayWorkers = new ConcurrentDictionary<int, ReplayWorker>();
        private XEventDataTableAdapter xeadapter;

        private Task ReplayTask = null;

        public ReplayResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));
        }

        public override void Process(IXEvent xevent)
        {
            if (xeadapter == null)
            {
                xeadapter = new XEventDataTableAdapter(eventsTable);
                xeadapter.Filter = this.Filter;
                xeadapter.OutputColumns = new List<OutputColumn>();
            }
            xeadapter.ReadEvent(xevent);

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
                    if (dr["name"].ToString() == "rpc_completed")
                    {
                        commandText = dr["statement"].ToString();
                    }
                    else if (dr["name"].ToString() == "sql_batch_completed")
                    {
                        commandText = dr["batch_text"].ToString();
                    }
                    else
                    {
                        //ignore events not suitable for replay
                        logger.Debug(String.Format("Skipping event {0}", dr["name"].ToString()));
                        continue;
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
                    if (ReplayWorkers.TryGetValue(session_id, out rw))
                    {
                        rw.AppendCommand(command);
                    }
                    else
                    {
                        rw = new ReplayWorker()
                        {
                            ConnectionInfo = ConnectionInfo,
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

            public int ReplayIntervalSeconds { get; set; } = 0;
            public bool StopOnError { get; set; } = false;
            public string Name { get; set; }

            private bool stopped = false;
            private ConcurrentQueue<ReplayCommand> Commands = new ConcurrentQueue<ReplayCommand>();
            private Task runner;
            private void InitializeConnection()
            {
                logger.Info(String.Format("Connecting to server {0} for replay...", ConnectionInfo.ServerName));
                string connString = ConnectionInfo.ConnectionString;
                conn = new SqlConnection(connString);
                conn.Open();
                logger.Info("Connected");
            }
            public SqlConnectionInfo ConnectionInfo { get; set; }

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
                    if (Commands.Count == 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    ReplayCommand cmd = null;
                    if (Commands.TryDequeue(out cmd))
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
                    if (!String.IsNullOrEmpty(command.Database))
                    {
                        logger.Trace(String.Format("Changing database to {0}", command.Database));
                        conn.ChangeDatabase(command.Database);
                    }

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

        public override object Clone()
        {
            ReplayResponse clone = (ReplayResponse)CloneBase();
            // deep clone of all complex properties
            clone.ConnectionInfo = new SqlConnectionInfo()
            {
                ServerName = this.ConnectionInfo.ServerName,
                DatabaseName = this.ConnectionInfo.DatabaseName,
                UserName = this.ConnectionInfo.UserName,
                Password = this.ConnectionInfo.Password,
                ConnectTimeout = this.ConnectionInfo.ConnectTimeout,
                TrustServerCertificate = this.ConnectionInfo.TrustServerCertificate
            };
            return clone;
        }
    }
}
