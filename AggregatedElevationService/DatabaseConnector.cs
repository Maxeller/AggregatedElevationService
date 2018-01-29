using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using System.IO;
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
        const string ConnectionString = "Host=localhost;Username=postgres;Password=root;Database=test"; //TODO: změnit databázi 

        public PostgreConnector()
        {
            
        }

        public void InitializeDatabase() //TODO: dodělat
        {
            using (var conn = new NpgsqlConnection(ConnectionString))
            {
                conn.Open();

                // Insert some data
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "INSERT INTO test (login, pass) VALUES (@login, @pass)";
                    cmd.Parameters.AddWithValue("login", "admin");
                    cmd.Parameters.AddWithValue("pass", "adminadmin");
                    cmd.ExecuteNonQuery();
                }

                // Retrieve all rows
                using (var cmd = new NpgsqlCommand("SELECT login FROM test", conn))
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            Console.WriteLine(reader.GetString(0));
            }
        }

        public void LoadxyzFile(string filepath)
        {
            StreamReader sr = new StreamReader(filepath);
            string line;
            List<xyz> xyzs = new List<xyz>();
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

                var lineSplit = line.Trim().Split('\t');
                var xParsed = double.TryParse(lineSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                var yParsed = double.TryParse(lineSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                var zParsed = double.TryParse(lineSplit[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z);
                if (xParsed && yParsed && zParsed) //TODO: log, že se asi něco nerozparsovalo
                {
                    xyzs.Add(new xyz(x, y, z));
                }
            }
            sr.Close();

            int rowCount = 0;
            Stopwatch s = Stopwatch.StartNew(); //TODO: delete?
            using (var conn = new NpgsqlConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand()) //TODO: smazat
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "DELETE FROM points";
                    cmd.ExecuteNonQuery();
                }
                foreach (xyz xyz in xyzs) //TODO: 700k trvá asi 5 (13 ntb) minut takže async + zjistit jestli nelze zrychlit
                {
                    rowCount++;
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        //Transformace z S-JTSK (5514) -> WGS84 (4326) | Vytvoření pointu -> Nastavení S-JTSK -> Trasformace na WGS84
                        cmd.CommandText = "INSERT INTO points(point) VALUES (ST_Transform(ST_SetSRID(ST_MakePoint(@x, @y, @z), 5514), 4326))"; 
                        //TODO: REPLACE? + víc najednou
                        cmd.Parameters.AddWithValue("x", xyz.x);
                        cmd.Parameters.AddWithValue("y", xyz.y);
                        cmd.Parameters.AddWithValue("z", xyz.z);
                        //cmd.Prepare(); //TODO: možnost zrychlení?
                        cmd.ExecuteNonQuery();
                    }
                    //if(rowCount == 100) break; //TODO: smazat
                }
            }
            s.Stop();
            Console.WriteLine("{0} rows added it took {1} ms", rowCount, s.ElapsedMilliseconds); //TODO: vylepšit výstup
        }

        public double GetElevation(double latitude, double longtitude)
        {
            using (var conn = new NpgsqlConnection(ConnectionString))
            {
                conn.Open();

                using (var cmd = new NpgsqlCommand("SELECT * FROM points WHERE point = ", conn))
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            Console.WriteLine(reader.GetString(0));

            }
            return 0;
        }

        public void GetClosestPoint(double latitude, double longtitude)
        {
            
        }
    }

    struct xyz
    {
        public readonly double x;
        public readonly double y;
        public readonly double z;

        public xyz(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }
}
