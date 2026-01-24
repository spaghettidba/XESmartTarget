using System.Data;
using Microsoft.Data.SqlClient;
using NLog;
using XESmartTarget.Core.Responses;

namespace XESmartTarget.Core.Utils
{
    public class DataTableTSQLAdapter
    {

        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static string[] AllowedDataTypes = {
             "System.Byte[]"
            ,"System.Boolean"
            ,"System.DateTime"
            ,"System.DateTimeOffset"
            ,"System.Decimal"
            ,"System.UInt64"
            ,"System.Double"
            ,"System.Single"
            ,"System.Int64"
            ,"System.UInt32"
            ,"System.Int32"
            ,"System.UInt16"
            ,"System.Int16"
            ,"System.SByte"
            ,"System.String"
            ,"System.Byte"
            ,"System.Guid"
        };


        private SqlConnection _connection = null!;
        public SqlConnection Connection
        {
            get { return _connection; }
            set { _connection = value; }
        }

        private SqlTransaction? _transaction;
        public SqlTransaction? Transaction
        {
            get { return _transaction; }
            set { _transaction = value; }
        }

        private string _tableName = string.Empty;
        public string DestinationTableName
        {
            get { return _tableName; }
            set { _tableName = value; }
        }

        private DataTable Table { get; set; }
        public int BatchSize { get; set; } = 50000;
        public int QueryTimeout { get; set; } = 0;

        public DataTableTSQLAdapter(DataTable table) {
            Table = table;
        }

        public DataTableTSQLAdapter(DataTable table, SqlConnection connection) : this(table, connection, null!) { }

        public DataTableTSQLAdapter(DataTable table, SqlConnection connection, SqlTransaction? transaction)
        {
            _connection = connection;
            _transaction = transaction;
            Table = table;
        }

        public void Create()
        {
            Create(null!);
        }

        public void Create(int numKeys)
        {
            int[] primaryKeys = new int[numKeys];
            for (int i = 0; i < numKeys; i++)
            {
                primaryKeys[i] = i;
            }
            Create(primaryKeys);
        }

        public void Create(int[]? primaryKeys)
        {
            string sql = GetCreateSQL(primaryKeys!);

            SqlCommand cmd;
            if (_transaction != null && _transaction.Connection != null)
                cmd = new SqlCommand(sql, _connection, _transaction);
            else
                cmd = new SqlCommand(sql, _connection);

            cmd.ExecuteNonQuery();
        }


        public bool CheckTableExists()
        {
            string sql = "SELECT ISNULL(OBJECT_ID(@ObjName),-1) AS ObjId;";

            SqlCommand cmd;
            if (_transaction != null && _transaction.Connection != null)
                cmd = new SqlCommand(sql, _connection, _transaction);
            else
                cmd = new SqlCommand(sql, _connection);

            SqlParameter prm = cmd.CreateParameter();
            prm.Direction = ParameterDirection.Input;
            prm.DbType = DbType.String;
            prm.Size = 128;
            prm.Value = DestinationTableName;
            prm.ParameterName = "@ObjName";
            cmd.Parameters.Add(prm);

            int result = (int)(cmd.ExecuteScalar() ?? -1);
            return result != -1;
        }


        public void CreateFromDataTable()
        {

            string sql = GetCreateFromDataTableSQL();

            SqlCommand cmd;
            if (_transaction != null && _transaction.Connection != null)
                cmd = new SqlCommand(sql, _connection, _transaction);
            else
                cmd = new SqlCommand(sql, _connection);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch(Exception e)
            {
                logger.Error(sql);
                logger.Error(e);
                throw;
            }
        }
        

