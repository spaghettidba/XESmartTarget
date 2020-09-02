using CommandLine;
using CommandLine.Text;
using NLog;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                        pathToLog = Path.Combine(Environment.CurrentDirectory, "SqlWorkload.log");
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
                config.Target.Stop();
                source.CancelAfter(TimeSpan.FromSeconds(10)); // give a 10 seconds cancellation grace period 
            };

            logger.Info("Starting Target");
            Task t = processTargetAsync(config.Target);
            t.Wait();
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
    }
}
