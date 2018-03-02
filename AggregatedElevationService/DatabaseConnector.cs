using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ServiceModel.Channels;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace AggregatedElevationService
{
    interface IDatabaseConnector
    {
        
    }

    class DatabaseConnector
    {

    }

    class PostgreConnector //TODO: přejmenovat
    {
        private const string CONNECTION_STRING = "Host=localhost;Username=postgres;Password=root;Database=test"; //TODO: změnit databázi a asi dát do configu
        private const short SRID_SJTSK = 5514;
        private const short SRID_WGS84 = 4326;

        public PostgreConnector()
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
                    //TODO: vytvořit databáze
                    //Použití rozšíření PostGIS
                    cmd.CommandText = "CREATE EXTENSION postgis";
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("PostGIS extension created");
                    //Vytvoření enumu pro určení zdroje
                    cmd.CommandText = "CREATE TYPE source AS ENUM('google', 'seznam', 'file');";
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("Source enum created");
                    //Vytvoření tabulky
                    cmd.CommandText = "CREATE TABLE points (id bigserial NOT NULL, point geometry NOT NULL, latitude double precision," +
                                      "longtitude double precision, elevation double precision, resolution double precision, source source NOT NULL," +
                                      "time_added timestamp with time zone NOT NULL, CONSTRAINT pk_points_id PRIMARY KEY (id))";
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("Points table created");
                }
            }
        }

        public void LoadXyzFile(string filepath)
        {
            IEnumerable<Xyz> xyzs = ExtractXyzs(filepath);

            int rowCount = 0;
            Stopwatch s = Stopwatch.StartNew(); //TODO: delete?
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand()) //TODO: smazat
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "DELETE FROM points";
                    cmd.ExecuteNonQuery();
                }
                foreach (Xyz xyz in xyzs) //TODO: 700k trvá asi 5 (13 ntb) minut takže async + zjistit jestli nelze zrychlit
                {
                    rowCount++;
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        //Transformace z S-JTSK (5514) -> WGS84 (4326) | Vytvoření pointu -> Nastavení S-JTSK -> Trasformace na WGS84
                        cmd.CommandText =
                            "INSERT INTO points(point, latitude, longtitude, elevation, resolution, source, time_added) " +
                            "SELECT Transform.Result, ST_Y(Transform.Result), ST_X(Transform.Result), ST_Z(Transform.Result), 0, @source, now() " +
                            "FROM(SELECT ST_Transform(ST_SetSRID(ST_MakePoint(@x, @y, @z), @jstk), @wgs84) AS Result) AS Transform";
                        cmd.Parameters.AddWithValue("source", NpgsqlDbType.Enum, Source.File);
                        cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, xyz.x);
                        cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, xyz.y);
                        cmd.Parameters.AddWithValue("z", NpgsqlDbType.Double, xyz.z);
                        cmd.Parameters.AddWithValue("jstk", NpgsqlDbType.Smallint, SRID_SJTSK);
                        cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID_WGS84);
                        cmd.Prepare();
                        cmd.ExecuteNonQuery();
                    }
                    if(rowCount == 100) break; //TODO: smazat
                }
            }
            s.Stop();
            Console.WriteLine("{0} rows added it took {1} ms", rowCount, s.ElapsedMilliseconds); //TODO: vylepšit výstup
        }

        private static IEnumerable<Xyz> ExtractXyzs(string filepath)
        {
            var sr = new StreamReader(filepath);
            string line;
            List<Xyz> xyzs = new List<Xyz>();
            while ((line = sr.ReadLine()) != null)
            {
                if (line.Contains("  "))
                {
                    line = line.Replace("  ", "\t");
                }

                if (line.Contains(","))
                {
                    line = line.Replace(",", ".");
                }

                string[] lineSplit = line.Trim().Split('\t');
                bool xParsed = double.TryParse(lineSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                bool yParsed = double.TryParse(lineSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                bool zParsed = double.TryParse(lineSplit[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z);
                if (xParsed && yParsed && zParsed) //TODO: log, že se asi něco nerozparsovalo
                {
                    xyzs.Add(new Xyz(x, y, z));
                }
            }
            sr.Close();

            return xyzs;
        }

        //http://www.sqlexamples.info/SPAT/postgis_nearest_point.htm
        //https://postgis.net/docs/manual-1.4/ST_Distance_Sphere.html
        public void GetClosestPoint(double latitude, double longtitude)
        {
            using (var conn = new NpgsqlConnection(CONNECTION_STRING))
            {
                conn.Open();
                conn.MapEnum<Source>();
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT *, ST_Distance_Spheroid(points.point, ST_SetSRID(ST_MakePoint(@x, @y), @wgs84)," +
                                      " \'SPHEROID[\"WGS 84\",6378137,298.257223563]\') as Distance FROM points ORDER BY Distance";
                    cmd.Parameters.AddWithValue("x", NpgsqlDbType.Double, longtitude);
                    cmd.Parameters.AddWithValue("y", NpgsqlDbType.Double, latitude);
                    cmd.Parameters.AddWithValue("wgs84", NpgsqlDbType.Smallint, SRID_WGS84);
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine(reader.GetString(0));
                        }
                    }
                }
                

            }
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
        [PgName("google")]
        Google,
        [PgName("seznam")]
        Seznam,
        [PgName("file")]
        File
    }
}