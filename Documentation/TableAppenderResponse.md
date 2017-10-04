# TableAppenderResponse

This Response type is used to write Extended Events to a database table. The events are temporarily stored in memory before being written to the database at regular intervals.

The target table can be created manually upfront or you can let the `TableAppenderResponse` create a target table based on the fields and actions available in the events captured. The columns of the target table and the fields/actions of the events are mapped by name (case-sensitive).

## Properties

* `string` **ServerName**: Specified the SQL Server instance name where the target table for the Extended Events data is located.

* `string` **DatabaseName**: Specifies the name of the database that contains the target table.

* `string` **TableName**: Specifies the name of the target table.

* `string` **UserName**: User name to connect to the target server. When blank, Windows authentication will be used.

* `string` **Password**: Password on the target server. Only required when SQL Server authentication is used.

* `bool` **AutoCreateTargetTable**: When `true`, XESmartTarget will infer the definition of the target table from the columns captured in the Extended Events session. If the target table already exists, it will not be recreated.

* `int` **UploadIntervalSeconds**: Specifies the number of seconds XESmartTarget will keep the events in memory befory dumping them to the target table. The default is 10 seconds.

* `List<string>` **OutputColumns**: Specifies the list of columns to output from the events. XESmartTarget will capture in memory and write to the target table only the columns (fields or targets) that are present in this list. Fields and actions are matched in a case-sensitive manner.