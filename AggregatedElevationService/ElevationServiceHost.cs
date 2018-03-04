using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Threading.Tasks;

namespace AggregatedElevationService
{
    [ServiceContract]
    class ElevationServiceHost
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        [OperationContract()]
        [WebGet(UriTemplate = "/xml?key={key}&locations={locations}", ResponseFormat = WebMessageFormat.Xml)] //TODO: možná sem přidat ještě &source={source} pro testování
        [XmlSerializerFormat()]
        public async Task<ElevationResponse> XmlRequest(string key, string locations) 
        {
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest; //TODO: asi vyřešit tenhle possible NullReferenceException
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request (XmlRequest) to {1}", System.DateTime.Now, uri);
            logger.Info("Request (XmlRequest) to {0}", uri);

            var elevationResponse = new ElevationResponse();
            var requestHandler = new RequestHandler();
            try
            {
                elevationResponse = await requestHandler.HandleRequest(key, locations);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                logger.Error(e);
                elevationResponse.status = e.Message;

            }

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
            logger.Info("Request (JsonRequest) to {0}", uri);

            var elevationResponse = new ElevationResponse();
            var requestHandler = new RequestHandler();
            try
            {
                elevationResponse = await requestHandler.HandleRequest(key, locations);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                logger.Error(e);
                elevationResponse.status = e.Message;

            }

            return elevationResponse;
        }

        
        [OperationContract()]
        [WebGet(UriTemplate = "*")] //TODO: tohle smazat nebo odeslat nějakou chybovou zprávu
        public ElevationResponse OtherUris(Message msg)
        {
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest;
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request caugth by OtherUris: {1}", System.DateTime.Now, uri);
            logger.Info("Request caugth by AllURIs: {0}", uri);

            /*
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
            */

            return new ElevationResponse(ElevationResponses.KO, null);
        }
    }
}
