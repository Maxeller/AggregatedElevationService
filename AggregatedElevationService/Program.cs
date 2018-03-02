using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Xml.Serialization;

namespace AggregatedElevationService
{
    class Program
    {
        private const string URL = "http://localhost:8889/elevation";

        private static void Main(string[] args)
        {
            //TestElevationProviders();
            //XmlSerializationTest();
            //StartElevationService();
            TestDatabase();
            Console.ReadKey();
        }

        private static void StartElevationService()
        {
            var binding = new WebHttpBinding();
            var webServiceHost = new WebServiceHost(typeof(ElevationServiceHost));
            webServiceHost.AddServiceEndpoint(typeof(ElevationServiceHost), binding, URL);
            webServiceHost.Open();
            Console.WriteLine("Listening on {0}", URL);
            Console.WriteLine("Press enter to stop service");
            Console.ReadLine();
            webServiceHost.Close();
        }

        private static void TestDatabase()
        {
            PostgreConnector pgc = new PostgreConnector();
            //pgc.InitializeDatabase();
            pgc.LoadXyzFile(@"files/MOST64_5g.xyz");
            //pgc.LoadxyzFile(@"files/12-24-05.txt");
        }

        private static async void TestElevationProviders()
        {
            List<Location> list = new List<Location>
            {
                new Location(50.482999f, 13.430489f),
                new Location(39.7391536f, -104.9847034f)
            };
            //GoogleElevationProvider google = new GoogleElevationProvider();
            //var a = await google.GetElevationResultsAsync(list);
            //SeznamElevationProvider seznam = new SeznamElevationProvider();
            //var a = await seznam.GetElevationResultsAsync(list);
            //SeznamElevationProvider.ParseContent(File.ReadAllText(@"files/SeznamResponse.xml"));
            var r = new RequestHandler();
            var a = await r.HandleRequest("klic", "50.482999,13.430489|39.7391536,-104.9847034");
            Console.WriteLine(a.SerializeObject());
        }

        private static void XmlSerializationTest()
        {
            Result[] results = new Result[2];
            List<Result> res = new List<Result>();
            Result r1 = new Result(39.7391536f, -104.9847034f, 1608.6379395f, 4.771976f);
            Result r2 = new Result(50.482999f, 13.430489f, 367.9305725f, 152.7032318f);
            results[0] = r1;
            results[1] = r2;
            res.Add(r1);
            res.Add(r2);
            ElevationResponse erPole = new ElevationResponse("OK", res);
            //ElevationResponse erList = new ElevationResponse("OK", res.ToArray());
            Console.WriteLine(erPole.SerializeObject());
            //Console.WriteLine(erList.SerializeObject());
        }
    }

    static class Helper
    {
        public static string SerializeObject<T>(this T toSerialize)
        {
            var xmlSerializer = new XmlSerializer(toSerialize.GetType());

            using (var textWriter = new StringWriter())
            {
                xmlSerializer.Serialize(textWriter, toSerialize);
                return textWriter.ToString();
            }
        }
    }
}
