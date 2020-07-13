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
        public static SortedDictionary<int, int> dictCol = new SortedDictionary<int, int>();

        public const string TableName = "[Sales].[SalesOrderDetail]";
        public const string ColName = "ProductId";
        public const string statname1 = "stat1";
        public const string statname2 = "stat2";
        public const string statname3 = "stat3";
        public const string colName2 = "UnitPrice";
        public const string dbName = "adw";
        public static float maxalpha = 0;

        public static float contrib = 0.5f;

        public static int numSteps = 200;



        // The heap tuple will have 2 of these and their spreads
        public class HeapElem : IComparable
        {
            public float intercept;
            public float slope;
            public HeapStatElem hsElem;
            public int spread; // so that we know, how many have been merged into the two.
            public List<float> errorList;
            public int start;
            public int end;


            public float GetNorm()
            {
                float fin1 = intercept / Program.maxalpha;
                fin1 = fin1 * Program.contrib + (1 - Program.contrib) * slope;
                return fin1;
            }

            public float mergeintercept;
            public float mergeslope;

            public HeapStatElem mergehsElemM;
            public int mergespread; // so that we know, how many have been merged into the two.
            public List<float> mergeerrorList;
            public int mergestart;
            public int mergeend;

            public float GetMergeNorm()
            {
                float fin1 = mergeintercept / Program.maxalpha;
                fin1 = fin1 * Program.contrib + (1 - Program.contrib) * mergeslope;
                return fin1;
            }


            bool mergePossible;


            public int CompareTo(object obj)
            {
                if (obj == null) return 1;

                HeapElem otherTemperature = obj as HeapElem;
                if (otherTemperature != null)
                    return this.start - otherTemperature.start;
                else
                    throw new ArgumentException("Object is not a HeapElement");
            }


        }

        public struct HeapStatElem
        {

            public int range_high_key;
            public float range_rows;
            public float equal_rows;
            public int distint_range_rows;
            public float average_range_rows;
        }

        public class StatStep
        {
            public int step_number;
            public int range_high_key;
            public float range_rows;
            public float equal_rows;
            public int distint_range_rows;
            public float average_range_rows;
        }


        public class ErrorListElem
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
                string dirName = DateTime.Now.ToString("MM.dd.yyyy_HH:mm_tt");


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
                    GetErrorList(rangeRows, colVal, errorRateList, erroriterator, lisS);

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

                    List<StatStep> histLs = new List<StatStep>();
                    CreateHistogramFromAlgorithm(colVal, histLs, 3);
                    // We are working on the follow
                    //  we have te new histogram. We will now need to do the same estimation things

                    List<float> newErroRate = new List<float>();
                    List<ErrorListElem> eeForNewIterator = new List<ErrorListElem>();

                    GetErrorList(rangeRows, colVal, newErroRate, eeForNewIterator, histLs);

                    //Let us also see the errors that we are getting. 
                    newErroRate.Sort();
                    newErroRate.ForEach(Console.WriteLine);

                    csv = new StringBuilder();

                    foreach (var v in eeForNewIterator)
                    {
                        var newLine = string.Format("{0},{1},{2}{3}", v.num, v.countAct, v.countStat, v.qE);
                        csv.AppendLine(newLine);
                    }
                    string fileNameForFirstStatErrorRateNew = "HistogramSampleMain.csv";
                    string FullnameHist = Path.Combine(FoldToCreateFiles, fileNameForFirstStatErrorRateNew);

                    //after your loop , you write all the errors to the CSV file, in the folder that you wwant to see
                    File.WriteAllText(FullnameHist, csv.ToString());
                }
            }
            catch (SqlException e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("All done. Press any key to finish...");
            Console.ReadKey(true);
        }


        // Have a mechanism to quickly create a error list for agiven range rows
        // for apples to apple comparion the random ranges need to be on the same value boundaries 
        // for the lower and the upper boundaries.
        static void GetErrorList(List<Tuple<int, int>> rangeRows, List<int> colVal, List<float> errorRateList, List<ErrorListElem> erroriterator, List<StatStep> lisS)
        {
            int num = 0;
            foreach (var tup in rangeRows)
            {
                // we need to query how many values are there. 
                var count = colVal.Select(x => x).Count(r => r >= tup.Item1 && r <= tup.Item2);

                // Now we have the actual count, 
                //next we need the count from stat
                var countFromStat = CalculateValuesFromHistogram(tup.Item1, tup.Item2, lisS);

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

        }

        // This is the one that creates various histogram elements
        // from the values we already store and keep
        static void CreateHistogramFromAlgorithm(List<int> colVal, List<StatStep> histLs, int num)
        {

            //we  have the histogram
            int countHistInit = dictCol.Count();
            List<int> keyEl = new List<int>(dictCol.Keys);


            List<HeapStatElem> lhse = new List<HeapStatElem>();
            List<float> errorList = new List<float>();


            // Next will be the priority heap implementation
            // So if there are modulo values, they will be picked i nthe end
            // in a separate process
            for (int i = 0; i < countHistInit / num - 1; i++)
            {
                HeapStatElem hs = new HeapStatElem();

                hs.range_high_key = keyEl[i * num] + num - 1;

                // For each histogram element , add their values in the q-error metric, which is a temporary thing

                for (int j = 0; j < num - 1; j++)
                {
                    hs.range_rows += dictCol[keyEl[i * num + j]];

                }
                hs.equal_rows = dictCol[keyEl[i * num + (num - 1)]];
                hs.distint_range_rows = num - 1;
                hs.average_range_rows = hs.range_rows / hs.distint_range_rows;
                lhse.Add(hs);
            }

            HeapStatElem hsElem = new HeapStatElem();
            hsElem.equal_rows = dictCol[countHistInit - 1];
            hsElem.range_high_key = keyEl[countHistInit - 1];
            int tot = 0;
            int d = 0;
            for (int i = ((countHistInit / num) - 1) * num; i < countHistInit - 1; i++)
            {
                if (i < countHistInit - 1)
                {
                    tot += dictCol[keyEl[i]];
                    d++;
                }

            }
            hsElem.range_rows = tot;
            hsElem.average_range_rows = tot / d;
            hsElem.distint_range_rows = d;

            // Final bucket if there are some values left, we will work on it
            lhse.Add(hsElem);

            // We have the hist Elem. 
            // Now use that to find the  -q list and fill the q-error for each bucket.

            // Now have the q-errors 

            List<float> qeList = new List<float>();
            List<HeapElem> heElemList = new List<HeapElem>();
            maxalpha = float.MinValue;


            foreach (var v in keyEl)
            {
                float estimate = CalculateEMQEsitmateFromHist(ref lhse, v);
                float actual = dictCol[v];

                //  now we have the actual and estimate
                float qe = (estimate + 1) / (actual + 1);
                if (qe < 1)
                {
                    qe = (float)(1) / qe;
                }

                qeList.Add(qe);
            }

            for (int i = 0; i < countHistInit / num - 1; i++)
            {
                HeapElem he = new HeapElem();
                he.start = i * num;
                he.end = (i + 1) * (num) - 1;
                he.hsElem = lhse[i];
                he.spread = (int)lhse[i].range_rows + 1;
                he.errorList = new List<float>();

                float prod1 = 0, sum2 = 0, nls = 0, ls = 0;
                for (int j = 0; j < num; j++)
                {
                    he.errorList.Add(qeList[i * num + j]);

                }

                for (int j = 0; j < num; j++)

                {
                    he.errorList.Sort();
                    float val = (j + 1) * he.errorList[i * num + j];
                    prod1 += val;

                    sum2 += he.errorList[i * num + j];
                }


                float sumn = (num) * (num + 1) / 2;
                float sumns = (num) * (num + 1) * (2 * num + 1) / 6;
                float betanom = num * prod1 - (sumn) * sum2;
                float betaden = num * sumns - (sumn) * (sumn);
                float beta = betanom / betaden;

                float alphanom = prod1 * sumns - sum2 * sumn;
                float alphaden = betaden;
                float alpha = alphanom / alphaden;

                he.intercept = alpha;
                he.slope = beta;

                if (alpha > maxalpha)
                {
                    maxalpha = alpha;
                }
                heElemList.Add(he);
                // At the end of the three, we will need to find the beta and alpha
            }

            HeapElem heElem = new HeapElem();
            int c = 0;
            heElem.start = (countHistInit / num - 1) * num;
            heElem.end = countHistInit - 1;

            heElem.hsElem = lhse[countHistInit / num - 1];

            float prod1o = 0, sum2o = 0, nlso = 0, lso = 0;
            heElem.errorList = new List<float>();

            // Now for the last bucket
            for (int i = (countHistInit / num - 1) * num; i < countHistInit; i++)
            {
                c++;
                heElem.errorList.Add(qeList[i]);


            }

            heElem.errorList.Sort();


            for (int i = (countHistInit / num - 1) * num; i < countHistInit; i++)
            {

                float val = c * heElem.errorList[i];
                prod1o += val;

                sum2o += heElem.errorList[i];
            }

            heElem.spread = c;

            float sumno = c * (c + 1) / 2;
            float sumnso = c * (c + 1) * (2 * c + 1) / 6;
            float betanomo = c * prod1o - sumno * sum2o;
            float betadeno = c * sumnso - sumno * sumno;

            float betao = betanomo / betadeno;

            float alphanomo = sum2o * sumnso - prod1o * sumno;
            float alphao = alphanomo / betadeno;

            if (alphao < maxalpha)
            {
                maxalpha = alphao;

            }

            heElem.intercept = alphao;
            heElem.slope = betao;

            heElemList.Add(heElem);

            // The list if ready, now, we need to build the Heap where the top element with the
            // We will iterate over this list and now create the their mergestories.

            int heapElemCount = heElemList.Count;
            for (int i = 0; i < heapElemCount - 1; i++)
            {
                HeapElem h1 = heElemList[i];
                HeapElem h2 = heElemList[i + 1];


                MergeTwoHeapElem(h1, h2);
                // We have for nodes, their merged values.

            }
            // Heap Elem List has all the heaps with their mergeability information defined. 

            Heap.PriorityQueue<HeapElem, Tuple<float, float>> heapForWork = new Heap.PriorityQueue<HeapElem, Tuple<float, float>>();
            for (int i = 0; i < heElemList.Count - 1; i++)
            {
                heapForWork.Enqueue(heElemList[i], new Tuple<float, float>(heElemList[i].mergeslope, heElemList[i].mergeintercept));
            }

            // At this point the heap is ready. Now pop and keep merging till the end.
            while (heapForWork.Count > numSteps)
            {
                HeapElem he = heapForWork.Dequeue().Key;

                // We got the he elem, now the hard work of the merged one to be inserted.

                HeapElem heNew = new HeapElem();
                HeapStatElem hs = new HeapStatElem();
                heNew.hsElem = he.mergehsElemM;
                heNew.intercept = he.mergeintercept;
                heNew.slope = he.mergeslope;
                heNew.intercept = he.mergeintercept;
                heNew.errorList = he.mergeerrorList;
                heNew.spread = he.spread;
                heNew.end = he.mergeend;
                heNew.start = he.mergestart;


                int k = 0;
                for (int i = 0; i < heElemList.Count; i++)
                {
                    if (heElemList[i].start > he.end)
                    {
                        k = i;
                        heElemList[i] = heNew;
                        break;
                    }
                }

                if (k < heElemList.Count - 1)
                {
                    // Now to merge it with the next element. 
                    MergeTwoHeapElem(heNew, heElemList[k + 2]);
                }
                // We iwll keep removing it after every change.
                heElemList.RemoveAt(k + 1);

                foreach (var v in heapForWork)
                {
                    if (v.Key.end == he.start)
                    {
                        MergeTwoHeapElem(v.Key, heNew);
                        break;
                    }
                }

                // So both the sides are merged and we can have them in the heap inserted back again.
            }

            // At this point, we will have merged a few cases here and here

            // Now build the list

            foreach (var v in heElemList)
            {
                // Should have the lmited he element list
                StatStep ss = new StatStep();
                ss.average_range_rows = v.hsElem.average_range_rows;
                ss.distint_range_rows = v.hsElem.distint_range_rows;
                ss.equal_rows = v.hsElem.equal_rows;
                ss.range_high_key = v.hsElem.range_high_key;
                ss.range_rows = v.hsElem.range_rows;
                histLs.Add(ss);

            }

            return;
        }

        static HeapElem SearchHeapElemNext(HeapElem he, ref List<HeapElem> lshe)
        {

            foreach (var v in lshe)
            {
                if (v.start > he.end)
                {
                    return v;
                }
            }
            return lshe[0];
        }

        static void MergeTwoHeapElem(HeapElem h1, HeapElem h2)
        {
            // We have 2 in the list
            HeapStatElem hse = new HeapStatElem();
            HeapElem he = new HeapElem();

            he.spread = h1.spread + h2.spread;
            he.start = h1.start;
            he.end = h2.end;

            hse.range_high_key = h2.hsElem.range_high_key;
            hse.equal_rows = h2.hsElem.equal_rows;

            hse.distint_range_rows = h1.hsElem.distint_range_rows + 1 + h2.hsElem.distint_range_rows;
            hse.range_rows = h1.hsElem.range_rows + h1.hsElem.equal_rows + h2.hsElem.range_rows;
            hse.average_range_rows = hse.range_rows / hse.distint_range_rows;

            he.hsElem = hse;

            List<float> lisError = new List<float>();

            // Now that we will go from the start to the end
            // indices, so that we can find out the error values
            for (int j = he.start; j < he.end; j++)
            {
                float estimate = hse.average_range_rows;
                float actual = dictCol[j];

                float err = (estimate + 1) / (actual + 1);
                if (err < 1)
                {
                    err = 1 / err;
                }

                lisError.Add(err);
            }

            lisError.Sort();
            he.errorList = lisError;
            // Now we will need to generate the sorted 
            // list and find out the new metrics 

            float prod1 = 0, sum1 = 0;
            int k = 0;
            for (int j = he.start; j < he.end; j++, k++)
            {
                float val = k * lisError[j];
                prod1 += val;
                sum1 += lisError[j];
            }

            k--;
            float sumn = k * (k + 1) / 2;
            float sumns = k * (k + 1) * (2 * k + 1) / 6;

            float betanom = k * prod1 - sumn * sum1;
            float betaden = k * (sumns) - sumn * sumn;

            float betam = betanom / betaden;

            float alphanom = sum1 * sumns - prod1 * sumn;
            float alpha = alphanom / betaden;

            h1.mergeend = he.end;
            h1.mergestart = he.start;
            h1.mergehsElemM = hse;
            h1.mergeerrorList = lisError;
            h1.mergeintercept = betam;
            h1.mergeslope = alpha;
            h1.mergespread = he.spread;
        }



        static float CalculateEMQEsitmateFromHist(ref List<HeapStatElem> his, int val)
        {
            int prevHi = Int32.MinValue;

            for (int i = 0; i < his.Count(); i++)
            {
                if (his[i].range_high_key == val)
                {
                    return his[i].equal_rows;
                }
                if (his[i].range_high_key > val && val > prevHi)
                {
                    return his[i].average_range_rows;
                }

            }
            return 0;
        }


        static int CalculateValuesFromHistogram(int low, int high, List<StatStep> lisS)
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
