using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AggregatedElevationService
{
    public class AccuracyTesting
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static async void TestElevationPrecision(int limit = 100, int offset = 0)
        {
            IEnumerable<Result> results = PostgreDbConnector.QueryForTestingElevationPrecision(limit, offset);
            IEnumerable<Result> resultsEnumerable = results.ToList();
            List<Location> locations = resultsEnumerable.Select(result => result.location).ToList();
            var seznam = new SeznamElevationProvider();
            var google = new GoogleElevationProvider();
            var elevationTasks = new List<Task<List<Result>>>()
            {
                google.GetElevationResultsAsync(locations),
                seznam.GetElevationResultsAsync(locations),
            };

            List<Result>[] elevationResults = null;
            try
            {
                elevationResults = await Task.WhenAll(elevationTasks);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                logger.Error(e);
            }

            if (elevationResults == null) throw new ElevationProviderException("Elevation result were empty");

            List<Result> googleResults = elevationResults[0].ToList();
            List<Result> seznamResults = elevationResults[1].ToList();

            int i = -1;
            double googleSum = 0;
            double seznamSum = 0;
            foreach (Result result in resultsEnumerable)
            {
                i++;
                Result googleResult = googleResults[i];
                Result seznamResult = seznamResults[i];

                Console.WriteLine("Database: {0} (Lat: {1} Lng: {2})", result.elevation, result.location.lat, result.location.lng);
                //Console.WriteLine(result.ToString());
                Console.WriteLine("Google: {0} (Lat: {1} Lng: {2})", googleResult.elevation, googleResult.location.lat, googleResult.location.lng);
                //Console.WriteLine(googleResult.ToString());
                Console.WriteLine("Seznam: {0} (Lat: {1} Lng: {2})", seznamResult.elevation, seznamResult.location.lat, seznamResult.location.lng);
                //Console.WriteLine(seznamResult.ToString());

                googleSum += Math.Pow(result.elevation - googleResult.elevation, 2);
                seznamSum += Math.Pow(result.elevation - seznamResult.elevation, 2);

                Console.WriteLine("Google sum: {0}", googleSum);
                Console.WriteLine("Seznam sum: {0}", seznamSum);

                Console.WriteLine("-----------------------");
            }

            Console.WriteLine("Google: {0}", Math.Sqrt(googleSum) / limit);
            Console.WriteLine("Seznam: {0}", Math.Sqrt(seznamSum) / limit);
        }
    }
}