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
            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options => ProcessTarget(options));
        }

        private static void ProcessTarget(Options options)
        {
            string version = string.Empty;
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            if (!string.IsNullOrEmpty(assembly.Location))
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                version = fvi.FileMajorPart.ToString() + "." + fvi.FileMinorPart.ToString() + "." + fvi.FileBuildPart.ToString();
            }
            else
                version = "Linux";

            // save the URI to a file and point configuration there
            string tempPath = Path.GetTempPath();
            if (string.IsNullOrEmpty(tempPath) && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                tempPath = "/tmp/QMonitor";

            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            /*
            if (!options.NoLogo)
            {
                Console.WriteLine(fvi.FileDescription + " " + version);
                Console.WriteLine(fvi.LegalCopyright + " - " + fvi.CompanyName);
                Console.WriteLine();
            }
            */

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
            bool isUri = Uri.TryCreate(options.ConfigurationFile, UriKind.Absolute, out outUri) && (outUri.Scheme == Uri.UriSchemeHttp || outUri.Scheme == Uri.UriSchemeHttps);

            if (isUri)
            {
                //password can be read from Windows Credentials
                //if it exists, otherwise execution proceeds with user passed uri
                try
                {
                    (string username, string password) = (null, null);
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        (username, password) = WindowsCredentialHelper.ReadCredential(outUri.OriginalString);
                    else
                        (username, password) = LinuxCredentialHelper.ReadCredential(outUri.OriginalString);

                    if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    {
                        var uriBuilder = new UriBuilder(outUri)
                        {
                            UserName = username,
                            Password = password
                        };

                        options.ConfigurationFile = uriBuilder.Uri.ToString();
                        Uri.TryCreate(options.ConfigurationFile, UriKind.Absolute, out outUri);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Couldn't retrieve Windows Credentials: {ex.Message}");
                }

                

                options.ConfigurationFile = Path.Combine(tempPath, $"{Guid.NewGuid()}.json");

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", $"XESmartTarget/{version} (XESmartTarget; copyright spaghettidba)");
                    try
                    {
                        if (!String.IsNullOrEmpty(outUri.UserInfo))
                        {
                            var byteArray = Encoding.ASCII.GetBytes(outUri.UserInfo);
                            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                        }
                        var response = client.GetAsync(outUri).GetAwaiter().GetResult();
                        if (response.IsSuccessStatusCode)
                        {
                            using (var fs = new FileStream(options.ConfigurationFile, FileMode.CreateNew))
                            {
                                response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                            }
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

            if (!File.Exists(options.ConfigurationFile))
            {
                logger.Error(String.Format("File not found: '{0}'", options.ConfigurationFile));
                return;
            }

            // parse key value pairs
            foreach (var kvp in options.GlobalVariables)
            {
                var pair = kvp.Split('=');
                TargetConfig.GlobalVariables.Add(pair[0], pair[1]);
            }

            TargetConfig config = TargetConfig.LoadFromFile(options.ConfigurationFile);

            // delete the file downloaded from URI
            if (deleteTempFile)
            {
                if (File.Exists(options.ConfigurationFile))
                {
                    File.Delete(options.ConfigurationFile);
                }
            }

            if (!String.IsNullOrEmpty(options.PreExecutionScript))
            {
                if (!File.Exists(options.PreExecutionScript))
                {
                    logger.Error(String.Format("File not found: '{0}'", options.PreExecutionScript));
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
                if (!File.Exists(options.PostExecutionScript))
                {
                    logger.Error(String.Format("File not found: '{0}'", options.PostExecutionScript));
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
                if (File.Exists(options.ConfigurationFile))
                {
                    File.Delete(options.ConfigurationFile);
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
