using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Device.Location;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel.Web;

namespace TestServer
{
    class Program
    {
        const string URL = "http://localhost:8889/elevation";

        static void Main(string[] args)
        {
            //TestElevationProviders();
      
            WebHttpBinding binding = new WebHttpBinding();
            WebServiceHost webServiceHost = new WebServiceHost(typeof(ElevationProviderHost));
            webServiceHost.AddServiceEndpoint(typeof(ElevationProviderHost), binding, URL);
            webServiceHost.Open();
            Console.WriteLine("Listening on {0}", URL);
            Console.WriteLine("Press enter to stop service");
            Console.ReadLine();
            webServiceHost.Close();

            //Console.ReadKey();
        }

        static async void TestElevationProviders()
        {
            GoogleElevationProvider google = new GoogleElevationProvider();
            GeoCoordinate geo = new GeoCoordinate(39.7391536, -104.9847034);
            GeoCoordinate[] geos = new GeoCoordinate[1];
            geos[0] = geo;
            var a = await GoogleElevationProvider.GetElevationResultsAsync(geos);
        }
    }
}
