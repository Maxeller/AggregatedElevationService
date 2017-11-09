using System.Xml.Serialization;

namespace AggregatedElevationService
{
    public class ElevationResponse
    {
        [XmlElement(ElementName = "status")]
        public string Status;
        [XmlElement(ElementName = "result")]
        public Result[] Results;

        public ElevationResponse()
        {
            
        }

        public ElevationResponse(string status, Result[] results)
        {
            this.Status = status;
            this.Results = results;
        }
    }

    public class Result
    {
        [XmlElement(ElementName = "location")]
        public Location Location;
        [XmlElement(ElementName = "elevation")]
        public double Elevation;
        [XmlElement(ElementName = "resolution")]
        public double Resolution;

        public Result()
        {
            
        }

        public Result(Location location, double elevation, double resolution)
        {
            this.Location = location;
            this.Elevation = elevation;
            this.Resolution = resolution;
        }

        public Result(double latitude, double longtitude, double elevation, double resolution)
        {
            this.Location = new Location(latitude, longtitude);
            this.Elevation = elevation;
            this.Resolution = resolution;
        }
    }

    public class Location
    {
        [XmlElement(ElementName = "lat")]
        public double Latitude;
        [XmlElement(ElementName = "lng")]
        public double Longtitude;

        public Location()
        {
            
        }

        public Location(double latitude, double longtitude)
        {
            this.Latitude = latitude;
            this.Longtitude = longtitude;
        }
    }
}
