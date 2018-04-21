using System;
using System.Collections.Generic;

namespace AggregatedElevationService
{
    public static class Approximation
    {
        public static Result Average(Location location, double within, bool premium, bool spheroid)
        {
            List<ResultDistance> closest = PostgreDbConnector.GetClosestPointsWithin(location, within, premium, spheroid);

            double elevation = 0;
            double resolution = 0;

            foreach (ResultDistance resultDistance in closest)
            {
                if (resultDistance.Distance == -1) return null;
                elevation += resultDistance.Result.elevation;
                resolution = Math.Max(resolution, resultDistance.Distance);
            }

            elevation = elevation / closest.Count;

            return new Result(location, elevation, resolution);
        }
    }
}