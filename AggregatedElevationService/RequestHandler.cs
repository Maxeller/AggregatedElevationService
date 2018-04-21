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

        private const double MAX_DISTANCE = 2.0d;
        private const double WITHIN_DISTANCE = 10.0d;

        public async Task<ElevationResponse> HandleRequest(string key, string locations, string source)
        {
            //Zjištění validity API klíče
            var (existingUser, premiumUser) = CheckApiKey(key);
            if (!existingUser)
            {
                return new ElevationResponse(ElevationResponses.INVALID_KEY, null);
            }

            //Parsování a kontrola lokací v URL
            IEnumerable<Location> parsedLocations = ParseLocations(locations).ToList();
            //Vytvoření odpovědi s lokacemi 
            var elevationResponse = new ElevationResponse(parsedLocations);

            //Nalezení nejbližího bodu v DB (pokud nebyl nebyl přímo vybrán source [spíše použit jako testovací funkce])
            List<Location> locsWithoutElevation;
            if (source == null)
            {
                Stopwatch s = Stopwatch.StartNew();
                locsWithoutElevation = (List<Location>) GetPointsFromDbParallel(parsedLocations, ref elevationResponse, premiumUser, false);
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

            //Načtení hodnot (které nebyly nalezeny v DB) z externích poskytovatelů výškopisu
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
            
            //Integrace výsledků do odpovědi
            IntegrateResults(providerResults, ref elevationResponse);
            
            return elevationResponse;
        }

        private static (bool existingUser, bool premiumUser) CheckApiKey(string key)
        {
            (string name, bool premium) = PostgreDbConnector.GetUser(key);
            return name == null ? (false, premium) : (true, premium);
        }

        private static IEnumerable<Location> ParseLocations(string locations)
        {
            string[] locationsSplit = locations.Split('|');
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

        private static IEnumerable<Location> GetPointsFromDb(IEnumerable<Location> locations, ref ElevationResponse elevationResponse, bool premiumUser, bool spheroid)
        {
            var locsWithoutElevation = new List<Location>();
            foreach (Result result in elevationResponse.result)
            {
                var closest = new ResultDistance();
                try
                {
                    closest = PostgreDbConnector.GetClosestPointsWithin(result.location, WITHIN_DISTANCE, premiumUser, spheroid);
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

        private static IEnumerable<Location> GetPointsFromDbParallel(IEnumerable<Location> locations, ref ElevationResponse elevationResponse, bool premiumUser, bool spheroid)
        {
            var locsWithoutElevation = new List<Location>();
            List<ResultDistance> resultDistances;
            try
            {
                resultDistances = PostgreDbConnector.GetClosestPointsWithinParallel(locations, WITHIN_DISTANCE, premiumUser, spheroid);
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
            var google = new GoogleElevationProvider();
            var seznam = new SeznamElevationProvider();
            var elevationTasks = new List<Task<IEnumerable<Result>>>()
            {
                google.GetElevationResultsAsync(locations),
                seznam.GetElevationResultsAsync(locations)
            };

            IEnumerable<Result>[] elevationResults = null;
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
            try
            {
                //int rowsAddedGoogle = PostgreDbConnector.InsertResultsParallel(googleResults, Source.Google);
                int rowsAddedSeznam = PostgreDbConnector.InsertResultsParallel(seznamResults, Source.Seznam);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                logger.Error(e);
            }


            switch (source)
            {
                case "google":
                    return googleResults;
                default:
                    return seznamResults;
            }
        }

        private static void IntegrateResults(List<Result> providerResults, ref ElevationResponse elevationResponse)
        {
            if (providerResults == null)
            {
                elevationResponse.status = ElevationResponses.INCOMPLETE;
            }

            foreach (Result result in elevationResponse.result)
            {
                if (result.elevation != -1) continue;
                Result providerResult = providerResults.Find(r => r.location.Equals(result.location));
                if (providerResult == null)
                {
                    //Google rád ořezává počet desetinných míst - kontrola s počtem, který Google vrací (ten je taky variabilní)
                    string number = providerResults[0].location.lat.ToString(CultureInfo.InvariantCulture);
                    int length = number.Substring(number.IndexOf(".")).Length - 1;
                    double lat = Math.Round(result.location.lat, length);
                    double lng = Math.Round(result.location.lng, length);
                    providerResult = providerResults.Find(r => r.location.Equals(new Location(lat, lng)));
                }
                result.elevation = providerResult.elevation;
                result.resolution = providerResult.resolution;
            }

            //Vrácení kompletní odpovědi
            elevationResponse.status = ElevationResponses.OK;
        }
    }
}