        private string GetCreateSQL(int[] primaryKeys)
        {
            string tableName = _tableName;
            if (tableName.IndexOf('[') < 0)
            {
                tableName = "[" + tableName + "]";
            }
            string sql = "CREATE TABLE " + tableName + " (\n";

            // columns
            foreach (DataRow column in Table.Rows)
            {
                if (!(Table.Columns.Contains("IsHidden") && (bool)column["IsHidden"]))
                {
                    sql += "\t[" + column["ColumnName"]?.ToString() + "] " + SQLGetType(column);

                    if (Table.Columns.Contains("AllowDBNull") && (bool)column["AllowDBNull"] == false)
                        sql += " NOT NULL";

                    sql += ",\n";
                }
            }
            sql = sql.TrimEnd(new char[] { ',', '\n' }) + "\n";

            // primary keys
            string pk = ", CONSTRAINT PK_" + tableName + " PRIMARY KEY CLUSTERED (";
            bool hasKeys = (primaryKeys != null && primaryKeys.Length > 0);
            if (hasKeys)
            {
                // user defined keys
                foreach (int key in primaryKeys!)
                {
                    pk += Table.Rows[key]["ColumnName"]?.ToString() + ", ";
                }
            }
            else
            {
                // check schema for keys
                string keys = string.Join(", ", GetPrimaryKeys());
                pk += keys;
                hasKeys = keys.Length > 0;
            }
            pk = pk.TrimEnd(new char[] { ',', ' ', '\n' }) + ")\n";
            if (hasKeys) sql += pk;

            sql += ")";

            return sql;
        }


        private string GetCreateFromDataTableSQL()
        {
            string tableName = _tableName;
            if (tableName.IndexOf('[') < 0)
            {
                tableName = "[" + tableName + "]";
            }
            string sql = "CREATE TABLE " + tableName + " (\n";
            // columns
            foreach (DataColumn column in Table.Columns)
            {
                bool addThisColumn = true;
                if(column.ExtendedProperties.ContainsKey("hidden")
                    && column.ExtendedProperties["hidden"] != null
                    && (bool)(column.ExtendedProperties["hidden"] ?? false))
                {
                    addThisColumn = false;
                }
                if (addThisColumn)
                {
                    sql += "[" + column.ColumnName + "] " + SQLGetType(column) + ",\n";
                }
            }
            sql = sql.TrimEnd(new char[] { ',', '\n' }) + "\n";
            // primary keys
            if (Table.PrimaryKey.Length > 0)
            {
                sql += "CONSTRAINT [PK_" + tableName + "] PRIMARY KEY CLUSTERED (";
                foreach (DataColumn column in Table.PrimaryKey)
                {
                    sql += "[" + column.ColumnName + "],";
                }
                sql = sql.TrimEnd(new char[] { ',' }) + ")";
            }
            sql += ")\n";

            return sql;
        }

        private string[] GetPrimaryKeys()
        {
            List<string> keys = new List<string>();

            foreach (DataRow column in Table.Rows)
            {
                if (Table.Columns.Contains("IsKey") && (bool)column["IsKey"])
                    keys.Add(column["ColumnName"]?.ToString() ?? string.Empty);
            }

            return keys.ToArray();
        }


        // Return T-SQL data type definition, based on schema definition for a column
        // Based off of http://msdn.microsoft.com/en-us/library/ms131092.aspx
        private string SQLGetType(object type, int columnSize, int numericPrecision, int numericScale)
        {

            switch (type.ToString())
            {
                case "System.Byte[]":
                    return "VARBINARY(MAX)";

                case "System.Boolean":
                    return "BIT";

                case "System.DateTime":
                    return "DATETIME";

                case "System.DateTimeOffset":
                    return "DATETIMEOFFSET";

                case "System.Decimal":
                    if (numericPrecision != -1 && numericScale != -1)
                        return "DECIMAL(" + numericPrecision + "," + numericScale + ")";
                    else
                        return "DECIMAL";

                case "System.UInt64":
                    return "DECIMAL(20)";

                case "System.Double":
                    return "FLOAT";

                case "System.Single":
                    return "REAL";

                case "System.Int64":
                case "System.UInt32":
                    return "BIGINT";

                case "System.Int32":
                case "System.UInt16":
                    return "INT";

                case "System.Int16":
                case "System.SByte":
                    return "SMALLINT";

                case "System.String":
                    return "NVARCHAR(" + ((columnSize == -1 || columnSize > 8000) ? "MAX" : columnSize.ToString()) + ")";

                case "System.Byte":
                    return "TINYINT";

                case "System.Guid":
                    return "UNIQUEIDENTIFIER";

                default:
                    return "NVARCHAR(" + ((columnSize == -1 || columnSize > 8000) ? "MAX" : columnSize.ToString()) + ")";
                    //throw new Exception(type.ToString() + " not implemented.");
            }
        }

