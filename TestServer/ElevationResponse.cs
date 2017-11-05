using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestServer
{
    class ElevationResponse
    {
        string status;
        Result[] result;
    }

    class Result
    {
        Location location;
        double elevation;
        double resolution;
    }

    class Location
    {
        double lat;
        double lng;
    }
}
