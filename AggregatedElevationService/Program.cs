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
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private const string URL = "http://localhost:8889/elevation"; //TODO move to konfigurák
        
        private static void Main(string[] args)
        {
            //TODO: konfigurák
            StartElevationService();
            Console.ReadKey();
        }

        private static void StartElevationService()
        {
            var binding = new WebHttpBinding();
            var webServiceHost = new WebServiceHost(typeof(ElevationServiceHost));
            webServiceHost.AddServiceEndpoint(typeof(ElevationServiceHost), binding, URL);
            webServiceHost.Open();
            Console.WriteLine("Listening on {0}", URL);
            logger.Info("Listening on {0}", URL);
            Console.WriteLine("Press enter to stop service");
            Console.ReadLine();
            webServiceHost.Close();
            logger.Info("Service stopped");
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
