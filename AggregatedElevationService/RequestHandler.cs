using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace AggregatedElevationService
{
    public class RequestHandler
    {
        public async Task<ElevationResponse> HandleRequest(string key, string locations)
        {
            if (!CheckApiKey(key))
            {
                return new ElevationResponse("Invalid API key", null); //TODO: zkontrolovat
            }

            List<Location> parsedLocations = ParseLocations(locations);
            List<Result> results = await GetElevation(parsedLocations);


            ElevationResponse elevationResponse = new ElevationResponse("OK", results.ToArray());

            return elevationResponse;
        }

        private bool CheckApiKey(string key)
        {
            //TODO: dodělat
            return key == "klic";
        }

        private List<Location> ParseLocations(string locations)
        {
            string[] locationsSplit = locations.Split('|'); //TODO: kontrola formátování
            List<Location> latLongs = new List<Location>();
            foreach (string l in locationsSplit)
            {
                string[] locSplit = l.Split(',');
                bool latParsed = double.TryParse(locSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                bool lonParsed = double.TryParse(locSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon);

                if (!(latParsed && lonParsed)) continue; //TODO: exception?

                //Hodnoty jsou ve formátu WGS 84
                //Kontrola jestli je latitude (zeměpisná šírka) v rozsahu -90 až 90
                bool latFormatted = Math.Abs(lat) <= 90;
                //Kontrola jestli je longitude (zeměpisná délka) v rozsahu -180 až 180
                bool lonFormatted = Math.Abs(lon) <= 180;

                if(!(latFormatted && lonFormatted)) continue; //TODO: exception?

                var loc = new Location(lat, lon);
                latLongs.Add(loc);
            }

            return latLongs;
        }

        private async Task<List<Result>> GetElevation(List<Location> locations) //TODO: dodělat
        {         
            var google = new GoogleElevationProvider();
            var seznam = new SeznamElevationProvider();
            List<Task<List<Result>>> tasks = new List<Task<List<Result>>>()
            {
                google.GetElevationResultsAsync(locations),
                seznam.GetElevationResultsAsync(locations)
            };
            List<Result> googleResults = null;
            List<Result> seznamResults = null;

            try
            {
                List<Result>[] a = await Task.WhenAll(tasks);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return googleResults;
        }
    }
}