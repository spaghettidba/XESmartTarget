using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XESmartTarget.Core.Responses
{
    public class AggregatedOutputColumn : OutputColumn
    {

        public enum AggregationType
        {
            Sum,
            Min,
            Max,
            Avg
        }


        public AggregationType Aggregation { get; set; }

    }
}
