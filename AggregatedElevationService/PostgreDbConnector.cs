﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using static System.Configuration.ConfigurationManager;

namespace AggregatedElevationService
{
    class PostgreDbConnector
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string DB_HOST = AppSettings["db_host"];
        private static readonly string DB_PORT = AppSettings["db_port"];
        private static readonly string DB_USERNAME = AppSettings["db_username"];
        private static readonly string DB_PASSWORD = AppSettings["db_password"];
        private static readonly string DB_DATABASE = AppSettings["db_database"];
        private static readonly string CONNECTION_STRING = $"Host={DB_HOST};Port={DB_PORT};Username={DB_USERNAME};Password={DB_PASSWORD};Database={DB_DATABASE};ApplicationName=AggregatedElevationService";

        /// <summary>
        /// Inicializuje databázi s názvem uvedeném v konfiguračním souboru.
        /// Vytvoří rozšíření PostGIS, vytvoří enum pro určení zdroje dat, vytvoří tabulku
        /// </summary>
        public static void InitializeDatabase()
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
                    cmd.CommandText = "CREATE TYPE Source AS ENUM('google', 'seznam', 'file')";
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
                    //Vytvoření tabulek
                    //Tabulka bodů
                    cmd.CommandText =
                        "CREATE TABLE points (id bigserial NOT NULL, point geometry NOT NULL, latitude double precision," +
                        "longitude double precision, elevation double precision, resolution double precision, Source Source NOT NULL," +
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
                    //Tabulka uživatelů
                    cmd.CommandText =
                        "CREATE TABLE users (id serial NOT NULL, name text NOT NULL, api_key CHAR(64) NOT NULL, premium_user BOOLEAN NOT NULL," +
                        "time_added timestamp with time zone NOT NULL, CONSTRAINT pk_users_id PRIMARY KEY (id))";
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Users table created");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        logger.Error(e);
                    }
                    //Vytvoření indexu
                    cmd.CommandText =
                        "CREATE INDEX points_gix ON points USING GIST (point)";
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Index created");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        logger.Error(e);
                    }
                }
            }
            //Přidání testovacích uživatelů
            InsertUser("Test Free", false);
            InsertUser("Test Premium", true);
            InsertUser("Taitale Mission Editor", true);
        }

        /// <summary>
        /// Vloží uživatele do databáze a vytvořímu API klíč z SHA256
        /// </summary>
        /// <param name="name">Jméno uživatele</param>
        /// <param name="premium">Prémiový uživatel</param>
        public static void InsertUser(string name, bool premium)
        {
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "INSERT INTO users(name, api_key, premium_user, time_added) VALUES (@name, @key, @premium, now())";
                    cmd.Parameters.AddWithValue("name", NpgsqlDbType.Text, name);
                    cmd.Parameters.AddWithValue("key", NpgsqlDbType.Char, Helper.GetHashSha256(name));
                    cmd.Parameters.AddWithValue("premium", NpgsqlDbType.Boolean, premium);
                    try
                    {
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        logger.Error(e);
                    }
                }
            }
        }

        /// <summary>
        /// Získá jméno a indikátor prémiového uživatele na základě API klíče
        /// </summary>
        /// <param name="apiKey">API klíč</param>
        /// <returns>Dvojice (jméno, premium)</returns>
        public static (string name, bool premium) GetUser(string apiKey)
        {
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT name, premium_user FROM users WHERE api_key = @key";
                    cmd.Parameters.AddWithValue("key", NpgsqlDbType.Char, apiKey);
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.HasRows) return (null, false);
                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            bool premium = reader.GetBoolean(1);
                            return (name, premium);
                        }
                    }
                }
            }
            return (null, false);
        }

        /// <summary>
        /// Vrátí výsledek s lokací, která má výšku nejbližího bodu, přesnost a jeho vzdálenost od zadané lokace.
        /// </summary>
        /// <param name="location">Lokace</param>
        /// <param name="within">Vzdálenost ve které bod hledat</param>
        /// <param name="premium">Prohledávat hodnoty nahrané ze souboru</param>
        /// <param name="spheroid">Použití přesnějšího měření vzdálenosti (pomalejší)</param>
        /// <returns>výsledek s lokací, která má výšku nejbližího bodu, přesnost a jeho vzdálenost od zadané lokace</returns>
        public static ResultDistance GetClosestPointWithin(Location location, double within , bool premium, bool spheroid)
        {
            return GetClosestPointWithin(location.lat, location.lng, within, premium, spheroid);
        }

        /// <summary>
        /// Vrátí výsledek s lokací (ze zadané zem. šířky a výšky), která má výšku nejbližího bodu, přesnost a jeho vzdálenost od zadané zem. šířky a výšky.
        /// </summary>
        /// <param name="latitude">zeměpisná šířka</param>
        /// <param name="longitude">zeměpisná šířka</param>
        /// <param name="within">Vzdálenost ve které bod hledat</param>
        /// <param name="premium">Prohledávat hodnoty nahrané ze souboru</param>
        /// <param name="spheroid">Použití přesnějšího měření vzdálenosti (pomalejší)</param>
        /// <returns>Výsledek s lokací (ze zadané zem. šířky a výšky), která má výšku nejbližího bodu, přesnost a jeho vzdálenost od zadané zem. šířky a výšky</returns>
        public static ResultDistance GetClosestPointWithin(double latitude, double longitude, double within, bool premium, bool spheroid)
        {
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    if (!spheroid)
                    {
                        cmd.CommandText =
                            "SELECT elevation, resolution, ST_DistanceSphere(point, Input) as Distance FROM (" +
                            "SELECT *, ST_DWithin(point::geography, Input::geography, @within, false) as Within FROM (" +
                            "SELECT *, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84) as Input FROM points) as mp) as dw " +
                            "WHERE Within = true " +
                            (premium ? "" : "AND Source != @file ") +
                            "ORDER BY Distance LIMIT 1";
                    }
                    else
                    {
                        cmd.CommandText =
                            "SELECT elevation, resolution, ST_DistanceSpheroid(point, input, \'SPHEROID[\"WGS 84\",6378137,298.257223563]\') as Distance FROM (" +
                            "SELECT *, ST_DWithin(point::geography, input::geography, @within) as Within FROM (" +
                            "SELECT *, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84) as input FROM points) as mp) as dw " +
                            "WHERE Within = true " +
                            (premium ? "" : "AND Source != @file ") +
                            "ORDER BY Distance LIMIT 1";
                    }

                    cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, longitude);
                    cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, latitude);
                    cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
                    cmd.Parameters.AddWithValue("within", NpgsqlDbType.Double, within);
                    if (!premium) cmd.Parameters.AddWithValue("file", NpgsqlDbType.Enum, Source.File);
                    cmd.Prepare();
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.HasRows) return new ResultDistance(new Result(latitude, longitude, -1, -1), -1);
                        while (reader.Read())
                        {
                            double elevation = reader.GetDouble(0);
                            double resolution = reader.GetDouble(1);
                            double distance = reader.GetDouble(2);
                            return new ResultDistance(
                                new Result(latitude, longitude, elevation, resolution != 0 ? resolution : -1),
                                distance);
                        }
                    }
                }
            }
            return new ResultDistance(new Result(latitude, longitude, -1, -1), -1);
        }

        /// <summary>
        /// Vrátí kolekci lokací s výškou nejbližího bodu, přesnost a jeho vzdálenost od zadané lokace.
        /// </summary>
        /// <param name="locations">Kolekce lokací</param>
        /// <param name="within">Vzdálenost ve které bod hledat</param>
        /// <param name="premium">Prohledávat hodnoty nahrané ze souboru</param>
        /// <param name="spheroid">Použití přesnějšího měření vzdálenosti (pomalejší)</param>
        /// <returns>List lokací s výškou nejbližího bodu, přesnost a jeho vzdálenost od zadané lokace</returns>
        public static List<ResultDistance> GetClosestPointWithinParallel(IEnumerable<Location> locations, double within, bool premium, bool spheroid)
        {
            var results = new ConcurrentBag<ResultDistance>();
            Parallel.ForEach(locations, new ParallelOptions(){MaxDegreeOfParallelism = 4} , location =>
            {
                using (var conn = new NpgsqlConnection(CONNECTION_STRING))
                {
                    conn.Open();
                    conn.MapEnum<Source>();
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        if (!spheroid)
                        {
                            cmd.CommandText =
                                "SELECT elevation, resolution, ST_DistanceSphere(point, Input) as Distance FROM (" +
                                "SELECT *, ST_DWithin(point::geography, Input::geography, @within, false) as Within FROM (" +
                                "SELECT *, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84) as Input FROM points) as mp) as dw " +
                                "WHERE Within = true " +
                                (premium ? "" : "AND Source != @file ") +
                                "ORDER BY Distance LIMIT 1";
                        }
                        else
                        {
                            cmd.CommandText =
                                "SELECT elevation, resolution, ST_DistanceSpheroid(point, input, \'SPHEROID[\"WGS 84\",6378137,298.257223563]\') as Distance FROM (" +
                                "SELECT *, ST_DWithin(point::geography, input::geography, @within) as Within FROM (" +
                                "SELECT *, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84) as input FROM points) as mp) as dw " +
                                "WHERE Within = true " +
                                (premium ? "" : "AND Source != @file ") +
                                "ORDER BY Distance LIMIT 1";
                        }

                        cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, location.lng);
                        cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, location.lat);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
                        cmd.Parameters.AddWithValue("within", NpgsqlDbType.Double, within);
                        if (!premium) cmd.Parameters.AddWithValue("file", NpgsqlDbType.Enum, Source.File);
                        cmd.Prepare();
                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (!reader.HasRows) results.Add(new ResultDistance(new Result(location, -1, -1), -1));
                            while (reader.Read())
                            {
                                double elevation = reader.GetDouble(0);
                                double resolution = reader.GetDouble(1);
                                double distance = reader.GetDouble(2);
                                results.Add(new ResultDistance(new Result(location, elevation, resolution != 0 ? resolution : -1), distance));
                            }
                        }
                    }
                }
            });

            return results.ToList();
        }

        /// <summary>
        /// Vrátí kolekci nejbližšího bodů k zadané lokaci
        /// </summary>
        /// <param name="location">Lokace</param>
        /// <param name="within">Vzdálenost ve které bod hledat</param>
        /// <param name="premium">Prohledávat hodnoty nahrané ze souboru</param>
        /// <param name="spheroid">Použití přesnějšího měření vzdálenosti (pomalejší)</param>
        /// <returns>List s výsledky a vzdáleností od zadané lokace</returns>
        public static List<ResultDistance> GetClosestPointsWithin(Location location, double within, bool premium, bool spheroid)
        {
            List<ResultDistance> results = new List<ResultDistance>();
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    if (!spheroid)
                    {
                        cmd.CommandText =
                            "SELECT latitude, longitude, elevation, resolution, ST_DistanceSphere(point, Input) as Distance FROM (" +
                            "SELECT *, ST_DWithin(point::geography, Input::geography, @within, false) as Within FROM (" +
                            "SELECT *, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84) as Input FROM points) as mp) as dw " +
                            "WHERE Within = true " +
                            (premium ? "" : "AND Source != @file ") +
                            "ORDER BY Distance";
                    }
                    else
                    {
                        cmd.CommandText =
                            "SELECT latitude, longitude, elevation, resolution, ST_DistanceSpheroid(point, input, \'SPHEROID[\"WGS 84\",6378137,298.257223563]\') as Distance FROM (" +
                            "SELECT *, ST_DWithin(point::geography, input::geography, @within) as Within FROM (" +
                            "SELECT *, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84) as input FROM points) as mp) as dw " +
                            "WHERE Within = true " +
                            (premium ? "" : "AND Source != @file ") +
                            "ORDER BY Distance";
                    }

                    cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, location.lng);
                    cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, location.lat);
                    cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
                    cmd.Parameters.AddWithValue("within", NpgsqlDbType.Double, within);
                    if (!premium) cmd.Parameters.AddWithValue("file", NpgsqlDbType.Enum, Source.File);
                    cmd.Prepare();
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.HasRows) results.Add(new ResultDistance(new Result(null, -1, -1), -1));
                        while (reader.Read())
                        {
                            double latitude = reader.GetDouble(0);
                            double longitude = reader.GetDouble(1);
                            double elevation = reader.GetDouble(2);
                            double resolution = reader.GetDouble(3);
                            double distance = reader.GetDouble(4);
                            results.Add(new ResultDistance(new Result(latitude, longitude, elevation, resolution), distance));
                        }
                    }
                }
            }

            return results;
        }

        [Obsolete("Please use method GetClosestPointWithin")]
        public static ResultDistance GetClosestPoint(Location location, bool premium, bool spheroid)
        {
            return GetClosestPoint(location.lat, location.lng, premium, spheroid);
        }

        [Obsolete("Please use method GetClosestPointWithin")]
        public static ResultDistance GetClosestPoint(double latitude, double longitude, bool premium, bool spheroid)
        {
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    if (spheroid)
                    {
                        cmd.CommandText =
                            "SELECT elevation, resolution, ST_DistanceSpheroid(points.point, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84), \'SPHEROID[\"WGS 84\",6378137,298.257223563]\') " +
                            "AS Distance FROM points " +
                            (premium ? "" : "WHERE Source != @file ") +
                            "ORDER BY Distance LIMIT 1";
                    }
                    else
                    {
                        cmd.CommandText =
                            "SELECT elevation, resolution, ST_DistanceSphere(points.point, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84)) " +
                            "AS Distance FROM points " +
                            (premium ? "" : "WHERE Source != @file ") +
                            "ORDER BY Distance LIMIT 1";
                    }
                    cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, longitude);
                    cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, latitude);
                    cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
                    if (!premium) cmd.Parameters.AddWithValue("file", NpgsqlDbType.Enum, Source.File);
                    cmd.Prepare();
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.HasRows) return new ResultDistance(new Result(latitude, longitude, -1, -1), -1);
                        while (reader.Read())
                        {
                            double elevation = reader.GetDouble(0);
                            double resolution = reader.GetDouble(1);
                            double distance = reader.GetDouble(2);
                            return new ResultDistance(new Result(latitude, longitude, elevation, resolution != 0 ? resolution : -1), distance);
                        }
                    }
                }
            }
            return new ResultDistance(new Result(latitude, longitude, -1, -1), -1);
        }

        [Obsolete("Please use method GetClosestPointWithinParallel")]
        public static List<ResultDistance> GetClosestPointParallel(IEnumerable<Location> locations, bool premium, bool spheroid)
        {
            var results = new ConcurrentBag<ResultDistance>();
            Parallel.ForEach(locations, new ParallelOptions(){MaxDegreeOfParallelism = 4}, location =>
            {
                using (var conn = new NpgsqlConnection(CONNECTION_STRING))
                {
                    conn.Open();
                    conn.MapEnum<Source>();
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        if (spheroid)
                        {
                            cmd.CommandText =
                                "SELECT elevation, resolution, ST_DistanceSpheroid(points.point, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84), \'SPHEROID[\"WGS 84\",6378137,298.257223563]\') " +
                                "AS Distance FROM points " +
                                (premium ? "" : "WHERE Source != @file ") +
                                "ORDER BY Distance LIMIT 1";
                        }
                        else
                        {
                            cmd.CommandText =
                                "SELECT elevation, resolution, ST_DistanceSphere(points.point, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84)) " +
                                "AS Distance FROM points " +
                                (premium ? "" : "WHERE Source != @file ") +
                                "ORDER BY Distance LIMIT 1";
                        }
                        cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, location.lng);
                        cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, location.lat);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
                        if (!premium) cmd.Parameters.AddWithValue("file", NpgsqlDbType.Enum, Source.File);
                        cmd.Prepare();
                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (!reader.HasRows) results.Add(new ResultDistance(new Result(location, -1, -1), -1));
                            while (reader.Read())
                            {
                                double elevation = reader.GetDouble(0);
                                double resolution = reader.GetDouble(1);
                                double distance = reader.GetDouble(2);
                                results.Add(new ResultDistance(new Result(location, elevation, resolution != 0 ? resolution : -1), distance));
                            }
                        }
                    }
                }
            });

            return results.ToList();
        }

        /// <summary>
        /// Vloží výsledky externího poskytovatele výškopisu do tabulky bodů
        /// </summary>
        /// <param name="results">Kolekce výsledků</param>
        /// <param name="source">Externí poskytovatel výškopisu</param>
        /// <returns>Počet zapsaných řádků</returns>
        public static int InsertResults(IEnumerable<Result> results, Source source)
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
                        cmd.CommandText = "INSERT INTO points(point, latitude, longitude, elevation, resolution, Source, time_added) " +
                                          "SELECT Point.Result, ST_Y(Point.Result), ST_X(Point.Result), ST_Z(Point.Result), @res, @Source, now() " +
                                          "FROM(SELECT ST_SetSRID(ST_MakePoint(@lon, @lat, @ele), @wgs84) AS Result) AS Point";
                        cmd.Parameters.AddWithValue("lon", NpgsqlDbType.Double, result.location.lng);
                        cmd.Parameters.AddWithValue("lat", NpgsqlDbType.Double, result.location.lat);
                        cmd.Parameters.AddWithValue("ele", NpgsqlDbType.Double, result.elevation);
                        cmd.Parameters.AddWithValue("res", NpgsqlDbType.Double, result.resolution);
                        cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, source);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
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

        /// <summary>
        /// Paralelně vloží výsledky externího poskytovatele výškopisu do tabulky bodů
        /// </summary>
        /// <param name="results">Kolekce výsledků</param>
        /// <param name="source">Externí poskytovatel výškopisu</param>
        /// <returns>Počet zapsaných řádků</returns>
        public static int InsertResultsParallel(IEnumerable<Result> results, Source source)
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
                                "INSERT INTO points(point, latitude, longitude, elevation, resolution, Source, time_added) " +
                                "SELECT Point.Result, ST_Y(Point.Result), ST_X(Point.Result), ST_Z(Point.Result), @res, @Source, now() " +
                                "FROM(SELECT ST_SetSRID(ST_MakePoint(@lon, @lat, @ele), @wgs84) AS Result) AS Point";
                            cmd.Parameters.AddWithValue("lon", NpgsqlDbType.Double, result.location.lng);
                            cmd.Parameters.AddWithValue("lat", NpgsqlDbType.Double, result.location.lat);
                            cmd.Parameters.AddWithValue("ele", NpgsqlDbType.Double, result.elevation);
                            cmd.Parameters.AddWithValue("res", NpgsqlDbType.Double, result.resolution);
                            cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, source);
                            cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
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

        /// <summary>
        /// Zapíše data z XYZ souboru do tabulky bodů
        /// </summary>
        /// <param name="filepath">Cesta k souboru</param>
        /// <param name="inputSrid">SRID dat v souboru</param>
        /// <returns>Počet zapsaných řádků</returns>
        public static int LoadXyzFile(string filepath, SRID inputSrid)
        {
            IEnumerable<Xyz> xyzs = ExtractXyzs(filepath);
            int rowCount = 0;
            Stopwatch s = Stopwatch.StartNew();
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
                            "INSERT INTO points(point, latitude, longitude, elevation, resolution, Source, time_added) " +
                            "SELECT Transform.Result, ST_Y(Transform.Result), ST_X(Transform.Result), @z, 0, @Source, now() " +
                            "FROM(SELECT ST_Transform(ST_SetSRID(ST_MakePoint(@x, @y), @input_srid), @wgs84) AS Result) AS Transform";
                        cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, Source.File);
                        cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, xyz.x);
                        cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, xyz.y);
                        cmd.Parameters.AddWithValue("z", NpgsqlDbType.Double, xyz.z);
                        cmd.Parameters.AddWithValue("input_srid", NpgsqlDbType.Smallint, inputSrid);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
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
            s.Stop();
            Console.WriteLine("{0} rows added it took {1} ms", rowCount, s.ElapsedMilliseconds);
            logger.Info("{0} rows added it took {1} ms", rowCount, s.ElapsedMilliseconds);
            return rowCount;
        }

        /// <summary>
        /// Paralelně zapíše data z XYZ souboru do tabulky bodů
        /// </summary>
        /// <param name="filepath">Cesta k souboru</param>
        /// <param name="inputSrid">SRID dat v souboru</param>
        /// <returns>Počet zapsaných řádků</returns>
        public static int LoadXyzFileParallel(string filepath, SRID inputSrid)
        {
            IEnumerable<Xyz> xyzs = ExtractXyzs(filepath);
            int rowCount = 0;
            Stopwatch s = Stopwatch.StartNew();
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
                            "INSERT INTO points(point, latitude, longitude, elevation, resolution, Source, time_added) " +
                            "SELECT Transform.Result, ST_Y(Transform.Result), ST_X(Transform.Result), @z, 0, @Source, now() " +
                            "FROM(SELECT ST_Transform(ST_SetSRID(ST_MakePoint(@x, @y), @input_srid), @wgs84) AS Result) AS Transform";
                        cmd.Parameters.AddWithValue("Source", NpgsqlDbType.Enum, Source.File);
                        cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, xyz.x);
                        cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, xyz.y);
                        cmd.Parameters.AddWithValue("z", NpgsqlDbType.Double, xyz.z);
                        cmd.Parameters.AddWithValue("input_srid", NpgsqlDbType.Smallint, inputSrid);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
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
            s.Stop();
            Console.WriteLine("{0} rows added it took {1} ms", rowCount, s.ElapsedMilliseconds);
            logger.Info("{0} rows added it took {1} ms", rowCount, s.ElapsedMilliseconds);
            return rowCount;
        }

        private static IEnumerable<Xyz> ExtractXyzs(string filepath)
        {
            var sr = new StreamReader(filepath);
            string line;
            var xyzs = new List<Xyz>();
            int lineNumber = 0;
            while ((line = sr.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Contains(" "))
                {
                    line = line.Replace(" ", "\t");
                }

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
        
        public static IEnumerable<Result> QueryForTestingElevationPrecision(int limit = 100, int offset = 0)
        {
            var results = new List<Result>();
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT latitude, longitude, elevation FROM points WHERE source = 'file' OFFSET @offset LIMIT @limit";
                    cmd.Parameters.AddWithValue("offset", NpgsqlDbType.Integer, offset);
                    cmd.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            double latitude = reader.GetDouble(0);
                            double longitude = reader.GetDouble(1);
                            double elevation = reader.GetDouble(2);
                            results.Add(new Result(latitude, longitude, elevation, 0));
                        }
                    }
                }
            }
            return results;
        }

        public static IEnumerable<Result> QueryForTestingElevationPrecisionClosestPoints(Location location, int limit = 100)
        {
            var results = new List<Result>();
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText =
                        "SELECT latitude, longitude, elevation, ST_DistanceSphere(points.point, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84)) " +
                        "AS Distance FROM points WHERE source = @file " +
                        "ORDER BY Distance LIMIT @limit";
                    cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, location.lng);
                    cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, location.lat);
                    cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID.WGS84);
                    cmd.Parameters.AddWithValue("file", NpgsqlDbType.Enum, Source.File);
                    cmd.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            double latitude = reader.GetDouble(0);
                            double longitude = reader.GetDouble(1);
                            double elevation = reader.GetDouble(2);
                            results.Add(new Result(latitude, longitude, elevation, 0));
                        }
                    }
                }
            }

            return results;
        }

    }

    struct ResultDistance
    {
        public Result Result { get; }
        public double Distance { get; }

        public ResultDistance(Result result, double distance) : this()
        {
            Result = result;
            Distance = distance;
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

    /// <summary>
    /// Zdroj výškopisných dat
    /// </summary>
    enum Source
    {
        [PgName("google")] Google,
        [PgName("seznam")] Seznam,
        [PgName("file")] File
    }

    /// <summary>
    /// Spatial Reference System Identifier
    /// </summary>
    enum SRID
    {
        WGS84 = 4326,
        S_JTSK = 5514
    }
}