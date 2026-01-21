using System.Text.RegularExpressions;

namespace XESmartTarget.Core.Responses
{
    public class OutputColumn
    {
        public enum ColType
        {
            Column,
            Tag,
            Field
        }

        private string _name = string.Empty;

        public string Name {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
                if (Regex.IsMatch(value, @"\s+AS\s+", RegexOptions.IgnoreCase))
                {
                    Calculated = true;
                }
                
            }
        }

        public virtual bool Calculated { get; set; } = false;
        public bool Hidden { get; set; } = false;
        public string Expression { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public ColType ColumnType { get; set; } = ColType.Column;

        public OutputColumn()
        {
            Hidden = false;
        }

        public OutputColumn(string name) : this()
        {
            Name = name;
            Alias = name;
        }


        public override string ToString()
        {
            return Name;
        }


        // Allow implicit conversion from string
        // This is particularly useful when reading
        // the configuration from a .json file, in order
        // to keep it as simple as possible
        public static implicit operator OutputColumn(string? name)
        {
            OutputColumn result = new OutputColumn(name ?? string.Empty);
            return result;
        }


    }
}
