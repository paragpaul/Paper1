using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.IO;


namespace ConsoleApp1
{
    class Program
    {
        public static  SortedDictionary<int, int> dictCol = new SortedDictionary<int, int>();

        public const string TableName = "[Sales].[SalesOrderDetail]";
        public const string ColName = "ProductId";
        public const string statname1 = "stat1";
        public const string statname2 = "stat2";
        public const string statname3 = "stat3";
        public const string colName2 = "UnitPrice";
        public const string dbName = "adw";


        struct StatStep
        {
            public int step_number;
            public int range_high_key;
            public float range_rows;
            public float equal_rows;
            public int distint_range_rows;
            public float average_range_rows;
        }


        struct ErrorListElem
        {
            public float countStat;
            public float countAct;
            public int num;
            public float qE;
        }


        static void Main(string[] args)
        {
            try
            {
               string folderName = @"C:\Work\Stats\Exper";
               string dirName = DateTime.Now.ToString("ddd MM.dd.yyyy_HH:mm_tt");


                Random random = new Random();
                Console.WriteLine("Connect to SQL Server and demo Create, Read, Update and Delete operations.");
                string FoldToCreateFiles = Path.Combine(folderName, dirName);
                Directory.CreateDirectory(FoldToCreateFiles);


                string useDb = "use adw";

                // Build connection string
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
                builder.DataSource = "localhost";   // update me
                builder.UserID = "sa";              // update me
                builder.Password = "Yukon900";      // update me
                builder.InitialCatalog = "adw";

                // Connect to SQL
                Console.Write("Connecting to SQL Server ... ");
                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    connection.Open();
                    Console.WriteLine("Done.");

                    string stat1 = ColName + "_" + statname1;


                    // Create a sample database
                    Console.Write("Creating a statistics on the table and column of my choice");
                    String sql = String.Format("select* from sys.stats where object_id = object_id('{0}')", TableName);
                    bool statsExists = false;

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                String statname = reader.GetString(1);
                                if (statname == stat1)
                                {
                                    statsExists = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (statsExists)
                    {
                        sql = String.Format("drop statistics {0}", TableName + "." + stat1);
                        using (SqlCommand command = new SqlCommand(sql, connection))
                        {
                            command.ExecuteNonQuery();
                            Console.WriteLine("Done deleting stats.");
                        }
                    }

                    // Now create the stats
                    sql = String.Format("create statistics {0} on {1}({2}) with sample 20 percent", stat1, TableName, ColName);
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.ExecuteNonQuery();
                        Console.WriteLine("Done creating stats.");
                    }

                    // Create a Table and insert some sample data
                    Console.Write("Reading all the values in the table");

                    List<Int32> colVal = new List<int>();
                    sql = String.Format("select {0} from {1}", ColName, TableName);

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int val = reader.GetInt32(0);
                                colVal.Add(val);

                            }
                        }
                    }

                    Console.WriteLine("Now find the stats id");
                    sql = string.Format("select stats_id from sys.stats where object_id = object_id('{0}') and name = '{1}'", TableName, stat1);

