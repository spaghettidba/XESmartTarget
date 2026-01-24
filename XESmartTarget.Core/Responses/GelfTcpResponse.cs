using System.Text;
using NLog;
using System.Data;
using XESmartTarget.Core.Utils;
using System.Net.Sockets;
using System.Net.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.SqlServer.XEvent.XELite;

namespace XESmartTarget.Core.Responses
{
    [Serializable]
    public class GelfTcpResponse : Response
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public string? ServerName { get; set; }
        public int Port { get; set; }
        public bool Encrypt { get; set; }
        public bool TrustServerCertificate { get; set; }

        protected DataTable EventsTable = new DataTable("events");
        private XEventDataTableAdapter? xeadapter;
        private JsonSerializerSettings jsonSettings;

        public GelfTcpResponse()
        {
            logger.Info(String.Format("Initializing Response of Type '{0}'", this.GetType().FullName));

            jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Converters = { new DataRowConverter() },
            };
        }

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
                if (!(EventsTable.Columns.Contains("host")))
                {
                    EventsTable.Columns.Add("host", typeof(string));
                }

                if (!(EventsTable.Columns.Contains("version")))
                {
                    EventsTable.Columns.Add("version", typeof(string));
                    EventsTable.Columns["version"]!.DefaultValue = "1.1";
                }
                
                if (!(EventsTable.Columns.Contains("short_message")))
                {
                    EventsTable.Columns.Add("short_message", typeof(string));
                }

                if (!(EventsTable.Columns.Contains("timestamp")))
                {
                    EventsTable.Columns.Add("timestamp", typeof(double));
                }

                foreach (DataRow dr in EventsTable.Rows)
                {
                    // populate host column
                    if (dr.Table.Columns.Contains("server_instance_name"))
                    {
                        dr.SetField("host", dr["server_instance_name"]);
                    }
                    else
                    {
                        dr.SetField("host", System.Environment.MachineName);
                    }

                    // populate the timestamp column
                    if (dr["collection_time"] != null)
                    {
                        dr.SetField("timestamp", (((DateTime)dr["collection_time"]).ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds);
                    } 
                    else
                    {
                        dr.SetField("timestamp", DateTime.Now.ToUniversalTime().Subtract(new DateTime(1970, 1, 1)).TotalSeconds);
                    }

                    // populate short_message column
                    if (dr["short_message"] == null)
                    {
                        dr.SetField("short_message", dr["message"] ?? "No message from XE session");
                    }

                    // check lengths since elasticsearch has a 32kb limit
                    foreach (DataColumn column in dr.Table.Columns)
                    {
                        if (dr[column.ColumnName]?.ToString()?.Length > 32766)
                        {
                            dr.SetField(column.ColumnName, ((string)dr[column.ColumnName]).Substring(0, 32766));
                        }
                    }

                    var rowJson = JsonConvert.SerializeObject(dr, Formatting.None, jsonSettings);
                    var byteList = new List<Byte>();
                    byteList.AddRange(Encoding.UTF8.GetBytes(rowJson));
                    byteList.Add(0x00);

                    using (TcpClient tcpClient = new TcpClient())
                    {
                        var connect = tcpClient.ConnectAsync(ServerName!, Port);
                        if (!(connect.Wait(500)))
                        {
                            logger.Error("Connection timed out");
                            throw new Exception("Connection timed out");
                        }

                        using (NetworkStream tcpStream = tcpClient.GetStream())
                        {
                            if (Encrypt)
                            {
                                SslStream? sslStream = null;
                                if (TrustServerCertificate)
                                {
                                    sslStream = new SslStream(tcpStream, false, delegate { return true; }, null);
                                }
                                else
                                {
                                    sslStream = new SslStream(tcpStream, false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                                }

                                try
                                {
                                    logger.Debug("Validating certificate");
                                    sslStream!.AuthenticateAsClient(ServerName!);
                                }
                                catch (AuthenticationException ex)
                                {
                                    logger.Error("Exception: {0}", ex.Message);
                                    if (ex.InnerException != null)
                                    {
                                        logger.Error("Inner exception: {0}", ex.InnerException.Message);
                                    }

                                    logger.Error("Authentication failed - closing the connection.");
                                    sslStream.Close();
                                    tcpClient.Close();
                                    return;
                                }

                                try
                                {
                                    sslStream.Write(byteList.ToArray(), 0, byteList.Count);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error("Exception: {0}", ex.Message);
                                    if (ex.InnerException != null)
                                    {
                                        logger.Error("Inner exception: {0}", ex.InnerException.Message);
                                    }

                                    logger.Error("Could not write bytes to stream.");
                                    sslStream.Close();
                                    tcpClient.Close();
                                    return;
                                }

                                sslStream.Close();
                            }
                            else
                            {
                                try
                                {
                                    tcpStream.Write(byteList.ToArray(), 0, byteList.Count);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error("Exception: {0}", ex.Message);
                                    if (ex.InnerException != null)
                                    {
                                        logger.Error("Inner exception: {0}", ex.InnerException.Message);
                                    }

                                    logger.Error("Could not write bytes to stream.");
                                    tcpStream.Close();
                                    tcpClient.Close();
                                    return;
                                }
                                
                                tcpStream.Close();
                            }

                            tcpClient.Close();
                        }
                    }
                }

                EventsTable.Clear();
            }
        }

        static bool ValidateServerCertificate(Object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            logger.Debug("Validating the server certificate.");
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            logger.Error("Certificate error: {0}", sslPolicyErrors);

            return false;
        }

        // json converter attributed to dbc on stackoverflow
        // https://stackoverflow.com/a/33400729
        class DataRowConverter : JsonConverter<DataRow>
        {
            public override DataRow ReadJson(JsonReader reader, Type objectType, DataRow? existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException(string.Format("{0} is only implemented for writing.", this));
            }

            public override void WriteJson(JsonWriter writer, DataRow? row, JsonSerializer serializer)
            {
                if (row == null)
                    throw new JsonSerializationException("row is null");
                    
                var table = row.Table;
                if (table == null)
                    throw new JsonSerializationException("no table");
                var contractResolver = serializer.ContractResolver as DefaultContractResolver;

                writer.WriteStartObject();
                foreach (DataColumn col in table.Columns)
                {
                    var value = row[col];

                    if (serializer.NullValueHandling == NullValueHandling.Ignore && (value == null || value == DBNull.Value))
                        continue;

                    writer.WritePropertyName(contractResolver != null ? contractResolver.GetResolvedPropertyName(col.ColumnName) : col.ColumnName);
                    serializer.Serialize(writer, value);
                }
                writer.WriteEndObject();
            }
        }

        public override object Clone()
        {
            GelfTcpResponse clone = (GelfTcpResponse)CloneBase();
            // Deep copy any reference type members here if necessary
            clone.EventsTable = new DataTable("events");
            clone.xeadapter = null; // Reset adapter for the clone
            return clone;
        }
    }
}
