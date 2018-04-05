using System;
using System.Collections.Generic;
using System.Text;
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

        public ElevationResponse(string status, List<Result> result)
        {
            this.status = status;
            this.result = result;
        }

        public ElevationResponse(IEnumerable<Location> locations)
        {
            result = new List<Result>();
            foreach (Location location in locations)
            {
                result.Add(new Result(location, -1, -1));
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Status: {status}");
            sb.AppendLine("Results:");
            foreach (Result r in result)
            {
                sb.AppendLine(r.ToString());
            }

            return sb.ToString();
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
            location = new Location(latitude, longtitude);
            this.elevation = elevation;
            this.resolution = resolution;
        }

        public override string ToString()
        {
            return $"Location: {location} Elevation: {elevation} Resolution: {resolution}";
        }
    }

    public class Location : IEquatable<Location>
    {
        public double lat;
        public double lng;

        public Location()
        {
            
        }

        public Location(double latitude, double longtitude)
        {
            lat = latitude;
            lng = longtitude;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Location);
        }

        public bool Equals(Location other)
        {
            return other != null && lat == other.lat && lng == other.lng;
        }

        public override int GetHashCode()
        {
            int hashCode = 2124363670;
            hashCode = hashCode * -1521134295 + lat.GetHashCode();
            hashCode = hashCode * -1521134295 + lng.GetHashCode();
            return hashCode;
        }

        public override string ToString()
        {
            return $"Lat: {lat} Lng: {lng}";
        }
    }

    public static class ElevationResponses
    {
        public const string OK = "OK";
        public const string KO = "KO";
        public const string INVALID_KEY = "Invalid API key";
        public const string INCOMPLETE = "Results are incomplete";
    }
}
