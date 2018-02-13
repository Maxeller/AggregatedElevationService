using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace AggregatedElevationService
{
    public class RequestHandler
    {
        string key;
        string locations;

        public RequestHandler(string key, string locations)
        {
            this.key = key;
            this.locations = locations;
        }

        public async Task<ElevationResponse> HandleRequest()
        {
            if (!CheckApiKey())
            {
                return new ElevationResponse("Invalid API key", null); //TODO: zkontrolovat
            }

            var parsedLocations = ParseLocations();
            var results = await GetElevation(parsedLocations);


            ElevationResponse elevationResponse = new ElevationResponse("OK", results.ToArray());

            return elevationResponse;
        }

        private bool CheckApiKey()
        {
            //TODO: dodělat
            return key == "klic";
        }

        private List<Location> ParseLocations()
        {
            string[] locationsSplit = locations.Split('|'); //TODO: kontrola formátování
            List<Location> latLongs = new List<Location>();
            foreach (string l in locationsSplit)
            {
                string[] locSplit = l.Split(',');
                var latParsed = double.TryParse(locSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                var lonParsed = double.TryParse(locSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon);

                if (!(latParsed && lonParsed)) continue; //TODO: exception?

                //Hodnoty jsou ve formátu WGS 84
                //Kontrola jestli je latitude (zeměpisná šírka) v rozsahu -90 až 90
                bool latFormatted = Math.Abs(lat) <= 90;
                //Kontrola jestli je longitude (zeměpisná délka) v rozsahu -180 až 180
                bool lonFormatted = Math.Abs(lon) <= 180;

                if(!(latFormatted && lonFormatted)) continue; //TODO: exception?

                Location loc = new Location(lat, lon);
                latLongs.Add(loc);
            }

            return latLongs;
        }

        private async Task<List<Result>> GetElevation(List<Location> locations) //TODO: dodělat
        {         
            List<Result> googleElevation = null;

            try
            {
                googleElevation = await GoogleElevationProvider.GetElevationResultsAsync(locations);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }


            return googleElevation;
        }
    }
}