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
        [WebGet(UriTemplate = "/xml?key={key}&locations={locations}", ResponseFormat = WebMessageFormat.Xml)] //TODO: možná sem přidat ještě &Source={Source} pro testování
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
        [WebGet(UriTemplate = "*")]
        public ElevationResponse OtherUris(Message msg)
        {
            WebOperationContext webOperationContext = WebOperationContext.Current;
            IncomingWebRequestContext incomingWebRequestContext = webOperationContext.IncomingRequest;
            string uri = incomingWebRequestContext.UriTemplateMatch.RequestUri.ToString();
            Console.WriteLine("{0}: Request caugth by OtherUris: {1}", System.DateTime.Now, uri);
            logger.Info("Request caugth by AllURIs: {0}", uri);

            return new ElevationResponse(ElevationResponses.KO, null);
        }
    }
}
