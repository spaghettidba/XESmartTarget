using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Reflection;

namespace XESmartTarget.Core.Config
{
    class TargetConfigTypeResolver : SimpleTypeResolver
    {

        private static Dictionary<string, Type> mappedTypes = new Dictionary<string, Type>();
        
        static TargetConfigTypeResolver()
        {
            Assembly currentAssembly = Assembly.GetExecutingAssembly();
            string nameSpace = "XESmartTarget.Core.Responses";
            Type[] types = currentAssembly.GetTypes().Where(t => String.Equals(t.Namespace, nameSpace, StringComparison.Ordinal)).ToArray();
            foreach(Type t in types)
            {
                mappedTypes.Add(t.AssemblyQualifiedName, t);
                mappedTypes.Add(t.Name, t);
            }
        }

        public override Type ResolveType(string id)
        {
            if(mappedTypes.ContainsKey(id))
            return mappedTypes[id];
            else return base.ResolveType(id);
        }

        public override string ResolveTypeId(Type type)
        {
            return base.ResolveTypeId(type);
        }
    }
}
