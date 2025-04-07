using CommandLine;
using NLog;
using NLog.Targets;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using XESmartTarget.Core.Config;
using XESmartTarget.Core.Utils;

namespace XESmartTarget
{
    class Program
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private static CancellationTokenSource source;

        static void Main(string[] args)
        {
            Console.WriteLine("Args count: " + args.Length);
            foreach (var a in args)
                Console.WriteLine("Arg: " + a);

            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options => ProcessTarget(options));
        }

        private static void ProcessTarget(Options options)
        {
            Console.WriteLine($"ProcessTarget invoked with: ConfigurationFile='{options.ConfigurationFile}', NoLogo={options.NoLogo}, Quiet={options.Quiet}, LogFile='{options.LogFile}', TimeoutSeconds={options.TimeoutSeconds}, PreExecutionScript='{options.PreExecutionScript}', PostExecutionScript='{options.PostExecutionScript}'");

            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileMajorPart.ToString() + "." + fvi.FileMinorPart.ToString() + "." + fvi.FileBuildPart.ToString();

            if (!options.NoLogo)
            {
                Console.WriteLine(fvi.FileDescription + " " + version);
                Console.WriteLine(fvi.LegalCopyright + " - " + fvi.CompanyName);
                Console.WriteLine();
            }


            if (options.Quiet)
            {
                // suppress writing to console
                if (LogManager.Configuration != null)
                {
                    LogManager.Configuration.RemoveTarget("console");
                    foreach (var lr in LogManager.Configuration.LoggingRules)
                    {
                        var consoleTarget = lr.Targets.Where(x => x.Name == "console").FirstOrDefault();
                        if (consoleTarget != null)
                        {
                            lr.Targets.Remove(consoleTarget);
                        }
                    }
                    LogManager.ReconfigExistingLoggers();
                }
            }

            if (LogManager.Configuration != null)
            {
                var target = (FileTarget)LogManager.Configuration.FindTargetByName("logfile");
                if (target != null)
                {
                    var pathToLog = options.LogFile;
                    Console.WriteLine($"Current LogFile option: '{pathToLog}'");
                    if (pathToLog == null)
                    {
                        pathToLog = Path.Combine(Environment.CurrentDirectory, "XESmartTarget.log");
                        Console.WriteLine($"LogFile is null, using default: '{pathToLog}'");
                    }
                    if (!Path.IsPathRooted(pathToLog))
                    {
                        pathToLog = Path.Combine(Environment.CurrentDirectory, pathToLog);
                        Console.WriteLine($"LogFile was relative, new full path: '{pathToLog}'");
                    }
                    target.FileName = pathToLog;

                    LogManager.ReconfigExistingLoggers();
                }
            }

            logger.Info(String.Format("Reading configuration from '{0}'", options.ConfigurationFile));

            // ******************************************
            // check the configuration file: is it a URI?
            // ******************************************
            Uri outUri;
            bool deleteTempFile = false;
            Console.WriteLine($"Attempting to create a URI from ConfigurationFile='{options.ConfigurationFile}'");
            bool isUri = Uri.TryCreate(options.ConfigurationFile, UriKind.Absolute, out outUri)
               && (outUri.Scheme == Uri.UriSchemeHttp || outUri.Scheme == Uri.UriSchemeHttps);
            Console.WriteLine($"Uri.TryCreate => {isUri}. outUri='{outUri}'");

            if (isUri)
            {
                Console.WriteLine($"Detected HTTP/HTTPS scheme: {outUri.Scheme}. UserInfo: {outUri.UserInfo}");
                //password can be read from Windows Credentials
                //if it exists, otherwise execution proceeds with user passed uri
                try
                {
                    (string username, string password) = (null, null);
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        (username, password) = WindowsCredentialHelper.ReadCredential(outUri.OriginalString);
                    else
                        (username, password) = LinuxCredentialHelper.ReadCredential(outUri.OriginalString);

                    Console.WriteLine($"Credentials retrieved? username: '{username}', password length: '{(password == null ? 0 : password.Length)}'");

                    if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    {
                        var uriBuilder = new UriBuilder(outUri)
                        {
                            UserName = username,
                            Password = password
                        };

                        options.ConfigurationFile = uriBuilder.Uri.ToString();
                        Console.WriteLine($"Rebuilt URI with credentials: {options.ConfigurationFile}");
                        Uri.TryCreate(options.ConfigurationFile, UriKind.Absolute, out outUri);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Couldn't retrieve Windows Credentials: {ex.Message}");
                }

                // save the URI to a file and point configuration there
                string tempPath = Path.GetTempPath();
                if (string.IsNullOrEmpty(tempPath) && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    tempPath = "/tmp/QMonitor";

                if (!Directory.Exists(tempPath))
                    Directory.CreateDirectory(tempPath);

                options.ConfigurationFile = Path.Combine(tempPath, $"{Guid.NewGuid()}.json");
                Console.WriteLine($"Downloading configuration to temp file: {options.ConfigurationFile}");

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", $"XESmartTarget/{version} (XESmartTarget; copyright spaghettidba)");
                    try
                    {
                        if (!String.IsNullOrEmpty(outUri.UserInfo))
                        {
                            var byteArray = Encoding.ASCII.GetBytes(outUri.UserInfo);
                            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                            Console.WriteLine("Using Basic Auth from outUri.UserInfo");
                        }
                        var response = client.GetAsync(outUri).GetAwaiter().GetResult();
                        Console.WriteLine($"HTTP response: {response.StatusCode} - {response.ReasonPhrase}");
                        if (response.IsSuccessStatusCode)
                        {
                            using (var fs = new FileStream(options.ConfigurationFile, FileMode.CreateNew))
                            {
                                response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                            }
                            Console.WriteLine($"File successfully downloaded to '{options.ConfigurationFile}'");
                        }
                        else
                        {
                            throw new ArgumentException($"URL returned {response.StatusCode} : {response.ReasonPhrase}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Unable to download configuration from URI: '{options.ConfigurationFile}'. Error: {ex.Message}");
                        return;
                    }
                }
                deleteTempFile = true;
            }

            Console.WriteLine($"Checking if file exists: '{options.ConfigurationFile}'");
            if (!File.Exists(options.ConfigurationFile))
            {
                logger.Error(String.Format("File not found: '{0}'", options.ConfigurationFile));
                Console.WriteLine("Run XESmartTarget -? for help.");
                return;
            }

            // parse key value pairs
            Console.WriteLine($"Parsing GlobalVariables (count: {options.GlobalVariables.Count()} )");
            foreach (var kvp in options.GlobalVariables)
            {
                Console.WriteLine($"GlobalVariable: '{kvp}'");
                var pair = kvp.Split('=');
                TargetConfig.GlobalVariables.Add(pair[0], pair[1]);
            }

            Console.WriteLine($"Loading TargetConfig from '{options.ConfigurationFile}'");
            TargetConfig config = TargetConfig.LoadFromFile(options.ConfigurationFile);

            // delete the file downloaded from URI
            if (deleteTempFile)
            {
                Console.WriteLine($"deleteTempFile=true, checking if '{options.ConfigurationFile}' still exists before deleting");
                if (File.Exists(options.ConfigurationFile))
                {
                    File.Delete(options.ConfigurationFile);
                    Console.WriteLine($"Temp file '{options.ConfigurationFile}' deleted.");
                }
            }

            if (!String.IsNullOrEmpty(options.PreExecutionScript))
            {
                Console.WriteLine($"PreExecutionScript is set: '{options.PreExecutionScript}'");
                if (!File.Exists(options.PreExecutionScript))
                {
                    logger.Error(String.Format("File not found: '{0}'", options.PreExecutionScript));
                    Console.WriteLine("Run XESmartTarget -? for help.");
                    return;
                }
                else
                {
                    foreach (var t in config.Target)
                    {
                        t.PreExecutionScript = options.PreExecutionScript;
                    }

                }
            }

            if (!String.IsNullOrEmpty(options.PostExecutionScript))
            {
                Console.WriteLine($"PostExecutionScript is set: '{options.PostExecutionScript}'");
                if (!File.Exists(options.PostExecutionScript))
                {
                    logger.Error(String.Format("File not found: '{0}'", options.PostExecutionScript));
                    Console.WriteLine("Run XESmartTarget -? for help.");
                    return;
                }
                else
                {
                    foreach (var t in config.Target)
                    {
                        t.PreExecutionScript = options.PostExecutionScript;
                    }

                }
            }


            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                logger.Info("Received shutdown signal...");

                foreach (var t in config.Target)
                {
                    t.Stop();
                }
                source.CancelAfter(TimeSpan.FromSeconds(10)); // give a 10 seconds cancellation grace period 
            };

            logger.Info("Starting Target");

            var tasks = new List<Task>();

            if (config != null)
            {
                foreach (var targ in config.Target)
                {
                    Task t = processTargetAsync(targ);
                    tasks.Add(t);
                }
                Task.WaitAll(tasks.ToArray(), options.TimeoutSeconds > 0 ? options.TimeoutSeconds * 1000 : -1);
            }
            else
            {
                logger.Error("No Targets found in the source configuration file");
            }

            // delete the file downloaded from URI
            if (deleteTempFile)
            {
                Console.WriteLine($"Final check to deleteTempFile: '{options.ConfigurationFile}'");
                if (File.Exists(options.ConfigurationFile))
                {
                    File.Delete(options.ConfigurationFile);
                    Console.WriteLine($"Temp file '{options.ConfigurationFile}' deleted (final cleanup).");
                }
            }

            logger.Info("Target process ended");
        }


        public static async Task processTargetAsync(XESmartTarget.Core.Target target)
        {
            source = new CancellationTokenSource();
            source.Token.Register(CancelNotification);
            var completionSource = new TaskCompletionSource<object>();
            source.Token.Register(() => completionSource.TrySetCanceled());
            var task = Task.Factory.StartNew(() => target.Start(), source.Token);
            await Task.WhenAny(task, completionSource.Task);
        }

        public static void CancelNotification()
        {
            logger.Info("Shutdown complete.");
        }

    }

    class Options
    {
        [Option('F', "File", Default = "XESmartTarget.json", HelpText = "Configuration file")]
        public string ConfigurationFile { get; set; }

        [Option('N', "NoLogo", Default = false, HelpText = "Hides copyright banner at startup")]
        public bool NoLogo { get; set; }

        [Option('Q', "Quiet", Default = false, HelpText = "Prevents output to console")]
        public bool Quiet { get; set; }

        [Option('G', "GlobalVariables", HelpText = "Global variables in the form key1=value1 key2=value2")]
        public IEnumerable<string> GlobalVariables { get; set; }

        [Option('L', "LogFile", HelpText = "Log File")]
        public string LogFile { get; set; }

        [Option('T', "Timeout", HelpText = "Timeout in seconds")]
        public int TimeoutSeconds { get; set; } = -1;

        [Option('P', "PreExecutionScript", HelpText = "Pre-Execution Script File")]
        public string PreExecutionScript { get; set; }

        [Option('O', "PostExecutionScript", HelpText = "Post-Execution Script File")]
        public string PostExecutionScript { get; set; }
    }
}
