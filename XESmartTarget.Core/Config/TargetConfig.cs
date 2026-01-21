using DouglasCrockford.JsMin;
using Newtonsoft.Json;  
using System.Web;                       
using XESmartTarget.Core.Utils;        

namespace XESmartTarget.Core.Config
{
    public class TargetConfig
    {
        public required Target[] Target { get; set; }

        public static Dictionary<string, object> GlobalVariables = new Dictionary<string, object>();

        public static void Test()
        {
            TargetConfig x = new TargetConfig()
            {
                Target = new Target[1]
            };
            x.Target[0].SessionName = "system_health";
            Responses.TableAppenderResponse res = new Responses.TableAppenderResponse()
            {
                UploadIntervalSeconds = 0,
                TableName = "someTable",
                Filter = "someField = \"SomeValue\""
            };
            res.Events.Add("SomeEvent");
            x.Target[0].Responses.Add(res);

            string s = JsonConvert.SerializeObject(x, Formatting.Indented);

            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(Samples.Sample.ToString());
            ModelConverter converter = new ModelConverter();
            if (dictionary != null)
            {
                TargetConfig tc = (TargetConfig)converter.Deserialize(dictionary, typeof(TargetConfig));
            }
        }

        public static TargetConfig LoadFromFile(string path)
        {
            using (StreamReader r = new StreamReader(path))
            {
                string json = r.ReadToEnd();

                foreach (string key in GlobalVariables.Keys)
                {
                    json = json.Replace($"${key}", HttpUtility.JavaScriptStringEncode(GlobalVariables[key].ToString()));
                }

                var minifier = new JsMinifier();
                string jsonMin = minifier.Minify(json);

                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMin);

                ModelConverter converter = new ModelConverter();
                if (dictionary != null)
                {
                    return (TargetConfig)converter.Deserialize(dictionary, typeof(TargetConfig));
                }
                else 
                {
                    throw new Exception("Failed to deserialize configuration file.");
                }
            }
        }
    }
}
