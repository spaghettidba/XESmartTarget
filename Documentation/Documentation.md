# Documentation

## Installation

XESmartTarget can be installed by running the `.msi` setup kit that you can download from the [releases page](https://github.com/spaghettidba/XESmartTarget/releases). 

XESmartTarget runs 
as a client application for an Extended Events session running on a SQL Server instance, so it does not need to be installed on the SQL Server machine.

The default installation path is `c:\Program Files(x86)\XESmartTarget`

## Usage

XESmartTarget offers the ability to set up complex actions in response to Extended Events captured in sessions, without writing a single line of code. 

Instead, XESmartTarget is based on `.JSON` configuration files that instruct the tool on what to do with the events in the stream.

XESmartTarget is a Command Line application, so don't expect a GUI. All the configuration you need is inside the `.JSON` files, so the only parameter you need to provide to XESmartTarget is the path to the configuration file itself. 

The syntax is the following:

```cmd
XESmartTarget -F <path to the .JSON configuration file>
```

## .JSON Configuration Files

The general structure of a `.JSON` configuration file is the following:

```json
{
    "Target": {
        "ServerName": "server to monitor, where the session is running",
        "SessionName": "name of the session",
        "Responses": [
            // List of Responses
        ]
    }
}
```

The Target object contain a set of attributes that can control the behaviour of the Target itself: 

* `string` **ServerName**: Server to monitor, where the Extended Events session is running.

* `string` **SessionName**: Name of the Extended Events session to attach to. You can monitor a single session with an instance of XESmartTarget: in case you need to perform action on multiple sessions, run an additional instance of XESmartTarget, with its own configuration file.

* `string` **UserName**: User Name for SQL Server authentication. When blank, Windows Authentication will be used.

* `string` **Password**: Password for SQL Server authentication. Only needed when connecting with SQL Server authentication.

* `string` **DatabaseName**: Name of the initial database to connect to.


The list of responses can include zero or more `Response` objects, each to be configured by specifying values for their public members. 

The first attribute that you need to specify is the name of the Response class that you want to work with. You can do so in the `"__type"` attribute.

Here is an example:

```json
 "Responses": [
    {
        // class name of the Response subclass
        "__type": "TableAppenderResponse",
        // attribute 1
        "ServerName": "(local)",
        // attribute 2
        "DatabaseName": "XESmartTargetTest",
        // attribute 3
        "TableName": "test_session_data"
    }
 ]
```

The attributes that you can specify depend on the members available on the `Response` subclass that you are working with. Some attributes instead are common to all Response implementations:

* `string` **Filter**: you can specify a filter expression by using this attribute. The filter expression is in the same form that you would use in a SQL query. For example, a valid example looks like this: 
<BR>`duration > 10000 AND cpu_time > 10000`

* `List<string>` **Events**: each `Response` can be limited to processing specific events, while ignoring all the other ones. When this attribute is omitted, all events are processed.

Here follows the list of the available `Response` subclasses available at the moment, each with the description of the members that you can configure.

* [TableAppenderReponse](./TableAppenderResponse.md)
* [ReplayReponse](./ReplayResponse.md)
* [EmailResponse](./EmailResponse.md)

## Logging

XESmartTarget uses `NLog`. The logging behaviour can be controlled by editing the file `NLOG.config` that you can find in the executable's installation folder.

The output on the console window is also controlled by the `NLOG.config` file, you just need to control the `console` target in the `<targets>` section of the config file.