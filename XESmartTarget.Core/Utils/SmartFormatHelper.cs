using NLog;
using SmartFormat;

namespace XESmartTarget.Core.Utils
{
    public static class SmartFormatHelper
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private static SmartFormatter fmt = Smart.CreateDefaultSmartFormat();
        public static string Format(string format, Dictionary<string, string> args)
        {
            try
            {
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
