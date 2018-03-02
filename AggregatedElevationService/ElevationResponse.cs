using System.Collections.Generic;
using System.Xml.Serialization;

namespace AggregatedElevationService
{
    public class ElevationResponse
    {
        public string status;
        [XmlElement]
        public List<Result> result;

        public ElevationResponse()
        {
            
        }

        public ElevationResponse(string status, List<Result> results)
        {
            this.status = status;
            this.result = results;
        }
    }

    public class Result
    {
        public Location location;
        public double elevation;
        public double resolution;

        public Result()
        {
            
        }

        public Result(Location location, double elevation, double resolution)
        {
            this.location = location;
            this.elevation = elevation;
            this.resolution = resolution;
        }

        public Result(double latitude, double longtitude, double elevation, double resolution)
        {
            this.location = new Location(latitude, longtitude);
            this.elevation = elevation;
            this.resolution = resolution;
        }
    }

    public class Location
    {
        public double lat;
        public double lng;

        public Location()
        {
            
        }

        public Location(double latitude, double longtitude)
        {
            this.lat = latitude;
            this.lng = longtitude;
        }
    }
}