        // Overload based on row from schema table
        private string SQLGetType(DataRow schemaRow)
        {
            int numericPrecision, numericScale;

            if (!int.TryParse(schemaRow["NumericPrecision"]?.ToString(), out numericPrecision))
            {
                numericPrecision = -1;
            }
            if (!int.TryParse(schemaRow["NumericScale"]?.ToString(), out numericScale))
            {
                numericScale = -1;
            }

            return SQLGetType(schemaRow["DataType"],
                                int.Parse(schemaRow["ColumnSize"]?.ToString() ?? "-1"),
                                numericPrecision,
                                numericScale);
        }

        // Overload based on DataColumn from DataTable type
        private string SQLGetType(DataColumn column)
        {
            return SQLGetType(column.DataType, column.MaxLength, -1, -1);
        }

        private string SQLGetType(Type t)
        {
            return SQLGetType(t, -1, -1, -1);
        }


        public void WriteToServer()
        {
            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(Connection,
                                SqlBulkCopyOptions.KeepIdentity |
                                SqlBulkCopyOptions.FireTriggers |
                                SqlBulkCopyOptions.CheckConstraints |
                                SqlBulkCopyOptions.TableLock,
                                Transaction))
            {

                bulkCopy.DestinationTableName = GetSynonymBase(DestinationTableName);
                bulkCopy.BatchSize = BatchSize;
                bulkCopy.BulkCopyTimeout = QueryTimeout;

                foreach (string dbcol in GetColumns(DestinationTableName))
                {
                    if (Table.Columns.Contains(dbcol))
                    {
                        bulkCopy.ColumnMappings.Add(dbcol, dbcol);
                    }
                }

                bulkCopy.WriteToServer(Table);
            }
        }



