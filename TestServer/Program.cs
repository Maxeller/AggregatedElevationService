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

namespace TestServer
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
            Result r1 = new Result(1.0f, 1.0f, 1.0f, 1.0f);
            Result r2 = new Result(2.0f, 2.0f, 2.0f, 2.0f);
            results[0] = r1;
            results[1] = r2;
            ElevationResponse er = new ElevationResponse("OK", results);
            Console.WriteLine(er.SerializeObject());
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
