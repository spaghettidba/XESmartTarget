using Microsoft.SqlServer.XEvent.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Responses
{
    class GroupedTableAppenderResponse : TableAppenderResponse
    {
        public GroupedTableAppenderResponse()
        {

        }

        public string GroupBy { get; set; } // Groupby Expression (list of colunns to group on)
        
    }
}
