using System;
using System.Collections.Specialized;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Threading.Tasks;

namespace AggregatedElevationService
{
    [ServiceContract]
    class ElevationProviderHost
    {
        [OperationContract()]
        [WebGet(UriTemplate = "/xml?key={key}&locations={locations}", ResponseFormat = WebMessageFormat.Xml)]
        [XmlSerializerFormat()]
        public async Task<ElevationResponse> XmlRequest(string key, string locations)
        {
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest; //TODO: asi vyřešit tenhle possible NullReferenceException
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request (XmlRequest) to {1}", System.DateTime.Now, uri); //TODO: logování do souboru (asi i podrobnější)

            var requestHandler = new RequestHandler(key, locations);
            var elevationResponse = await requestHandler.HandleRequest();

            //Message response = Message.CreateMessage(MessageVersion.None, "*", googleElevation); //TODO: hybrid na ntb nefunguje
            //return response;
            return elevationResponse;
        }

        [OperationContract()]
        [WebGet(UriTemplate = "/json?key={key}&locations={locations}", ResponseFormat = WebMessageFormat.Json)]
        public async Task<ElevationResponse> JsonRequest(string key, string locations)
        {
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest;
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request (JsonRequest) to {1}", System.DateTime.Now, uri);

            RequestHandler requestHandler = new RequestHandler(key, locations);
            var elevationResponse = await requestHandler.HandleRequest();

            return elevationResponse;
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
