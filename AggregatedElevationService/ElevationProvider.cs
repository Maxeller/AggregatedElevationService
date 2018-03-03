using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

namespace AggregatedElevationService
{
    interface IElevationProvider //TODO: dodělat nebo smazat
    {

    }

    class ElevationProvider //TODO: dodělat nebo smazat
    {

    }

    class GoogleElevationProvider
    {
        private const string BASE_URL = "https://maps.googleapis.com/maps/api/elevation/xml";
        private const string API_KEY = "AIzaSyBXNtwvKHCj4d-fkOr4rqhYloJRwISgR7g"; //TODO: asi do konfiguráku

        //TODO: asi nějak pořešit ten limit (2500 dotazů na den)
        //TODO: problém https://developers.google.com/maps/terms 10.5 d)
        public async Task<List<Result>> GetElevationResultsAsync(List<Location> locations) //TODO: static?
        {
            List<Result> results = new List<Result>();

            string requestUrl = CreateRequestUrl(locations); //TODO: pořešit kolik se vejde do jednoho requestu
            using (var client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(requestUrl);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    results.AddRange(ParseContent(content));
                }
                else
                {
                    throw new ElevationProviderException($"{response.ReasonPhrase} - {response.RequestMessage}");
                }   
            }

            return results;
        }

        private static string CreateRequestUrl(IReadOnlyCollection<Location> locations)
        {
            var sbLocs = new StringBuilder();
            int n = 0;
            foreach (Location location in locations)
            {
                sbLocs.AppendFormat(CultureInfo.InvariantCulture, "{0},{1}", location.lat, location.lng); 
                if (n < locations.Count - 1)
                {
                    sbLocs.Append("|");
                }
                n++;
            }
            return $"{BASE_URL}?key={API_KEY}&locations={sbLocs}";
        }

        private static IEnumerable<Result> ParseContent(string content)
        {
            List<Result> results = new List<Result>();

            XDocument xmlDocument = XDocument.Parse(content);
            string status = xmlDocument.XPathSelectElement("ElevationResponse/status")?.Value;
            if (status == "OK")
            {
                IEnumerable<XElement> responseResults = xmlDocument.XPathSelectElements("ElevationResponse/result");
                foreach (XElement responseResult in responseResults)
                {
                    if (responseResult == null) continue;

                    string latitude = responseResult.XPathSelectElement("location/lat")?.Value;
                    string longtitude = responseResult.XPathSelectElement("location/lng")?.Value;
                    string elevation = responseResult.XPathSelectElement("elevation")?.Value;
                    string resolution = responseResult.XPathSelectElement("resolution")?.Value;

                    bool isLatParsed = double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                    bool isLngParsed = double.TryParse(longtitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng);
                    bool isEleParsed = double.TryParse(elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out double ele);
                    bool isResParsed = double.TryParse(resolution, NumberStyles.Float, CultureInfo.InvariantCulture, out double res);
                    if (isLatParsed && isLngParsed && isEleParsed && isResParsed)
                    {
                        results.Add(new Result(lat, lng, ele, res));
                    }
                    else
                    {
                        throw new ElevationProviderException("Data could not be parsed: " + responseResult);
                    }
                }
            }
            else
            {
                throw new ElevationProviderException("API call error: " + status);
            }

            return results;
        }
    }

    class SeznamElevationProvider
    {
        private const string BASE_URL = "https://api.mapy.cz/altitude";
        private const string SAMPLE_PAYLOAD = "yhECAWgLZ2V0QWx0aXR1ZGVYAVgCGAAAAAAAAC5AGAAAAAAAAElAOAEQ";
        private const string HEADER = "application/x-base64-frpc";

        //TODO: problém https://api.mapy.cz/#pact 3.4 a 4.5
        public async Task<List<Result>> GetElevationResultsAsync(IEnumerable<Location> locations)
        {
            List<Result> results = new List<Result>();

            using (var client = new HttpClient())
            { 
                foreach (Location location in locations)
                {
                    HttpResponseMessage response = await client.PostAsync(BASE_URL, CreateContentWithPayload(location));
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        results.Add(ParseContent(content));
                    }
                    else
                    {
                        throw new ElevationProviderException($"{response.ReasonPhrase} - {response.RequestMessage}");
                    }
                }
            }
            return results;
        }

        private static Result ParseContent(string content)
        {
            XDocument xmlDocument = XDocument.Parse(content);
            string statusMessage = xmlDocument.XPathSelectElement("//name[contains(text(),'statusMessage')]/../value/string")?.Value;
            if (statusMessage == "OK")
            {
                XElement geometryCode = xmlDocument.XPathSelectElement("//name[contains(text(),'geometryCode')]/../value/array/data/value/array/data");
                string latitude = ((XElement)geometryCode?.LastNode)?.Value;
                string longtitude = ((XElement)geometryCode?.FirstNode)?.Value;
                string elevation = xmlDocument.XPathSelectElement("//name[contains(text(),'altitudeCode')]/../value/array/data/value/double")?.Value;
                //string resolution = responseResult.XPathSelectElement("resolution")?.Value; 

                bool isLatParsed = double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                bool isLngParsed = double.TryParse(longtitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng);
                bool isEleParsed = double.TryParse(elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out double ele);
                bool isResParsed = true;//double.TryParse(resolution, NumberStyles.Float, CultureInfo.InvariantCulture, out double res);
                if (isLatParsed && isLngParsed && isEleParsed && isResParsed)
                {
                    return new Result(lat, lng, ele, 1); //TODO: pořešit jakou má seznam teda přesnost
                }
                else
                {
                    throw new ElevationProviderException("Data could not be parsed: " + xmlDocument);
                }
            }
            else
            {
                throw new ElevationProviderException("API call error: " + statusMessage);
            }
        }

        private static HttpContent CreateContentWithPayload(Location location)
        {
            return CreateContent(CreatePayload(location));
        }

        private static HttpContent CreateContent(byte[] payload)
        {
            HttpContent content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue(HEADER);
            return content;
        }

        private static byte[] CreatePayload(Location location)
        {
            byte[] decodedPayload = Convert.FromBase64String(SAMPLE_PAYLOAD);

            byte[] byteArray = BitConverter.GetBytes(location.lng);
            for (int i = 22; i < 22 + byteArray.Length; i++)
            {
                decodedPayload[i] = byteArray[i - 22];
            }
            byteArray = BitConverter.GetBytes(location.lat);
            for (int i = 31; i < 31 + byteArray.Length; i++)
            {
                decodedPayload[i] = byteArray[i - 31];
            }

            return Encoding.ASCII.GetBytes(Convert.ToBase64String(decodedPayload));
        }
    }

    class ElevationProviderException : Exception
    {
        public ElevationProviderException(string message) : base(message)
        {

        }

        public ElevationProviderException(string message, Exception inner) : base(message, inner)
        {

        }
    }
}
