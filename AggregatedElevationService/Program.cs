using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Device.Location;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel.Web;
using System.Xml.Serialization;

namespace AggregatedElevationService
{
    class Program
    {
        const string URL = "http://localhost:8889/elevation";

        static void Main(string[] args)
        {
            //TestElevationProviders();
            //XmlSerializationTest();
            //Console.ReadKey();
            
            WebHttpBinding binding = new WebHttpBinding();
            WebServiceHost webServiceHost = new WebServiceHost(typeof(ElevationProviderHost));
            webServiceHost.AddServiceEndpoint(typeof(ElevationProviderHost), binding, URL);
            webServiceHost.Open();
            Console.WriteLine("Listening on {0}", URL);
            Console.WriteLine("Press enter to stop service");
            Console.ReadLine();
            webServiceHost.Close();
            
        }

        static async void TestElevationProviders()
        {
            GoogleElevationProvider google = new GoogleElevationProvider();
            GeoCoordinate geo = new GeoCoordinate(39.7391536, -104.9847034);
            GeoCoordinate[] geos = new GeoCoordinate[1];
            geos[0] = geo;
            var a = await GoogleElevationProvider.GetElevationResultsAsync(geos);
        }

        static void XmlSerializationTest()
        {
            Result[] results = new Result[2];
            List<Result> res = new List<Result>();
            Result r1 = new Result(39.7391536f, -104.9847034f, 1608.6379395f, 4.771976f);
            Result r2 = new Result(50.482999f, 13.430489f, 367.9305725f, 152.7032318f);
            results[0] = r1;
            results[1] = r2;
            res.Add(r1);
            res.Add(r2);
            ElevationResponse erPole = new ElevationResponse("OK", results);
            ElevationResponse erList = new ElevationResponse("OK", res.ToArray());
            Console.WriteLine(erPole.SerializeObject());
            Console.WriteLine(erList.SerializeObject());
        }
    }

    static class Help
    {
        public static string SerializeObject<T>(this T toSerialize)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(toSerialize.GetType());

            using (StringWriter textWriter = new StringWriter())
            {
                xmlSerializer.Serialize(textWriter, toSerialize);
                return textWriter.ToString();
            }
        }
    }
}
