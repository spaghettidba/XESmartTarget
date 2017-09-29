# XESmartTarget

XESmartTarget is a command line tool to help you working with SQL Server Extended Events. It offers the ability to perform custom actions in response to Events, in a way that is not possible by using the built-in targets.

While you are free to extend XESmartTarget with your own Response types, XESmartTarget does not require any coding. Instead, it can be configured with simple `.json` configuration files.

For instance, the following configuration file instructs XESmartTarget to connect to the server `(local)`, hook to the Extended Events session `test_session` and forward all the events of type `sql_batch_completed` to a Response of type `TableAppenderResponse`, which will insert all events every `10 seconds` into a table named `test_session_data` in the server `(local)`, Database `XESmartTargetTest`:

    {
        "Target": {
            "ServerName": "(local)",
            "SessionName": "test_session",
            "Responses": [
                {
                    "__type": "TableAppenderResponse",
                    "TargetServer": "(local)",
                    "TargetDatabase": "XESmartTargetTest",
                    "TargetTable": "test_session_data",
                    "AutoCreateTargetTable": true,
                    "UploadIntervalSeconds": 10,
                    "Events": [
                        "sql_batch_completed"
                    ]
                }
            ]
        }
    }

Here is the output it produces: 
![Screenshot 1](https://github.com/spaghettidba/XESmartTarget/blob/master/Images/Screenshot1.png?raw=true "Screenshot")

And here is the output it produces in the database:
![Screenshot 2](https://github.com/spaghettidba/XESmartTarget/blob/master/Images/Screenshot2.png?raw=true "Screenshot")

XESmartTarget is the simplest way to write Extended Events data to a database table.