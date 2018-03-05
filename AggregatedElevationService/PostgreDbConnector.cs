using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace AggregatedElevationService
{
    class PostgreDbConnector
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string DB_HOST = ConfigurationManager.AppSettings["db_host"];
        private static readonly string DB_USERNAME = ConfigurationManager.AppSettings["db_username"];
        private static readonly string DB_PASSWORD = ConfigurationManager.AppSettings["db_password"];
        private static readonly string DB_DATABASE = ConfigurationManager.AppSettings["db_database"];
        private static readonly string CONNECTION_STRING = $"Host={DB_HOST};Username={DB_USERNAME};Password={DB_PASSWORD};Database={DB_DATABASE}";

        private const short SRID_SJTSK = 5514;
        private const short SRID_WGS84 = 4326;

        public PostgreDbConnector()
        {
            
        }

        public void InitializeDatabase()
        {
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    //Použití rozšíření PostGIS
                    cmd.CommandText = "CREATE EXTENSION postgis";
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("PostGIS extension created");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        logger.Error(e);
                    }
                    //Vytvoření enumu pro určení zdroje
                    cmd.CommandText = "CREATE TYPE Source AS ENUM('google', 'seznam', 'file');";
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Source enum created");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        logger.Error(e);
                    }
                    //Vytvoření tabulky
                    cmd.CommandText =
                        "CREATE TABLE points (id bigserial NOT NULL, point geometry NOT NULL, latitude double precision," +
                        "longtitude double precision, elevation double precision, resolution double precision, Source Source NOT NULL," +
                        "time_added timestamp with time zone NOT NULL, CONSTRAINT pk_points_id PRIMARY KEY (id), CONSTRAINT point_source UNIQUE (point, Source))";
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Points table created");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        logger.Error(e);
                    }
                }
            }
        }

        public (double elevation, double resolution, double distance) GetClosestPoint(Location location, bool premium)
        {
            return GetClosestPoint(location.lat, location.lng, premium);
        }

        public (double elevation, double resolution, double distance) GetClosestPoint(double latitude, double longtitude, bool premium)
        {
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText =
                        "SELECT elevation, resolution, ST_Distance_Spheroid(points.point, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84), \'SPHEROID[\"WGS 84\",6378137,298.257223563]\') " +
                        "AS Distance FROM points " +
                        (premium ? "" : "WHERE Source != @file ") +
                        "ORDER BY Distance LIMIT 1";
                    cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, longtitude);
                    cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, latitude);
                    cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID_WGS84);
                    if (!premium) cmd.Parameters.AddWithValue("file", NpgsqlDbType.Enum, Source.File);
                    cmd.Prepare();
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            double elevation = reader.GetDouble(0);
                            double resolution = reader.GetDouble(1);
                            double distance = reader.GetDouble(2);
                            return (elevation, resolution, distance);
                        }
                    }
                }
            }
            return (-1, -1,-1);
        }

        public int InsertResults(IEnumerable<Result> results, Source source)
        {
            int rowCount = 0;
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                foreach (Result result in results)
                {
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = "INSERT INTO points(point, latitude, longtitude, elevation, resolution, Source, time_added) " +
                                          "SELECT Point.Result, ST_Y(Point.Result), ST_X(Point.Result), ST_Z(Point.Result), @res, @Source, now() " +
                                          "FROM(SELECT ST_SetSRID(ST_MakePoint(@lon, @lat, @ele), @wgs84) AS Result) AS Point";
                        cmd.Parameters.AddWithValue("lon", NpgsqlDbType.Double, result.location.lng);
                        cmd.Parameters.AddWithValue("lat", NpgsqlDbType.Double, result.location.lat);
                        cmd.Parameters.AddWithValue("ele", NpgsqlDbType.Double, result.elevation);
                        cmd.Parameters.AddWithValue("res", NpgsqlDbType.Double, result.resolution);
                        cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, source);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID_WGS84);
                        cmd.Prepare();
                        try
                        {
                            rowCount += cmd.ExecuteNonQuery();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            logger.Error(e);
                        }
                    }
                }   
            }
            return rowCount;
        }

        public int InsertResultsParallel(IEnumerable<Result> results, Source source)
        {
            int rowCount = 0;
            Parallel.ForEach(results, () => 0, (result, state, subCount) =>
                {
                    using (var conn = new NpgsqlConnection(CONNECTION_STRING))
                    {
                        conn.Open();
                        conn.MapEnum<Source>();
                        using (var cmd = new NpgsqlCommand())
                        {
                            cmd.Connection = conn;
                            cmd.CommandText =
                                "INSERT INTO points(point, latitude, longtitude, elevation, resolution, Source, time_added) " +
                                "SELECT Point.Result, ST_Y(Point.Result), ST_X(Point.Result), ST_Z(Point.Result), @res, @Source, now() " +
                                "FROM(SELECT ST_SetSRID(ST_MakePoint(@lon, @lat, @ele), @wgs84) AS Result) AS Point";
                            cmd.Parameters.AddWithValue("lon", NpgsqlDbType.Double, result.location.lng);
                            cmd.Parameters.AddWithValue("lat", NpgsqlDbType.Double, result.location.lat);
                            cmd.Parameters.AddWithValue("ele", NpgsqlDbType.Double, result.elevation);
                            cmd.Parameters.AddWithValue("res", NpgsqlDbType.Double, result.resolution);
                            cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, source);
                            cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID_WGS84);
                            cmd.Prepare();
                            try
                            {
                                subCount += cmd.ExecuteNonQuery();
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                                logger.Error(e);
                            }
                        }
                    }
                    return subCount;
                },
                (finalCount) => Interlocked.Add(ref rowCount, finalCount));
            return rowCount;
        }

        public int LoadXyzFile(string filepath)
        {
            IEnumerable<Xyz> xyzs = ExtractXyzs(filepath);
            int rowCount = 0;
            Stopwatch s = Stopwatch.StartNew(); //TODO: delete?
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                foreach (Xyz xyz in xyzs)
                {
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        //Transformace z S-JTSK (5514) -> WGS84 (4326) | Vytvoření pointu -> Nastavení S-JTSK -> Trasformace na WGS84
                        cmd.CommandText =
                            "INSERT INTO points(point, latitude, longtitude, elevation, resolution, Source, time_added) " +
                            "SELECT Transform.Result, ST_Y(Transform.Result), ST_X(Transform.Result), ST_Z(Transform.Result), 0, @Source, now() " +
                            "FROM(SELECT ST_Transform(ST_SetSRID(ST_MakePoint(@x, @y, @z), @jstk), @wgs84) AS Result) AS Transform";
                        cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, Source.File);
                        cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, xyz.x);
                        cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, xyz.y);
                        cmd.Parameters.AddWithValue("z", NpgsqlDbType.Double, xyz.z);
                        cmd.Parameters.AddWithValue("jstk", NpgsqlDbType.Smallint, SRID_SJTSK);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID_WGS84);
                        cmd.Prepare();
                        try
                        {
                            rowCount += cmd.ExecuteNonQuery();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            logger.Error(e);
                        }
                    }
                    if (rowCount == 100) break; //TODO: smazat
                }
            }
            s.Stop();
            Console.WriteLine("{0} rows added it took {1} ms", rowCount, s.ElapsedMilliseconds); //TODO: vylepšit výstup
            return rowCount;
        }

        public int LoadXyzFileParallel(string filepath)
        {
            IEnumerable<Xyz> xyzs = ExtractXyzs(filepath);
            int rowCount = 0;
            Parallel.ForEach(xyzs, () => 0, (xyz, state, subCount) =>
            {
                using (var conn = new NpgsqlConnection(CONNECTION_STRING))
                {
                    conn.Open();
                    conn.MapEnum<Source>();
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        //Transformace z S-JTSK (5514) -> WGS84 (4326) | Vytvoření pointu -> Nastavení S-JTSK -> Trasformace na WGS84
                        cmd.CommandText =
                            "INSERT INTO points(point, latitude, longtitude, elevation, resolution, Source, time_added) " +
                            "SELECT Transform.Result, ST_Y(Transform.Result), ST_X(Transform.Result), ST_Z(Transform.Result), 0, @Source, now() " +
                            "FROM(SELECT ST_Transform(ST_SetSRID(ST_MakePoint(@x, @y, @z), @jstk), @wgs84) AS Result) AS Transform";
                        cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, Source.File);
                        cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, xyz.x);
                        cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, xyz.y);
                        cmd.Parameters.AddWithValue("z", NpgsqlDbType.Double, xyz.z);
                        cmd.Parameters.AddWithValue("jstk", NpgsqlDbType.Smallint, SRID_SJTSK);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID_WGS84);
                        cmd.Prepare();
                        try
                        {
                            subCount += cmd.ExecuteNonQuery();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            logger.Error(e);
                        }
                    }
                }
                return subCount;
            }, (finalCount) => Interlocked.Add(ref rowCount, finalCount));
            return rowCount;
        }

        private static IEnumerable<Xyz> ExtractXyzs(string filepath)
        {
            var sr = new StreamReader(filepath);
            string line;
            List<Xyz> xyzs = new List<Xyz>();
            int lineNumber = 0;
            while ((line = sr.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Contains("  "))
                {
                    line = line.Replace("  ", "\t");
                }

                if (line.Contains(","))
                {
                    line = line.Replace(",", ".");
                }

                string[] lineSplit = line.Trim().Split('\t');
                bool xParsed = double.TryParse(lineSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double x);
                bool yParsed = double.TryParse(lineSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double y);
                bool zParsed = double.TryParse(lineSplit[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double z);
                if (xParsed && yParsed && zParsed)
                {
                    xyzs.Add(new Xyz(x, y, z));
                }
                else
                {
                    logger.Warn("Line no {0} could not be parsed. Line: {1}", lineNumber, line);
                }
            }
            sr.Close();
            return xyzs;
        }
    }

    struct Xyz
    {
        public readonly double x;
        public readonly double y;
        public readonly double z;

        public Xyz(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    enum Source
    {
        [PgName("google")] Google,
        [PgName("seznam")] Seznam,
        [PgName("file")] File
    }
}