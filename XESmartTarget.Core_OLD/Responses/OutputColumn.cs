using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

        private string _name;

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
        public string Expression { get; set; }
        public string Alias { get; set; }
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
        public static implicit operator OutputColumn(string name)
        {
            OutputColumn result = null;
            result = new OutputColumn(name);
            return result;
        }


    }
}
