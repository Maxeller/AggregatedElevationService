using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Device.Location;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TestServer
{
    [ServiceContract]
    class ElevationProviderHost
    {
        [OperationContract()]
        [WebGet(UriTemplate = "/xml?key={key}&locations={locations}")]
        public async Task<Message> XmlRequest(string key, string locations)
        {
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest;
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request (XmlRequest) to {1}", System.DateTime.Now, uri);

            string[] locationsSplit = locations.Split('|'); //TODO: kontrola formátování
            List<GeoCoordinate> latLongs = new List<GeoCoordinate>();
            foreach (string loc in locationsSplit)
            {
                string[] locSplit = loc.Split(','); 
                double.TryParse(locSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat); //TODO: kontrola jestli se hodnoty rozparsovali
                double.TryParse(locSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon);
                //TODO: kontrola správnosti údajů 
                GeoCoordinate geo = new GeoCoordinate(lat, lon);
                latLongs.Add(geo);
            }

            GeoCoordinate[] googleElevation = null;

            try
            {
                googleElevation = await GoogleElevationProvider.GetElevationResultsAsync(latLongs.ToArray());
                var i = googleElevation.Length;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            
           

            Message response = Message.CreateMessage(MessageVersion.None, "*", googleElevation);
            return response;
        }

        [OperationContract()]
        [WebGet(UriTemplate = "/json?key={key}&locations={latlongs}")]
        public Message JsonRequest(string key, string latlongs)
        {
            Console.WriteLine("{0}: Request caugth by JsonRequest", System.DateTime.Now);
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest;
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request to {1}", System.DateTime.Now, uri);
            NameValueCollection query = incomingWebRequestContext.UriTemplateMatch.QueryParameters;
            if (query.Count != 0)
            {
                Console.WriteLine("QueryString:");
                var enumerator = query.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    string name = enumerator.Current.ToString();
                    Console.WriteLine("{0} = {1}", name, query[name]);
                }
            }
            Message response = Message.CreateMessage(MessageVersion.None, "*", "Odpoved");
            OutgoingWebRequestContext outgoingWebRequestContext = webOperationContext.OutgoingRequest;
            outgoingWebRequestContext.Headers.Add("MyCustomHeader", "JsonRequest");
            return response;
        }

        [OperationContract()]
        [WebGet(UriTemplate = "*")] //TODO: tohle smazat nebo odeslat nějakou chybovou zprávu
        Message AllURIs(Message msg)
        {
            Console.WriteLine("{0}: Request caugth by AllURIs", System.DateTime.Now);
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest;
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request to {1}", System.DateTime.Now, uri);
            if (incomingWebRequestContext.Method != "GET")
            {
                Console.WriteLine("{0}: Incoming Message {1} with method of {2}", System.DateTime.Now,
                    msg.GetReaderAtBodyContents().ReadOuterXml(), incomingWebRequestContext.Method);
            }
            else
            {
                Console.WriteLine("{0}: GET Req - no message in body", System.DateTime.Now);
            }
            NameValueCollection query = incomingWebRequestContext.UriTemplateMatch.QueryParameters;
            if (query.Count != 0)
            {
                Console.WriteLine("QueryString:");
                var enumerator = query.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    string name = enumerator.Current.ToString();
                    Console.WriteLine("{0} = {1}", name, query[name]);
                }
            }
            Message response = Message.CreateMessage(MessageVersion.None, "*", "Odpoved");
            OutgoingWebRequestContext outgoingWebRequestContext = webOperationContext.OutgoingRequest;
            outgoingWebRequestContext.Headers.Add("MyCustomHeader", "Hodnota");
            return response;
        }
    }
}
