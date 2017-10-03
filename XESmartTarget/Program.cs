using CommandLine;
using CommandLine.Text;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            string name = assembly.FullName;
            logger.Info(name + " " + version);

            var options = new Options();
#if DEBUG
            options.ConfigurationFile = @"c:\temp\sample.json";
#else
            if (!CommandLine.Parser.Default.ParseArguments(args, options)) 
            {
                return;
            }
#endif

            logger.Info(String.Format("Reading configuration from '{0}'", options.ConfigurationFile));

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
        }

        public static async Task processTargetAsync(Target target)
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
