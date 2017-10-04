# ReplayResponse

This Response type can be used to replay execution related events to a target SQL Server instance. The events that you can replay are of the type `sql_batch_completed` and `rpc_completed`: all other events are ignored.

## Properties

* `string` **ServerName**: Name of the target SQL Server instance

* `string` **UserName**: User name to use for SQL Server authentication. When blank, Windows authentication will be used instead.

* `string` **Password**: Password for SQL Server authentication. Only needed when User Name is specified.

* `string` **DatabaseName**: Name of the initial catalog to connect to. Statements will be replayed by changing database to the same database where the event was originally captured, so this property only controls the initial database to connect to.

* `bool` **StopOnError**: When `true`, stops the replay when the first error is encountered. When `false`, pipes the error messages to the log and to the console output and proceeds with the replay.
