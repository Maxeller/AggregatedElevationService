using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Device.Location;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using System.Xml.XPath;

namespace TestServer
{
    interface IElevationProvider
    {
        void GetElevationResults(ref GeoCoordinate[] latlongs);
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

    class ElevationProvider
    {

    }

    class GoogleElevationProvider
    {
        const string baseUrl = "https://maps.googleapis.com/maps/api/elevation/xml";
        const string apiKey = "AIzaSyBXNtwvKHCj4d-fkOr4rqhYloJRwISgR7g";

        public void GetElevationResults(ref GeoCoordinate[] latlongs)
        {
            StringBuilder sbLocs = new StringBuilder();
            int n = 0;
            foreach (GeoCoordinate latlong in latlongs)
            {
                sbLocs.Append(string.Format("{0},{1}", latlong.Latitude, latlong.Longitude));
                if (n < latlongs.Length-1) sbLocs.Append("|");
                n++;
            }
            string requestUrl = baseUrl + string.Format("?key={0}&locations={1}", apiKey, sbLocs.ToString());
            WebRequest request = WebRequest.Create(requestUrl);
            WebResponse response = request.GetResponse();
            XDocument xdoc = XDocument.Load(response.GetResponseStream());
        }

        public static async Task<GeoCoordinate[]> GetElevationResultsAsync(GeoCoordinate[] latlongs)
        {
            StringBuilder sbLocs = new StringBuilder();
            int n = 0;
            foreach (var latlong in latlongs)
            {
                sbLocs.AppendFormat(CultureInfo.InvariantCulture, "{0},{1}", latlong.Latitude, latlong.Longitude);
                if (n < latlongs.Length - 1)
                {
                    sbLocs.Append("|");
                }
                n++;
            }
            string requestUrl = baseUrl + string.Format("?key={0}&locations={1}", apiKey, sbLocs.ToString());
            using (var client = new HttpClient())
            {
                
                var request = await client.GetAsync(requestUrl);
                var content = await request.Content.ReadAsStringAsync();
                if (request.StatusCode == HttpStatusCode.OK)
                {
                    var xmlDocument = XDocument.Parse(content);
                    if (xmlDocument.XPathSelectElement("ElevationResponse/status")?.Value == "OK") //TODO: NEFUNGUJE KURVA DRÁT .. prej descendats 
                    {
                        var results = xmlDocument.XPathSelectElements("ElevationResponse/result");
                        int i = 0;
                        foreach (XElement result in results)
                        {
                            if (result != null) //TODO: nevim jestli to tam jen tak házet nebo to vytvářet na novo ale mělo by to snad jít za sebou
                            {
                                string elevation = result.XPathSelectElement("elevation")?.Value;
                                string resolution = result.XPathSelectElement("resolution")?.Value;
                                bool isEleParsed = double.TryParse(elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out double ele);
                                bool isResParsed = double.TryParse(resolution, NumberStyles.Float, CultureInfo.InvariantCulture, out double res);
                                if (isEleParsed && isResParsed)
                                { 
                                    latlongs[0].Altitude = ele;
                                    latlongs[0].VerticalAccuracy = res;
                                }
                                else
                                {
                                    throw new ElevationProviderException("Data couldnt be parsed: " + result);
                                }
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
                    throw new ElevationProviderException(string.Format("{0} - {1}", request.ReasonPhrase, request.RequestMessage));
                }   
            }

            return latlongs;
        }
    }
}
