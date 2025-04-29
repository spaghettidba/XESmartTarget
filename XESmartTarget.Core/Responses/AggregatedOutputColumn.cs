using System.Text.RegularExpressions;

namespace XESmartTarget.Core.Responses
{
    public class AggregatedOutputColumn : OutputColumn
    {

        public enum AggregationType
        {
            Sum,
            Min,
            Max,
            Avg,
            Count
        }

        public override bool Calculated { get { return true; } set { ; } }

        public AggregationType Aggregation { get; private set; }

        public string BaseColumn { get; private set; }


        public AggregatedOutputColumn() : base()
        {

        }

        public AggregatedOutputColumn(string name) : base(name)
        {
        }

        public AggregatedOutputColumn(OutputColumn col) : base()
        {
            Name = col.Name == null ? "" : col.Name;
            Calculated = col.Calculated;
            if(col is AggregatedOutputColumn)
            {
                AggregatedOutputColumn outCol = (AggregatedOutputColumn)col;
                Aggregation = outCol.Aggregation;
                Expression = outCol.Expression;
                Alias = outCol.Alias;
                BaseColumn = outCol.BaseColumn;
                if (Name == "") Name = Alias;
            }
        }


        public static AggregatedOutputColumn TryParse(string name)
        {
            // looks for aggregation expressions in the name string
            // example: COUNT(someColumn), MIN(someColumn) etc...
            AggregatedOutputColumn result = null;
            bool isAggregated = false;

            Match aggregationMatch = Regex.Match(name, @"(?<expression>\b(?<aggregationType>(COUNT|SUM|MAX|MIN))\s*\((?<baseColumn>(\w+|\*))\))\s+AS\s+(?<alias>\w+)", RegexOptions.IgnoreCase);
            if (aggregationMatch.Success)
            {
                isAggregated = true;
                result = new AggregatedOutputColumn();
                result.Aggregation = (AggregationType)Enum.Parse(typeof(AggregationType),aggregationMatch.Groups["aggregationType"].ToString(), true);
                result.BaseColumn = aggregationMatch.Groups["baseColumn"].ToString();
                result.Expression = aggregationMatch.Groups["expression"].ToString();
                result.Alias = aggregationMatch.Groups["alias"].ToString();
            }

            if (!isAggregated)
            {
                return null;
            }
            
            return result;
        }

    }
}
