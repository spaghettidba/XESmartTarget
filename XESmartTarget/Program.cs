using CommandLine;
using CommandLine.Text;
using NLog;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XESmartTarget.Core;
using XESmartTarget.Core.Config;

namespace XESmartTarget
{
    class Program
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private static CancellationTokenSource source;

        static void Main(string[] args)
        {
#if DEBUG
            if (args.Length == 0)
                args = new string[] { "--File", @"c:\temp\sample.json" };
#endif
            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options => ProcessTarget(options));
        }

        private static void ProcessTarget(Options options)
        {
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
                    if (pathToLog == null)
                    {
                        pathToLog = Path.Combine(Environment.CurrentDirectory, "XESmartTarget.log");
                    }
                    if (!Path.IsPathRooted(pathToLog))
                    {
                        pathToLog = Path.Combine(Environment.CurrentDirectory, pathToLog);
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
            if (Uri.TryCreate(options.ConfigurationFile, UriKind.Absolute, out outUri)
               && (outUri.Scheme == Uri.UriSchemeHttp || outUri.Scheme == Uri.UriSchemeHttps))
            {
                // save the URI to a file and point configuration there
                options.ConfigurationFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
                using (var client = new HttpClient())
                {
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
                    catch (Exception)
                    {
                        logger.Error($"Unable to download configuration from URI: '{options.ConfigurationFile}'");
                        return;
                    }
                }
                deleteTempFile = true;
            }

            if (!File.Exists(options.ConfigurationFile))
            {
                logger.Error(String.Format("File not found: '{0}'", options.ConfigurationFile));
                Console.WriteLine("Run XESmartTarget -? for help.");
                return;
            }

            // parse key value pairs
            foreach (var kvp in options.GlobalVariables)
            {
                var pair = kvp.Split('=');
                TargetConfig.GlobalVariables.Add(pair[0], pair[1]);
            }

            TargetConfig config = TargetConfig.LoadFromFile(options.ConfigurationFile);

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
                Task.WaitAll(tasks.ToArray(),options.TimeoutSeconds > 0 ? options.TimeoutSeconds * 1000 : -1);
            }
            else
            {
                logger.Error("No Targets found in the source configuration file");
            }

            // delete the file downloaded from URI
            if (deleteTempFile) {
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
        [Option('F', "File", Default = "XESMartTarget.json", HelpText = "Configuration file")]
        public string ConfigurationFile { get; set; }

        [Option('N', "NoLogo", Default = false , HelpText = "Hides copyright banner at startup")]
        public bool NoLogo { get; set; }

        [Option('Q', "Quiet", Default = false, HelpText = "Prevents output to console")]
        public bool Quiet { get; set; }

        [Option('G', "GlobalVariables",  HelpText = "Global variables in the form key1=value1 key2=value2")]
        public IEnumerable<string> GlobalVariables { get; set; }

        [Option('L', "LogFile", HelpText = "Log File")]
        public string LogFile { get; set; }

        [Option('T', "Timeout", HelpText = "Timeout in seconds")]
        public int TimeoutSeconds { get; set; } = -1;
    }
}
