using SmartFormat;

namespace XESmartTarget.Core.Utils
{
    public static class SmartFormatHelper
    {
        public static string Format(string format, Dictionary<string, string> args)
        {
            var fmt = Smart.CreateDefaultSmartFormat();
            fmt.Settings.Parser.ConvertCharacterStringLiterals = false;
            return fmt.Format(format, args);
        }

        public static string Format(string format, Dictionary<string, object> args)
        {
            var fmt = Smart.CreateDefaultSmartFormat();
            fmt.Settings.Parser.ConvertCharacterStringLiterals = false;
            return fmt.Format(format, args);
        }
    }
}
