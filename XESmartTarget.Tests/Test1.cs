using Newtonsoft.Json;
using XESmartTarget.Core.Config;
using XESmartTarget.Core.Utils;

namespace XESmartTarget.Tests
{
    [TestClass]
    public class JsonFilesConversionTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void Test()
        {
            string folderPath = @"c:\temp\xe";
            string[] jsonFiles = Directory.GetFiles(folderPath, "*.json");
            Assert.IsTrue(jsonFiles.Length > 0, $"No JSON files found in {folderPath}.");

            ModelConverter converter = new ModelConverter();

            foreach (var file in jsonFiles)
            {
                TestContext.WriteLine($"Processing file: {file}");
                string json = File.ReadAllText(file);
                Assert.IsFalse(string.IsNullOrWhiteSpace(json), $"File {file} is empty.");

                try
                {
                    object obj = converter.Deserialize(json, typeof(TargetConfig));
                    TargetConfig config = obj as TargetConfig;
                    Assert.IsNotNull(config, $"Deserialized object from {file} is null or not a TargetConfig.");

                    string dump = JsonConvert.SerializeObject(config, Formatting.Indented);
                    TestContext.WriteLine($"Dump of converted object from {file}:\n{dump}");
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Error converting file {file}: {ex.Message}");
                }
            }
        }
    }
}
