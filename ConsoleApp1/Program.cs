using XESmartTarget.Core.Utils;
using XESmartTarget.Core.Config;
using Newtonsoft.Json;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            string folderPath = @"c:\temp\xe";
            string[] jsonFiles = Directory.GetFiles(folderPath, "*.json");

            ModelConverter converter = new ModelConverter();

            foreach (var file in jsonFiles)
            {
                string json = File.ReadAllText(file);

                try
                {
                    object obj = converter.Deserialize(json, typeof(TargetConfig));
                    TargetConfig config = obj as TargetConfig;
                    string dump = JsonConvert.SerializeObject(config, Formatting.Indented);
                }
                catch (Exception ex)
                {
                }
            }
        }
    }
}
