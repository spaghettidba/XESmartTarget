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
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileMajorPart.ToString() + "." + fvi.FileMinorPart.ToString() + "." + fvi.FileBuildPart.ToString();

            var options = new Options();
#if DEBUG
            if (args.Length == 0)
                args = new string[] {"--File", @"c:\temp\sample.json" };
#endif
            if (!CommandLine.Parser.Default.ParseArguments(args, options))
            {
                return;
            }

            if (!options.NoLogo)
            {
                Console.WriteLine(fvi.FileDescription + " " + version);
                Console.WriteLine(fvi.LegalCopyright + " - " + fvi.CompanyName);
                Console.WriteLine();
            }
            else
            {
                // also suppress writing to console
                if (LogManager.Configuration != null)
                {
                    var target = (ConsoleTarget)LogManager.Configuration.FindTargetByName("console");
                    if (target != null)
                    {
                        target.Error = true;
                    }
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

            TargetConfig config = TargetConfig.LoadFromFile(options.ConfigurationFile);

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) {
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
        [Option('F', "File", DefaultValue = "XESMartTarget.json", HelpText = "Configuration file")]
        public string ConfigurationFile { get; set; }

        [Option('N', "NoLogo", DefaultValue = false , HelpText = "Hides copyright banner at startup")]
        public bool NoLogo { get; set; }

        [ParserState]
        public IParserState LastParserState { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this,
              (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}
