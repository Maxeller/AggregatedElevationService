using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using static System.Configuration.ConfigurationManager;

namespace AggregatedElevationService
{
    interface IElevationProvider
    {
        Task<IEnumerable<Result>> GetElevationResultsAsync(IEnumerable<Location> locations);
    }

    class GoogleElevationProvider : IElevationProvider
    {
        private const string BASE_URL = "https://maps.googleapis.com/maps/api/elevation/xml";
        //private const short URL_LENGTH_LIMIT = 8192;
        private const byte AVG_LENGTH = 35;
        private const byte BASE_URL_LENGTH = 94;
        private const short URL_CS_LIMIT = 7700;

        private static readonly string API_KEY = AppSettings["google_elevation_api"];

        private static HttpClient httpClient = new HttpClient();

        public async Task<IEnumerable<Result>> GetElevationResultsAsync(IEnumerable<Location> locations)
        {
            var results = new List<Result>();

            IEnumerable<string> requestUrls = CreateRequestUrl(locations);
            foreach (string requestUrl in requestUrls)
            {
                HttpResponseMessage response = await httpClient.GetAsync(requestUrl);
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

        private static IEnumerable<string> CreateRequestUrl(IEnumerable<Location> locations)
        {
            var urls = new List<string>();
            var sbLocs = new StringBuilder();
            foreach (Location location in locations)
            {
                sbLocs.AppendFormat(CultureInfo.InvariantCulture, "{0},{1}", location.lat, location.lng); 
                sbLocs.Append("|");
                if (sbLocs.Length + BASE_URL_LENGTH + AVG_LENGTH >= URL_CS_LIMIT)
                {
                    sbLocs.Remove(sbLocs.Length - 1, 1);
                    urls.Add($"{BASE_URL}?key={API_KEY}&locations={sbLocs}");
                    sbLocs.Clear();
                }
            }
            sbLocs.Remove(sbLocs.Length - 1, 1);
            urls.Add($"{BASE_URL}?key={API_KEY}&locations={sbLocs}");
            return urls;
        }

        private static IEnumerable<Result> ParseContent(string content)
        {
            var results = new List<Result>();

            XDocument xmlDocument = XDocument.Parse(content);
            string status = xmlDocument.XPathSelectElement("ElevationResponse/status")?.Value;
            if (status == "OK")
            {
                IEnumerable<XElement> responseResults = xmlDocument.XPathSelectElements("ElevationResponse/result");
                foreach (XElement responseResult in responseResults)
                {
                    if (responseResult == null) continue;

                    string latitude = responseResult.XPathSelectElement("location/lat")?.Value;
                    string longitude = responseResult.XPathSelectElement("location/lng")?.Value;
                    string elevation = responseResult.XPathSelectElement("elevation")?.Value;
                    string resolution = responseResult.XPathSelectElement("resolution")?.Value;

                    bool isLatParsed = double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                    bool isLngParsed = double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng);
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

    class SeznamElevationProvider : IElevationProvider
    {
        private const string BASE_URL = "https://api.mapy.cz/altitude";
        private const string SAMPLE_PAYLOAD = "yhECAWgLZ2V0QWx0aXR1ZGVYAVgCGAAAAAAAAC5AGAAAAAAAAElAOAEQ";
        private const string HEADER = "application/x-base64-frpc";

        private static HttpClient httpClient = new HttpClient();

        public async Task<IEnumerable<Result>> GetElevationResultsAsync(IEnumerable<Location> locations)
        {
            var results = new List<Result>();

            foreach (Location location in locations)
            {
                HttpResponseMessage response = await httpClient.PostAsync(BASE_URL, CreateContentWithPayload(location));
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
                string longitude = ((XElement)geometryCode?.FirstNode)?.Value;
                string elevation = xmlDocument.XPathSelectElement("//name[contains(text(),'altitudeCode')]/../value/array/data/value/double")?.Value;

                bool isLatParsed = double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                bool isLngParsed = double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng);
                bool isEleParsed = double.TryParse(elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out double ele);
                if (isLatParsed && isLngParsed && isEleParsed)
                {
                    return new Result(lat, lng, ele, 0);
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
