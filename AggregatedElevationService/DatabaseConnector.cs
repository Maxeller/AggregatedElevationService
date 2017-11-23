using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using System.IO;

namespace AggregatedElevationService
{
    interface IDatabaseConnector
    {
        
    }

    class DatabaseConnector
    {

    }

    class PostgreConnector : IDatabaseConnector
    {
        private string connectionString = "Host=localhost;Username=postgres;Password=root;Database=test";

        public PostgreConnector()
        {
            
        }

        public void InitializeDatabase()
        {
            using (var conn = new NpgsqlConnection(connectionString))
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
                var lineSplit = line.Trim().Split(' ');
                double.TryParse(lineSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x); //TODO: kontrola jestli se to rozparsovalo
                double.TryParse(lineSplit[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                double.TryParse(lineSplit[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double z);
                xyzs.Add(new xyz(x,y,z));
            }
            sr.Close();

            int rowCount = 0;
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                foreach (xyz xyz in xyzs) //TODO: this takes to much fucking time
                {
                    rowCount++;
                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = "INSERT INTO points(point) VALUES (ST_SetSRID(ST_MakePoint(@x, @y, @z), 2065))";
                        cmd.Parameters.AddWithValue("x", xyz.x);
                        cmd.Parameters.AddWithValue("y", xyz.y);
                        cmd.Parameters.AddWithValue("z", xyz.z);
                        //cmd.Prepare();
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            Console.WriteLine("{0} rows added", rowCount);
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
}
