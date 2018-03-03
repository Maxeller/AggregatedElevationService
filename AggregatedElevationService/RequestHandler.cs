using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AggregatedElevationService
{
    public class RequestHandler
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private const double MAX_DISTANCE = 2.0d; //TODO: určit jestli je to dostatečná vzálenost

        public async Task<ElevationResponse> HandleRequest(string key, string locations)
        {
            var (existingUser, premiumUser) = CheckApiKey(key);
            if (!existingUser)
            {
                return new ElevationResponse(ElevationResponses.INVALID_KEY, null);
            }

            IEnumerable<Location> parsedLocations = ParseLocations(locations);
            var elevationResponse = new ElevationResponse(parsedLocations);
            List<Location> locsWithoutElevation = new List<Location>();
            var pgc = new PostgreDbConnector();
            foreach (Result result in elevationResponse.result)
            {
                double elevation = -1;
                double resolution = -1;
                double distance = -1;
                try
                {
                    (elevation, resolution, distance) = pgc.GetClosestPoint(result.location, premiumUser);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    logger.Error(e);
                }
                
                if (distance <= MAX_DISTANCE && distance >= 0)
                {
                    result.elevation = elevation;
                    result.resolution = resolution;
                }
                else
                {
                    locsWithoutElevation.Add(result.location);
                }
            }

            if (locsWithoutElevation.Count == 0)
            {
                elevationResponse.status = ElevationResponses.OK;
                return elevationResponse;
            }

            List<Result> providerResults = await GetElevation(locsWithoutElevation);
            foreach (Result result in elevationResponse.result)
            {
                if (result.elevation != -1) continue;
                Result providerResult = providerResults.Find(r => r.location.Equals(result.location));
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
            List<Location> latLongs = new List<Location>();
            foreach (string l in locationsSplit)
            {
                string[] locSplit = l.Split(',');
                bool latParsed = double.TryParse(locSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                bool lonParsed = double.TryParse(locSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon);

                if (!(latParsed && lonParsed)) throw new ElevationProviderException("Location could not be parsed");

                //Hodnoty jsou ve formátu WGS 84
                //Kontrola jestli je latitude (zeměpisná šírka) v rozsahu -90 až 90
                bool latFormatted = Math.Abs(lat) <= 90;
                //Kontrola jestli je longitude (zeměpisná délka) v rozsahu -180 až 180
                bool lonFormatted = Math.Abs(lon) <= 180;

                if(!(latFormatted && lonFormatted)) throw new ElevationProviderException("Location is not formatted properly");

                var loc = new Location(lat, lon);
                latLongs.Add(loc);
            }

            return latLongs;
        }

        private async Task<List<Result>> GetElevation(List<Location> locations)
        {         
            var google = new GoogleElevationProvider(); //TODO: tohle by možná mohlo bejt schovaný v ElevationProvider s tim, že se daj vybrat ty provideři
            var seznam = new SeznamElevationProvider();
            List<Task<List<Result>>> elevationTasks = new List<Task<List<Result>>>()
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

            if (elevationResults == null) throw new ElevationProviderException("Elevation results were empty");

            List<Result> googleResults = elevationResults[0].ToList();
            List<Result> seznamResults = elevationResults[1].ToList();

            //TODO: určit, kterej je nejlepší

            //Uložení hodnot do databáze
            var pgc = new PostgreDbConnector();
            try
            {
                int rowsAddedGoogle = pgc.InsertResultsAsync(googleResults, Source.Google);
                int rowsAddedSeznam = pgc.InsertResultsAsync(seznamResults, Source.Seznam);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                logger.Error(e);
            }
            

            return googleResults; //TODO: zatim vrací jen věci od googlu
        }
    }
}