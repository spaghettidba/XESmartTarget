using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace XESmartTarget.Core.Config
{
    public class TargetConfig
    {
        public string ServerName { get; set; }
        public Target Target { get; set; }


        public static void test()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer(new TargetConfigTypeResolver());
            TargetConfig x = new TargetConfig();
            x.ServerName = "(local)";
            x.Target = new Target();
            x.Target.SessionName = "system_health";
            Responses.TableAppenderResponse res = new Responses.TableAppenderResponse();
            res.DelaySeconds = 0;
            res.TargetTable = "someTable";
            res.Filter = "someField = \"SomeValue\"";
            res.Events.Add("SomeEvent");
            x.Target.Responses.Add(res);
            string s = ser.Serialize(x);

            TargetConfig tc = ser.Deserialize<TargetConfig>(Samples.Sample.ToString());

            
        }

    }
}
