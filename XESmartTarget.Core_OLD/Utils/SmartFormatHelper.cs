using SmartFormat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Utils
{
    public static class SmartFormatHelper
    {

        public static string Format(string format, Dictionary<string, string> args)
        {
            var fmt = Smart.CreateDefaultSmartFormat();
            fmt.Settings.ConvertCharacterStringLiterals = false;
            return fmt.Format(format, args);
        }

        public static string Format(string format, Dictionary<string,object> args)
        {
            var fmt = Smart.CreateDefaultSmartFormat();
            fmt.Settings.ConvertCharacterStringLiterals = false;
            return fmt.Format(format, args);
        }
    }
}
