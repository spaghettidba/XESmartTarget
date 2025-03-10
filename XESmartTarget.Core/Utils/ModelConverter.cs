using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                    result.Add(t);
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

            JToken token = JsonConvert.DeserializeObject<JToken>(json);
            var dictionary = token.ToObject<Dictionary<string, object>>();
            return Deserialize(dictionary, type);
        }

        public object Deserialize(IDictionary<string, object> dictionary, Type type)
        {
            object p;
            try
            {
                if (type.IsAbstract && dictionary.ContainsKey("__type"))
                {
                    var subTypeName = dictionary["__type"]?.ToString();
                    if (!string.IsNullOrEmpty(subTypeName))
                    {
                        string fullTypeName = subTypeName.Contains(".")
                            ? subTypeName
                            : "XESmartTarget.Core.Responses." + subTypeName;
                        var assembly = Assembly.GetExecutingAssembly();
                        var realType = assembly.GetType(fullTypeName) ?? Type.GetType(fullTypeName);
                        if (realType != null && !realType.IsAbstract)
                        {
                            p = Activator.CreateInstance(realType);
                        }
                        else
                        {
                            p = FormatterServices.GetUninitializedObject(type);
                        }
                    }
                    else
                    {
                        p = FormatterServices.GetUninitializedObject(type);
                    }
                }
                else
                {
                    p = Activator.CreateInstance(type);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception during instance creation: " + ex.Message);
                p = FormatterServices.GetUninitializedObject(type);
            }

            var props = p.GetType().GetProperties();

            foreach (string key in dictionary.Keys.ToList())
            {
                string dictKey = dictionary.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                var prop = props.FirstOrDefault(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase));
                if (prop == null)
                    continue;

                object rawValue = dictionary[key];
                object value = ConvertJTokenIfNeeded(rawValue);
                dictionary[key] = value;

                if (prop.Name.Equals("OutputColumns", StringComparison.OrdinalIgnoreCase) &&
                    prop.PropertyType == typeof(List<string>))
                {
                    List<string> listOutput = null;
                    if (rawValue is JArray jArr)
                    {
                        listOutput = jArr.ToObject<List<string>>();
                    }
                    else if (value is IList listVal)
                    {
                        listOutput = listVal.Cast<object>().Select(x => x?.ToString()).ToList();
                    }
                    else
                    {
                        listOutput = new List<string> { value.ToString() };
                    }
                    prop.SetValue(p, listOutput, null);
                    continue;
                }

                if (prop.Name.Equals("OutputMeasurement", StringComparison.OrdinalIgnoreCase) && prop.PropertyType == typeof(string))
                {
                    prop.SetValue(p, value?.ToString(), null);
                    continue;
                }

                if (prop.Name.EndsWith("ServerName", StringComparison.OrdinalIgnoreCase))
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
                else if (prop.Name.EndsWith("Target", StringComparison.OrdinalIgnoreCase))
                {
                    value = ConvertJTokenIfNeeded(value);
                    if (value is IList listVal)
                    {
                        var deserializedList = new List<Target>();
                        foreach (var el in listVal)
                        {
                            var realEl = ConvertJTokenIfNeeded(el);
                            if (realEl is IDictionary<string, object> dic)
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
                else if (value is IDictionary<string, object> subDict)
                {
                    prop.SetValue(p, Deserialize(subDict, prop.PropertyType), null);
                }
                else if (value is IList && prop.PropertyType.IsGenericType)
                {
                    var listInstance = Activator.CreateInstance(prop.PropertyType) as IList;
                    if (listInstance != null)
                    {
                        Type elementType = prop.PropertyType.GetGenericArguments()[0];
                        foreach (var item in (IList)value)
                        {
                            object convertedItem = ConvertJTokenIfNeeded(item);
                            if (convertedItem is IDictionary<string, object> dictItem)
                            {
                                convertedItem = Deserialize(dictItem, elementType);
                            }
                            listInstance.Add(convertedItem);
                        }
                        prop.SetValue(p, listInstance, null);
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
            return p;
        }

        private object ConvertJTokenIfNeeded(object value)
        {
            if (value is JObject jObj)
            {
                var dict = new Dictionary<string, object>();
                foreach (var prop in jObj.Properties())
                {
                    dict[prop.Name] = ConvertJTokenIfNeeded(prop.Value);
                }
                return dict;
            }
            else if (value is JArray jArr)
            {
                var list = new List<object>();
                foreach (var item in jArr)
                {
                    list.Add(ConvertJTokenIfNeeded(item));
                }
                return list;
            }
            else if (value is JValue jVal)
            {
                return jVal.Value;
            }
            return value;
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
                return v?.ToString();
            }
            else if (propertyType == typeof(bool))
            {
                return Convert.ToBoolean(v);
            }
            else if (propertyType == typeof(int))
            {
                return Convert.ToInt32(v);
            }
            else if (propertyType == typeof(long))
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
