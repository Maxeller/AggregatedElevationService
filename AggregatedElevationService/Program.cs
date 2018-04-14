using System;
using System.Configuration;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Windows.Forms;
using static System.Configuration.ConfigurationManager;

namespace AggregatedElevationService
{
    class Program
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string SCHEME = AppSettings["scheme"];
        private static readonly string HOST = AppSettings["host"];
        private static readonly string PORT = AppSettings["port"];
        private static readonly string PATH = AppSettings["path"];
        private static readonly string FILEPATH = AppSettings["filepath"];

        private static void Main(string[] args)
        {
            if (AppSettings["db_initialized"] == "false")
            {
                InitializeDatabase();
            }
            //ChooseXyzFiles(FILEPATH);
            StartElevationService();
            //TestElevationPrecision();
            Console.ReadKey();
        }

        private static void InitializeDatabase()
        {
            PostgreDbConnector.InitializeDatabase();

            Configuration configFile = OpenExeConfiguration(ConfigurationUserLevel.None);
            KeyValueConfigurationCollection settings = configFile.AppSettings.Settings;
            settings["db_initialized"].Value = "true";
            configFile.Save(ConfigurationSaveMode.Modified);
            RefreshSection(configFile.AppSettings.SectionInformation.Name);

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
            Console.WriteLine("Loading file {0}", filepath);
            logger.Info("Loading file {0}", filepath);
            int rowsAdded = PostgreDbConnector.LoadXyzFileParallel(filepath, SRID.S_JTSK);
            Console.WriteLine("Rows {0} added from {1}", rowsAdded, filepath);
            logger.Info("Rows {0} added from {1}", rowsAdded, filepath);
        }

        private static void TestElevationPrecision()
        {
            AccuracyTesting.TestElevationPrecision(1000, 0, false);
        }
    }
}
