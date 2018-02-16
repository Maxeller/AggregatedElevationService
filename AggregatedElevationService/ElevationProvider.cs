using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Globalization;
using System.IO;
using System.Net;
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

    class ElevationProviderException : Exception
    {
        public ElevationProviderException()
        {
            
        }

        public ElevationProviderException(string message) : base(message)
        {
            
        }

        public ElevationProviderException(string message, Exception inner) : base(message, inner)
        {
            
        }
    }

    class ElevationProvider //TODO: dodělat nebo smazat
    {

    }

    class GoogleElevationProvider
    {
        const string BASE_URL = "https://maps.googleapis.com/maps/api/elevation/xml";
        const string API_KEY = "AIzaSyBXNtwvKHCj4d-fkOr4rqhYloJRwISgR7g";

        //TODO: asi nějak pořešit ten limit (2500 dotazů na den)
        //TODO: možná rozdělit do více metod
        //TODO: problém https://developers.google.com/maps/terms 10.5 d)
        public async Task<List<Result>> GetElevationResultsAsync(List<Location> locations)
        {
            List<Result> myResults = new List<Result>();
            StringBuilder sbLocs = new StringBuilder();
            int n = 0;
            foreach (var location in locations)
            {
                sbLocs.AppendFormat(CultureInfo.InvariantCulture, "{0},{1}", location.lat, location.lng); //TODO: pořešit kolik se vejde do jednoho requestu
                if (n < locations.Count - 1)
                {
                    sbLocs.Append("|");
                }
                n++;
            }
            string requestUrl = BASE_URL + string.Format("?key={0}&locations={1}", API_KEY, sbLocs.ToString());
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(requestUrl);
                var content = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var xmlDocument = XDocument.Parse(content);
                    if (xmlDocument.XPathSelectElement("ElevationResponse/status")?.Value == "OK")
                    {
                        var results = xmlDocument.XPathSelectElements("ElevationResponse/result");
                        foreach (XElement result in results)
                        {
                            if (result == null) continue;

                            string latitude = result.XPathSelectElement("location/lat")?.Value;
                            string longtitude = result.XPathSelectElement("location/lng")?.Value;
                            string elevation = result.XPathSelectElement("elevation")?.Value;
                            string resolution = result.XPathSelectElement("resolution")?.Value;
                            bool isLatParsed = double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
                            bool isLngParsed = double.TryParse(longtitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng);
                            bool isEleParsed = double.TryParse(elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out double ele);
                            bool isResParsed = double.TryParse(resolution, NumberStyles.Float, CultureInfo.InvariantCulture, out double res);
                            if (isLatParsed && isLngParsed && isEleParsed && isResParsed)
                            { 
                                myResults.Add(new Result(lat, lng, ele, res));
                            }
                            else
                            {
                                throw new ElevationProviderException("Data couldnt be parsed: " + result); //TODO: tohle by možná měla bejt chyba do response
                            }
                        }
                    }
                    else
                    {
                        throw new ElevationProviderException("API call error: " + xmlDocument.XPathSelectElement("status")?.Value);
                    }
                }
                else
                {
                    throw new ElevationProviderException(string.Format("{0} - {1}", response.ReasonPhrase, response.RequestMessage));
                }   
            }

            if (myResults.Count > 0) //TODO: to tady asi bejt nemusí
            {
                return myResults;
            }
            else
            {
                return null;
            }
        }
    }

    class SeznamElevationProvider
    {
        const string BASE_URL = "https://api.mapy.cz/altitude";
        const string SAMPLE_PAYLOAD = "yhECAWgLZ2V0QWx0aXR1ZGVYAVgCGAAAAAAAAC5AGAAAAAAAAElAOAEQ";

        //TODO: problém https://api.mapy.cz/#pact 3.4 a 4.5
        public async Task<List<Result>> GetElevationResultsAsync(List<Location> locations)
        {
            List<Result> myResults = new List<Result>();

            using (var client = new HttpClient())
            { 
                foreach (var location in locations)
                {
                    var response = await client.PostAsync(BASE_URL, CreateContentWithPayload(location));
                    var content = await response.Content.ReadAsStringAsync();
                    //TODO: rozparsovat
                }
            }
            return myResults;
        }

        private HttpContent CreateContentWithPayload(Location location)
        {
            return CreateContent(CreatePayload(location));
        }

        private HttpContent CreateContent(byte[] payload)
        {
            HttpContent content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-base64-frpc");
            return content;
        }

        private byte[] CreatePayload(Location location)
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

            return Encoding.ASCII.GetBytes(System.Convert.ToBase64String(decodedPayload));
        }
    }
}
