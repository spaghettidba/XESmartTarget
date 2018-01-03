using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Responses
{
    public class GroupedTableAppenderResponse : TableAppenderResponse
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public GroupedTableAppenderResponse()
        {

        }

        public List<string> GroupBy { get; set; } // Groupby Columns
        
    }
}
