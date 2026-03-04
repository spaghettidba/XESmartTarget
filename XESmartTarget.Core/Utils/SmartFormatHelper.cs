using NLog;
using SmartFormat;

namespace XESmartTarget.Core.Utils
{
    public static class SmartFormatHelper
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        
        public static string Format(string format, Dictionary<string, string> args)
        {
            try
            {
                SmartFormatter fmt = Smart.CreateDefaultSmartFormat();
                fmt.Settings.Parser.ConvertCharacterStringLiterals = false;
                return fmt.Format(format, args);
            }
            catch (Exception e)
            {
                logger.Debug(e.Message);
                return format;
            }
        }

        public static string Format(string format, Dictionary<string, object> args)
        {
            try
            {
                SmartFormatter fmt = Smart.CreateDefaultSmartFormat();
                fmt.Settings.Parser.ConvertCharacterStringLiterals = false;
                return fmt.Format(format, args);
            }
            catch (Exception e)
            {
                logger.Debug(e.Message);
                return format;
            }
        }
    }
}
