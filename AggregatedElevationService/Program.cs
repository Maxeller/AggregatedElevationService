using System;
using System.Configuration;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Xml.Serialization;

namespace AggregatedElevationService
{
    class Program
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string SCHEME = ConfigurationManager.AppSettings["scheme"];
        private static readonly string HOST = ConfigurationManager.AppSettings["host"];
        private static readonly string HOST_LOCAL = ConfigurationManager.AppSettings["localhost"];
        private static readonly string PORT = ConfigurationManager.AppSettings["port"];
        private static readonly string PATH = ConfigurationManager.AppSettings["path"];

        private static void Main(string[] args)
        {
            //var pgc = new PostgreDbConnector();
            //pgc.InitializeDatabase();
            ChooseXyzFiles("files/");
            StartElevationService();
            Console.ReadKey();
        }

        private static void StartElevationService()
        {
            string url = $"{SCHEME}://{HOST}:{PORT}/{PATH}";
            var binding = new WebHttpBinding();
            var webServiceHost = new WebServiceHost(typeof(ElevationServiceHost));
            webServiceHost.AddServiceEndpoint(typeof(ElevationServiceHost), binding, url);
            webServiceHost.Open();
            Console.WriteLine("Listening on {0}", url);
            logger.Info("Listening on {0}", url);
            Console.WriteLine("Press enter to stop service");
            Console.ReadLine();
            webServiceHost.Close();
            Console.WriteLine("Service stopped");
            logger.Info("Service stopped");
        }

        private static void ChooseXyzFiles(string directoryPath)
        {
            string[] files = Directory.GetFiles(directoryPath);
            int number = 0;
            Console.WriteLine("{0}) {1}", number, "None");
            foreach (string file in files)
            {
                Console.WriteLine("{0}) {1}", ++number, file);
            }
            Console.WriteLine("{0}) {1}", ++number, "All");
            Console.Write("Choose file(s) to load: ");
            string line = Console.ReadLine();

            if (line == "0" || line == "") return;
            
            if (line == number.ToString())
            {
                var pgc = new PostgreDbConnector();
                foreach (string file in files)
                {
                    LoadXyzFile(file, pgc);
                }
            }
            else
            {
                //TODO: dodělat kontrolu
                string[] fileNumbers = new string[0];
                if (line != null)
                {
                    fileNumbers = line.Split(',');
                }
                var pgc = new PostgreDbConnector();
                foreach (string fileNumber in fileNumbers)
                {
                    string file = files[int.Parse(fileNumber)-1];
                    LoadXyzFile(file, pgc);
                }
            }
            //TODO: označit, že jsou už načtený
        }

        private static void LoadXyzFile(string filepath, PostgreDbConnector pgc)
        {
            Console.WriteLine("Loading file: {0}", filepath);
            int rowsAdded = pgc.LoadXyzFileParallel(filepath);
            Console.WriteLine("Rows {0} added from {1}", rowsAdded, filepath);
            logger.Info("Rows {0} added from {1}", rowsAdded, filepath);
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
