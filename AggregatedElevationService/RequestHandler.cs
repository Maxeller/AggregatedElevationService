﻿using System;
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
        private const double WITHIN_DISTANCE_APPROX = 5.0d;

        private bool approx = false;

        /// <summary>
        /// Zpracuje požadavek
        /// </summary>
        /// <param name="key">API klíč</param>
        /// <param name="locations">Řetezec lokací</param>
        /// <param name="source">Zdroj</param>
        /// <returns>Odpověď služby</returns>
        public async Task<ElevationResponse> HandleRequest(string key, string locations, string source)
        {
            if (source == "approx")
            {
                approx = true;
                source = null;
            }

            //Zjištění validity API klíče
            //Stopwatch s = Stopwatch.StartNew();
            (bool existingUser, bool premiumUser) = CheckApiKey(key);
            if (!existingUser)
            {
                return new ElevationResponse(ElevationResponses.INVALID_KEY, null);
            }
            //Console.WriteLine("User search: {0} ms",s.ElapsedMilliseconds);

            //Parsování a kontrola lokací v URL
            IEnumerable<Location> parsedLocations = ParseLocations(locations).ToList();
            //Console.WriteLine("Number of locs: {0}", parsedLocations.ToList().Count);
            //Vytvoření odpovědi s lokacemi 
            var elevationResponse = new ElevationResponse(parsedLocations);

            //Nalezení nejbližího bodu v DB (pokud nebyl nebyl přímo vybrán source)
            List<Location> locsWithoutElevation;
            if (source == null)
            {
                //s.Restart();
                locsWithoutElevation = (List<Location>) GetPointsFromDbParallel(parsedLocations, ref elevationResponse, premiumUser, false);
                //Console.WriteLine("Getting points from DB parallel: {0} ms", s.ElapsedMilliseconds);
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

            //Aproximace
            if (approx)
            {
                //s.Restart();
                locsWithoutElevation = (List<Location>) Approximate(locsWithoutElevation, ref elevationResponse, premiumUser, false);
                //Console.WriteLine("Approximation: {0} ms", s.ElapsedMilliseconds);
                if (locsWithoutElevation.Count == 0) return elevationResponse;
            }

            //Načtení hodnot (které nebyly nalezeny v DB) z externích poskytovatelů výškopisu
            List<Result> providerResults = null;
            try
            {
                //s.Restart();
                providerResults = await GetElevation(locsWithoutElevation, source);
                //Console.WriteLine("Getting info from external sources: {0} ms", s.ElapsedMilliseconds);
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

        /// <summary>
        /// Kontrola zdali se API klíč nachází v DB
        /// </summary>
        /// <param name="key">API klíč</param>
        /// <returns>Dvojice (existující uživatel, prémiový uživatel)</returns>
        private static (bool existingUser, bool premiumUser) CheckApiKey(string key)
        {
            (string name, bool premium) = PostgreDbConnector.GetUser(key);
            return name == null ? (false, premium) : (true, premium);
        }

        /// <summary>
        /// Rozparsuje řetezec lokací na kolekci lokací
        /// </summary>
        /// <param name="locations">Řetezec lokací</param>
        /// <returns>Kolekce lokací</returns>
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

        /// <summary>
        /// Provede pokus o aproximaci výšky zadaných lokací
        /// </summary>
        /// <param name="locations">kolekce lokací pro které chceme aproximovat výšku</param>
        /// <param name="elevationResponse">referencovaná odpověď do které chceme výsledky zapsat</param>
        /// <param name="premiumUser">Prémiový uživatel</param>
        /// <param name="spheroid">Použít sféroid pro výpočet vzdálenosti (pomalejší)</param>
        /// <returns>Kolekce lokací pro které nebyla aproximována výška</returns>
        private static IEnumerable<Location> Approximate(IEnumerable<Location> locations, ref ElevationResponse elevationResponse, bool premiumUser, bool spheroid)
        {
            var locsWithoutElevation = new List<Location>();
            foreach (Location location in locations)
            {
                Result result = Approximation.Average(location, WITHIN_DISTANCE_APPROX, premiumUser, spheroid);
                if (result == null)
                {
                    locsWithoutElevation.Add(location);
                    continue;
                }
                elevationResponse.result.Find(er => er.location.Equals(location)).elevation = result.elevation;
                elevationResponse.result.Find(er => er.location.Equals(location)).resolution = result.resolution;
            }

            return locsWithoutElevation;
        }

        /// <summary>
        /// Provede pokus o nalezení nejbližšího bodu a použití jeho výšky
        /// </summary>
        /// <param name="locations">kolekce lokací pro které chceme získat výšku</param>
        /// <param name="elevationResponse">referencovaná odpověď do které chceme výsledky zapsat</param>
        /// <param name="premiumUser">Prémiový uživatel</param>
        /// <param name="spheroid">Použít sféroid pro výpočet vzdálenosti (pomalejší)</param>
        /// <returns>Kolekce lokací pro které nebyla nalezena výška</returns>
        private static IEnumerable<Location> GetPointsFromDb(IEnumerable<Location> locations, ref ElevationResponse elevationResponse, bool premiumUser, bool spheroid)
        {
            var locsWithoutElevation = new List<Location>();
            foreach (Result result in elevationResponse.result)
            {
                var closest = new ResultDistance();
                try
                {
                    closest = PostgreDbConnector.GetClosestPointWithin(result.location, WITHIN_DISTANCE, premiumUser, spheroid);
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

        /// <summary>
        /// Paralelně provede pokus o nalezení nejbližšího bodu a použití jeho výšky
        /// </summary>
        /// <param name="locations">kolekce lokací pro které chceme získat výšku</param>
        /// <param name="elevationResponse">referencovaná odpověď do které chceme výsledky zapsat</param>
        /// <param name="premiumUser">Prémiový uživatel</param>
        /// <param name="spheroid">Použít sféroid pro výpočet vzdálenosti (pomalejší)</param>
        /// <returns>Kolekce lokací pro které nebyla nalezena výška</returns>
        private static IEnumerable<Location> GetPointsFromDbParallel(IEnumerable<Location> locations, ref ElevationResponse elevationResponse, bool premiumUser, bool spheroid)
        {
            var locsWithoutElevation = new List<Location>();
            List<ResultDistance> resultDistances;
            try
            {
                resultDistances = PostgreDbConnector.GetClosestPointWithinParallel(locations, WITHIN_DISTANCE, premiumUser, spheroid);
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

        /// <summary>
        /// Asynchroně získá výšky pro lokace od externích poskytovatelů výškopisu
        /// </summary>
        /// <param name="locations">kolekce lokací pro které chceme získat výšku</param>
        /// <param name="source">zdroj externích dat</param>
        /// <returns>kolekce výsledků</returns>
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

        /// <summary>
        /// Integruje výsledky od externího poskytovatele do odpovědi
        /// </summary>
        /// <param name="providerResults">výsledky od externího poskytovatele výškopisu</param>
        /// <param name="elevationResponse">referencována odpověď do které chceme výsledky zapsat</param>
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