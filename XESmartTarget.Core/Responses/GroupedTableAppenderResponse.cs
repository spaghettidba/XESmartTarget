using Microsoft.SqlServer.XEvent.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Responses
{
    class GroupedTableAppenderResponse : TableAppenderResponse
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public GroupedTableAppenderResponse()
        {

        }

        public List<string> GroupBy { get; set; } // Groupby Expression (list of colunns to group on)
        
    }
}