        public int MergeToServer(List<OutputColumn> OutputColumns)
        {
            int result = -1;

            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(Connection,
                                SqlBulkCopyOptions.KeepIdentity |
                                SqlBulkCopyOptions.FireTriggers |
                                SqlBulkCopyOptions.CheckConstraints |
                                SqlBulkCopyOptions.TableLock,
                                Transaction))
            {
                string destination = GetSynonymBase(DestinationTableName);
                bulkCopy.DestinationTableName = "#tmpBCP";
                bulkCopy.BatchSize = BatchSize;
                bulkCopy.BulkCopyTimeout = QueryTimeout;

                foreach (string dbcol in GetColumns(DestinationTableName))
                {
                    if (Table.Columns.Contains(dbcol))
                    {
                        bulkCopy.ColumnMappings.Add(dbcol, dbcol);
                    }
                }

                using (SqlCommand cmd = Connection.CreateCommand())
                {
                    cmd.CommandText = String.Format("SELECT TOP(0) * INTO #tmpBCP FROM {0}", destination);
                    cmd.ExecuteNonQuery();
                }

                bulkCopy.WriteToServer(Table);

                // Merge data
                using (SqlCommand cmd = Connection.CreateCommand())
                {
                    string sql = @"
                        MERGE INTO {0} AS dest
                        USING #tmpBCP AS src
                        ON {1}
                        WHEN MATCHED THEN UPDATE
                            SET {2}
                        WHEN NOT MATCHED THEN INSERT ({3}) VALUES ({4});
                    ";

                    string _0 = destination;
                    string _1 = String.Join(" AND ", OutputColumns.Where(s => !(s is AggregatedOutputColumn) && !(s.Hidden)).Select(s => "(src." + s.Name + " = dest." + s.Name + " OR (src." + s.Name + " IS NULL AND dest." + s.Name + " IS NULL))"));
                    string _2 = String.Join(", ", OutputColumns.Where(s => s is AggregatedOutputColumn).Select(s => s.Alias + " = " + BuildMergeSetClause((AggregatedOutputColumn)s)));
                    string _3 = String.Join(", ", OutputColumns.Where(s => !s.Hidden).Select(s => s is AggregatedOutputColumn ? s.Alias : s.Name));
                    string _4 = String.Join(", ", OutputColumns.Where(s => !s.Hidden).Select(s => "src." + (s is AggregatedOutputColumn ? s.Alias : s.Name)));

                    sql = String.Format(sql, _0, _1, _2, _3, _4);

                    cmd.CommandText = sql;
                    result = cmd.ExecuteNonQuery();
                }

            }
            return result;

        }

        private string BuildMergeSetClause(AggregatedOutputColumn col)
        {
            string result = "";
            switch (col.Aggregation)
            {
                case AggregatedOutputColumn.AggregationType.Max:
                    result = "CASE WHEN src." + col.Alias + " > dest." + col.Alias + " OR dest." + col.Alias + " IS NULL THEN src." + col.Alias + " ELSE dest." + col.Alias + " END";
                    break;
                case AggregatedOutputColumn.AggregationType.Min:
                    result = "CASE WHEN src." + col.Alias + " < dest." + col.Alias + " OR dest." + col.Alias + " IS NULL THEN src." + col.Alias + " ELSE dest." + col.Alias + " END";
                    break;
                case AggregatedOutputColumn.AggregationType.Avg:
                    // this is not really an average, but once the previous value
                    // is already aggregated, there is not much that can be done
                    result = "(ISNULL(dest." + col.Alias + ",src." + col.Alias + ") + src." + col.Alias + ") / 2";
                    break;
                case AggregatedOutputColumn.AggregationType.Sum:
                case AggregatedOutputColumn.AggregationType.Count:
                    result = "ISNULL(dest." + col.Alias + ",0) + ISNULL(src." + col.Alias + ",0)";
                    break;
            }
            return result;
        }


        private IEnumerable<string> GetColumns(string TableName)
        {
            string qry = @"
                SELECT TOP(0) * FROM {0};
            ";

            SqlCommand cmd = new SqlCommand(String.Format(qry, TableName), Connection) { CommandTimeout = 600 };


            DataSet ds = new DataSet();
            SqlDataAdapter da = new SqlDataAdapter(cmd);
            da.Fill(ds);
            DataTable data = ds.Tables[0];

            List<string> results = new List<string>();
            foreach (DataColumn col in data.Columns)
            {
                results.Add(col.ColumnName);
            }
            return results;
        }


        private string GetSynonymBase(string ObjectName)
        {
            string result = ObjectName;

            string qry = @"
                SELECT base_object_name
                FROM sys.synonyms
                WHERE name = @ObjName
            ";

            SqlCommand cmd = new SqlCommand(qry, Connection) { CommandTimeout = 600 };

            SqlParameter prm = cmd.CreateParameter();
            prm.Direction = ParameterDirection.Input;
            prm.DbType = DbType.String;
            prm.Size = 128;
            prm.Value = ObjectName;
            prm.ParameterName = "@ObjName";
            cmd.Parameters.Add(prm);

            string? sqlResult = (string?)cmd.ExecuteScalar();
            if(sqlResult != null)
            {
                result = sqlResult;
            }

            return result;
        }

    }
}
