using CommandLine;
using NLog;
using System.Diagnostics;
using XESmartTarget.Core.Utils;

namespace XelToCsv
{
    class Program
    {

        private static Logger logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options => ProcessTarget(options));

            
        }

        private static void ProcessTarget(Options options)
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileMajorPart.ToString() + "." + fvi.FileMinorPart.ToString() + "." + fvi.FileBuildPart.ToString();
            string? name = assembly.FullName;
            logger.Info(name + " " + version);

            logger.Info("Converting {0} to {1}", options.SourceFile, options.DestinationFile);

            Convert(options.SourceFile, options.DestinationFile);
        }

        public static void Convert(String sourceFile, String destinationFile)
        {
            if (File.Exists(destinationFile))
            {
                File.Delete(destinationFile);
            }

            XELFileCSVAdapter Adapter = new XELFileCSVAdapter();
            Adapter.InputFile = sourceFile;
            Adapter.OutputFile = destinationFile;

            DateTime startTime = DateTime.Now;

            try
            {
                Adapter.Convert();
            }
            catch(Exception e)
            {
                logger.Error("Conversion Error");
                logger.Error(e);
            }
            TimeSpan duration = DateTime.Now - startTime;
            logger.Info("Conversion finished at {0}", DateTime.Now);
            logger.Info("{0} seconds taken", duration.TotalSeconds);
        }


        class Options
        {
            [Option('s', "SourceFile", Required = true, HelpText = "Source file")]
            public string SourceFile { get; set; } = string.Empty;

            [Option('d', "DestinationFile", Required = true, HelpText = "Destination file")]
            public string DestinationFile { get; set; } = string.Empty;

        }

    }
}
