using System;
using System.Configuration;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace AggregatedElevationService
{
    class Program
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string SCHEME = ConfigurationManager.AppSettings["scheme"];
        private static readonly string HOST = ConfigurationManager.AppSettings["host"];
        //private static readonly string HOST = ConfigurationManager.AppSettings["localhost"];
        private static readonly string PORT = ConfigurationManager.AppSettings["port"];
        private static readonly string PATH = ConfigurationManager.AppSettings["path"];

        private static void Main(string[] args)
        {
            if (ConfigurationManager.AppSettings["db_initialized"] == "false")
            {
                InitializeDatabase();
            }
            //ChooseXyzFiles("files/");
            StartElevationService();
            //TestElevationPrecision(); //conture map
            Console.ReadKey();
        }

        private static void InitializeDatabase()
        {
            PostgreDbConnector.InitializeDatabase();

            Configuration configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            KeyValueConfigurationCollection settings = configFile.AppSettings.Settings;
            settings["db_initialized"].Value = "true";
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);

            System.Diagnostics.Process.Start(Application.ExecutablePath);
            Environment.Exit(0);
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
                    LoadXyzFile(file);
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
                    LoadXyzFile(file);
                }
            }
            //TODO: označit, že jsou už načtený
        }

        private static void LoadXyzFile(string filepath)
        {
            Console.WriteLine("Data formats: ");
            Console.WriteLine("1) {0}", SRID.S_JTSK);
            Console.WriteLine("2) {0}", SRID.WGS84_UTM_33N); //TODO: smazat páč ty data maj jen jtsk
            Console.Write("Choose data format: ");
            string line = Console.ReadLine();
            SRID inputFormat;
            
            switch (line)
            {
                case "1":
                    inputFormat = SRID.S_JTSK;
                    break;
                case "2":
                    inputFormat = SRID.WGS84_UTM_33N;
                    break;
                default:
                    inputFormat = SRID.S_JTSK;
                    break;
            }
            Console.WriteLine("Loading file {0} with format {1}", filepath, inputFormat);
            int rowsAdded = PostgreDbConnector.LoadXyzFileParallel(filepath, inputFormat);
            Console.WriteLine("Rows {0} added from {1} with format {2}", rowsAdded, filepath, inputFormat);
            logger.Info("Rows {0} added from {1} with format {2}", rowsAdded, filepath, inputFormat);
        }

        private static void TestElevationPrecision()
        {
            var requestHandler = new RequestHandler();
            RequestHandler.TestElevationPrecision(300, 500);
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
