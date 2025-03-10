using Newtonsoft.Json;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

namespace XESmartTarget.Core.Utils
{
    public class ModelConverter
    {
        public IEnumerable<Type> SupportedTypes
        {
            get
            {
                List<Type> result = new List<Type>();
                Assembly currentAssembly = Assembly.GetExecutingAssembly();
                string nameSpace = "XESmartTarget.Core.";
                Type[] types = currentAssembly.GetTypes().Where(t => t != null && t.FullName.StartsWith(nameSpace) & !t.FullName.Contains("+")).ToArray();
                foreach (Type t in types)
                {
                    try
                    {
                        result.Add(t);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                return result;
            }
        }

        public T Deserialize<T>(string json)
        {
            return (T)Deserialize(json, typeof(T));
        }

        public object Deserialize(string json, Type type)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            return Deserialize(dictionary, type);
        }

        public object Deserialize(IDictionary<string, object> dictionary, Type type)
        {
            object p;
            try
            {
                p = Activator.CreateInstance(type);
            }
            catch
            {
                p = FormatterServices.GetUninitializedObject(type);
            }

            var props = type.GetProperties();

            foreach (string key in dictionary.Keys)
            {
                var prop = props.FirstOrDefault(t => t.Name == key);
                if (prop == null)
                    continue;  

                var value = dictionary[key];

                if (prop.Name.EndsWith("ServerName"))
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        prop.SetValue(p, value?.ToString());
                    }
                    else
                    {
                        if (value is string singleStr)
                        {
                            prop.SetValue(p, new string[] { singleStr }, null);
                        }
                        else
                        {
                            prop.SetValue(p, ConvertToStringArray(value), null);
                        }
                    }
                }
                else if (prop.Name.EndsWith("Target"))
                {
                    if (value is IList)
                    {
                        var listVal = (IList)value;
                        var deserializedList = new List<Target>();

                        foreach (var el in listVal)
                        {
                            if (el is IDictionary<string, object> dic)
                            {
                                var tObj = Deserialize(dic, typeof(Target));
                                deserializedList.Add((Target)tObj);
                            }
                        }
                        prop.SetValue(p, deserializedList.ToArray(), null);
                    }
                    else if (value is IDictionary<string, object> singleDict)
                    {
                        var tObj = Deserialize(singleDict, typeof(Target));
                        prop.SetValue(p, new Target[] { (Target)tObj }, null);
                    }
                }
                else
                {
                    if (value is IDictionary<string, object> subDict)
                    {
                        prop.SetValue(p, Deserialize(subDict, prop.PropertyType), null);
                    }
                    else if (value is IList && prop.PropertyType.IsGenericType)
                    {
                        var listInstance = Activator.CreateInstance(prop.PropertyType);
                        var list = listInstance as IList;
                        if (list != null)
                        {
                            foreach (var item in (IList)value)
                            {
                                list.Add(item);
                            }
                            prop.SetValue(p, list, null);
                        }
                    }
                    else if (prop.PropertyType.IsEnum)
                    {
                        prop.SetValue(p, Enum.Parse(prop.PropertyType, value.ToString()), null);
                    }
                    else
                    {
                        prop.SetValue(p, GetValueOfType(value, prop.PropertyType), null);
                    }
                }
            }

            return p;
        }

        private string[] ConvertToStringArray(object val)
        {
            if (val == null)
                return null;

            if (val is IEnumerable enumerable && !(val is string))
            {
                var strList = new List<string>();
                foreach (var item in enumerable)
                {
                    strList.Add(item?.ToString());
                }
                return strList.ToArray();
            }

            return new string[] { val.ToString() };
        }

        private object GetValueOfType(object v, Type propertyType)
        {
            if (propertyType == typeof(string))
            {
                return (string)v;
            }
            else if (propertyType == typeof(Boolean))
            {
                return Convert.ToBoolean(v);
            }
            else if (propertyType == typeof(Int32))
            {
                return Convert.ToInt32(v);
            }
            else if (propertyType == typeof(Int64))
            {
                return Convert.ToInt64(v);
            }
            else
                return v;
        }

        public IDictionary<string, object> Serialize(object obj)
        {
            throw new NotImplementedException();
        }
    }
}