                    int stat1_id = 0;

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        stat1_id = (int)command.ExecuteScalar();
                    }


                    // Now read the stats rows
                    sql = String.Format("select step_number, range_high_key,range_rows, equal_rows, distinct_range_rows, average_range_rows  from sys.dm_db_stats_histogram(object_id('{0}'),{1}) ", TableName, stat1_id);
                    List<StatStep> lisS = new List<StatStep>();
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                StatStep ss = new StatStep();

                                ss.step_number = reader.GetInt32(0);
                                ss.range_high_key = reader.GetInt32(1);
                                ss.range_rows = reader.GetFloat(2);
                                ss.equal_rows = reader.GetFloat(3);
                                ss.distint_range_rows = int.Parse(reader["distinct_range_rows"].ToString());
                                ss.average_range_rows = reader.GetFloat(5);

                                lisS.Add(ss);
                            }
                        }
                    }

                    // We have a list of stat steps, 
                    // Now use that for queries that we need
                    colVal.Sort();
                    int min = colVal.First();
                    int last = colVal.Last();

                    List<Tuple<int, int>> rangeRows = new List<Tuple<int, int>>();
                    for (int i = 0; i < 300; i++)
                    {
                        int val = random.Next(min, last - (int)(.3 * (last - min)));
                        Tuple<int, int> ti = new Tuple<int, int>(val, random.Next(val + 1, last));
                        rangeRows.Add(ti);

                    }

                    List<float> errorRateList = new List<float>();
                    int num = 0;
                    List<ErrorListElem> erroriterator = new List<ErrorListElem>();
                    // We have range queries
                    foreach (var tup in rangeRows)
                    {
                        // we need to query how many values are there. 
                        var count = colVal.Select(x => x).Count(r => r >= tup.Item1 && r <= tup.Item2);

                        // Now we have the actual count, 
                        //next we need the count from stat
                        var countFromStat = CalculateValuesFromHistogram(tup.Item1, tup.Item2, ref lisS);

                        float errorRate = ((float)(countFromStat + 1)) / (float)(count + 1);

                        if (errorRate < 1 && errorRate > 0)
                        {
                            errorRate = 1 / errorRate;
                        }
                        errorRateList.Add(errorRate);
                        ErrorListElem ee = new ErrorListElem();
                        ee.countAct = count;
                        ee.countStat = countFromStat;
                        ee.qE = errorRate;
                        ee.num = num++;
                        erroriterator.Add(ee);
                    }


                    // We have the erroRate list and now we will sort it
                    errorRateList.Sort();
                    errorRateList.ForEach(Console.WriteLine);


                    var csv = new StringBuilder();

                    foreach (var v in erroriterator)
                    {
                        var newLine = string.Format("{0},{1},{2}{3}", v.num, v.countAct, v.countStat, v.qE);
                        csv.AppendLine(newLine);
                    }
                    string fileNameForFirstStatErrorRate = "20PercDefaultSampleMain.csv";
                    string Fullname = Path.Combine(FoldToCreateFiles, fileNameForFirstStatErrorRate);

                    //after your loop
                    File.WriteAllText(Fullname, csv.ToString());

                    // Now we have some files ready to be read. For the Excel. 
                    // Then we will process the data to be able to use qerror to build the hist.
                    foreach (int v in colVal)
                    {
                        if (dictCol.ContainsKey(v))
                        {
                            dictCol[v]++;
                        }
                        else
                        {
                            dictCol.Add(v, 1);
                        }
                    }

                }
            }
            catch (SqlException e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("All done. Press any key to finish...");
            Console.ReadKey(true);
        }

        static ref List<StatStep>  CreateHistogramFromAlgorithm(ref List<int> colVal)
        {

        }


        static int CalculateValuesFromHistogram(int low, int high, ref List<StatStep> lisS)
        {
            int totCount = 0;
            int prevHi = Int32.MinValue;

            if (high == lisS[0].range_high_key)
            {
                totCount = (int)lisS[0].equal_rows;
                return totCount;
            }

            if (high < lisS[0].range_high_key)
            {
                return 0;
            }

            for (int i = 0; i < lisS.Count(); i++)
            {
                // First is for the rnage that is below the contained value. 
                if (i == 0 && low < lisS[0].range_high_key)
                {
                    totCount += (int)lisS[0].equal_rows;

                }
                else if (low < prevHi && high < lisS[i].range_high_key)
                {
                    float frac = (high - prevHi) / (lisS[i].range_high_key - prevHi);
                    float numDis = frac * lisS[i].distint_range_rows;
                    float tot = numDis * lisS[i].average_range_rows;
                    return totCount + (int)tot;

                }
                else if (low < prevHi && high > lisS[i].range_high_key)
                {
                    totCount += (int)lisS[i].range_rows + (int)lisS[i].equal_rows;
                }
                else if (low > prevHi && high < lisS[i].range_high_key)
                {
                    float frac = (high - low) / (lisS[i].range_high_key - prevHi);
                    float numDis = frac * lisS[i].distint_range_rows;
                    float tot = numDis * lisS[i].average_range_rows;
                    return totCount + (int)tot;
                }
                prevHi = lisS[i].range_high_key;

            }

            return totCount;
        }
    }

}
