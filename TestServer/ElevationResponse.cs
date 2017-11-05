using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TestServer
{
    public class ElevationResponse
    {
        //[XmlElement(ElementName = "status")]
        public string status;
        [XmlElement]
        public Result[] result;

        public ElevationResponse()
        {
            
        }

        public ElevationResponse(string status, Result[] results)
        {
            this.status = status;
            this.result = results;
        }
    }

    public class Result
    {
        //[XmlElement(ElementName = "location")]
        public Location location;
        //[XmlElement(ElementName = "elevation")]
        public double elevation;
        //[XmlElement(ElementName = "resolution")]
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
        //[XmlElement(ElementName = "lat")]
        public double lat;
        //[XmlElement(ElementName = "lng")]
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
