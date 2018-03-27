using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AggregatedElevationService
{
    public class RequestHandler
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private const double MAX_DISTANCE = 2.0d; //TODO: určit jestli je to dostatečná vzálenost

        public async Task<ElevationResponse> HandleRequest(string key, string locations, string source)
        {
            var (existingUser, premiumUser) = CheckApiKey(key);
            if (!existingUser)
            {
                return new ElevationResponse(ElevationResponses.INVALID_KEY, null);
            }

            IEnumerable<Location> parsedLocations = ParseLocations(locations).ToList();
            var elevationResponse = new ElevationResponse(parsedLocations);

            List<Location> locsWithoutElevation = null;
            if (source == null)
            {
                /*
                Stopwatch s = Stopwatch.StartNew();
                locsWithoutElevation = GetPointsFromDb(parsedLocations, ref elevationResponse, premiumUser, false);
                s.Stop();
                Console.WriteLine("Spheroid: "+s.ElapsedMilliseconds);
                s.Restart();
                locsWithoutElevation = GetPointsFromDbParallel(parsedLocations, ref elevationResponse, premiumUser, false);
                s.Stop();
                Console.WriteLine("Sphere: " + s.ElapsedMilliseconds);
                */
                Stopwatch s = Stopwatch.StartNew();
                locsWithoutElevation = GetPointsFromDbParallel(parsedLocations, ref elevationResponse, premiumUser, spheroid: false);
                s.Stop();
                Console.WriteLine("Getting points: " + s.ElapsedMilliseconds);

                if (locsWithoutElevation.Count == 0)
                {
                    elevationResponse.status = ElevationResponses.OK;
                    return elevationResponse;
                }
            }
            else
            {
                locsWithoutElevation = (List<Location>) parsedLocations;
            }


            List<Result> providerResults = null;
            try
            {
                providerResults = await GetElevation(locsWithoutElevation, source);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                logger.Error(e);
            }

            if (providerResults == null)
            {
                if (source != null) throw new ElevationProviderException("Results from providers were empty");
                elevationResponse.status = ElevationResponses.INCOMPLETE;
                return elevationResponse;
            }

            foreach (Result result in elevationResponse.result)
            {
                if (result.elevation != -1) continue;
                Result providerResult = providerResults.Find(r => r.location.Equals(result.location));
                if (providerResult == null)
                {
                    //Google rád ořezává počet desetinných míst - kontrola s počtem, který Google vrací (ten je taky variabilní)
                    string number = providerResults[0].location.lat.ToString(CultureInfo.InvariantCulture);
                    int length = number.Substring(number.IndexOf(".")).Length-1; 
                    double lat = Math.Round(result.location.lat, length);
                    double lng = Math.Round(result.location.lng, length);
                    providerResult = providerResults.Find(r => r.location.Equals(new Location(lat, lng)));
                }
                result.elevation = providerResult.elevation;
                result.resolution = providerResult.resolution;
            }

            elevationResponse.status = ElevationResponses.OK;
            return elevationResponse;
        }

        private static (bool existingUser, bool premiumUser) CheckApiKey(string key)
        {
            //TODO: dodělat
            return (key == "klic" || key == "premium", key == "premium");
        }

        private static IEnumerable<Location> ParseLocations(string locations)
        {
            string[] locationsSplit = locations.Split('|'); //TODO: kontrola formátování
            var latLongs = new List<Location>();
            foreach (string l in locationsSplit)
            {
                string[] locSplit = l.Split(',');
                bool latParsed = double.TryParse(locSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double lat);
                bool lonParsed = double.TryParse(locSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double lon);

                if (!(latParsed && lonParsed)) throw new ElevationProviderException("Location could not be parsed");

                //Hodnoty jsou ve formátu WGS 84
                //Kontrola jestli je latitude (zeměpisná šírka) v rozsahu -90 až 90
                bool latFormatted = Math.Abs(lat) <= 90;
                //Kontrola jestli je longitude (zeměpisná délka) v rozsahu -180 až 180
                bool lonFormatted = Math.Abs(lon) <= 180;

                if (!(latFormatted && lonFormatted))
                    throw new ElevationProviderException("Location is not formatted properly");

                var loc = new Location(lat, lon);
                latLongs.Add(loc);
            }

            return latLongs;
        }

        private static List<Location> GetPointsFromDb(IEnumerable<Location> locations, ref ElevationResponse elevationResponse, bool premiumUser, bool spheroid)
        {
            var locsWithoutElevation = new List<Location>();
            foreach (Result result in elevationResponse.result)
            {
                var closest = new ResultDistance();
                try
                {
                    closest = PostgreDbConnector.GetClosestPoint(result.location, premiumUser, spheroid);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    logger.Error(e);
                }

                if (closest.Distance <= MAX_DISTANCE && closest.Distance >= 0)
                {
                    result.elevation = closest.Result.elevation;
                    result.resolution = closest.Result.resolution != -1 || closest.Result.resolution != 0 ? closest.Result.resolution : closest.Distance;
                }
                else
                {
                    locsWithoutElevation.Add(result.location);
                }
            }

            return locsWithoutElevation;
        }

        private static List<Location> GetPointsFromDbParallel(IEnumerable<Location> locations, ref ElevationResponse elevationResponse, bool premiumUser, bool spheroid)
        {
            var locsWithoutElevation = new List<Location>();
            List<ResultDistance> resultDistances;
            try
            {
                resultDistances = PostgreDbConnector.GetClosestPointParallel(locations, premiumUser, spheroid);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                logger.Error(e);
                throw;
            }
            
            foreach (Result result in elevationResponse.result)
            {
                ResultDistance closest = resultDistances.Find(rd => rd.Result.location.Equals(result.location));
                if (closest.Distance <= MAX_DISTANCE && closest.Distance >= 0)
                {
                    result.elevation = closest.Result.elevation;
                    result.resolution = closest.Result.resolution != -1 ? closest.Result.resolution : closest.Distance;
                }
                else
                {
                    locsWithoutElevation.Add(result.location);
                }
            }

            return locsWithoutElevation;
        }

        private static async Task<List<Result>> GetElevation(IReadOnlyCollection<Location> locations, string source)
        {
            //TODO: vylepšit source
            var google = new GoogleElevationProvider();
            var seznam = new SeznamElevationProvider();
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

            //Uložení hodnot do databáze
            if (source == "google" || source == "seznam")
            {
                try
                {
                    int rowsAddedGoogle = PostgreDbConnector.InsertResultsParallel(googleResults, Source.Google);
                    int rowsAddedSeznam = PostgreDbConnector.InsertResultsParallel(seznamResults, Source.Seznam);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    logger.Error(e);
                }
            }

            switch (source)
            {
                case "seznam":
                    return seznamResults;
                default:
                    return googleResults;
            }
        }

        public static async void TestElevationPrecision(int limit = 100, int offset = 0)
        {
            var pgc = new PostgreDbConnector();
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

            Console.WriteLine("Google: {0}", Math.Sqrt(googleSum)/limit);
            Console.WriteLine("Seznam: {0}", Math.Sqrt(seznamSum)/limit);
        }
}
}