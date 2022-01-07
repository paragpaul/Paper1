using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.IO;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Random;
using MathNet.Numerics.Statistics;

using System.Diagnostics;


namespace ConsoleApp1
{
    class Program
    {
        public static SortedDictionary<double, int> dictCol = new SortedDictionary<double, int>();

        public const string TableName = "[Sales].[SalesOrderDetail]";
        public const string ColName = "LineTotal";
        public const string statname1 = "stat1";
        public const string statname2 = "stat2";
        public const string statname3 = "stat3";
        public const string colName2 = "UnitPrice";
        public const string dbName = "adw";
        public static double maxalpha = 0;
        public static double maxbeta = 0;
        public static double minalpha = 0;
        public static double contrib = 0.01f;
        public static int STEPS = 20;
        public static int numSteps = 200;
        public static int SAMPLECOUNT = 10000;


        // The heap tuple will have 2 of these and their spreads
        public class HeapElem : IComparable
        {
            public double intercept;
            public double slope;
            public HeapStatElem hsElem;
            public int spread; // so that we know, how many have been merged into the two.
            public List<double> errorList;
            public int start;
            public int end;


            public double GetNorm()
            {
                // if all the intercepts are of same value, then the programs maxalpha and min alpha will be the same
                // and that will mean there is a possibility of division by 0
                // stopping that possiblity by normalizing it.
                double fin1 = (intercept - Program.minalpha) / (Program.maxalpha != Program.minalpha ? (Program.maxalpha - Program.minalpha) : 1);

                // Older interpreation of the interecpt ws wrong.
                //double fin1 = intercept / Program.maxalpha;
                fin1 = fin1 * Program.contrib + ((1 - Program.contrib) * slope) / Program.maxbeta;
                return fin1;
            }

            public double mergeintercept;
            public double mergeslope;

            public HeapStatElem mergehsElemM;
            public int mergespread; // so that we know, how many have been merged into the two.
            public List<double> mergeerrorList;
            public int mergestart;
            public int mergeend;

            public double GetMergeNorm()
            {
                double fin1 = (mergeintercept - Program.minalpha) / (Program.maxalpha != Program.minalpha ? (Program.maxalpha - Program.minalpha) : 1);

                fin1 = fin1 * Program.contrib + ((1 - Program.contrib) * mergeslope) / (Program.maxbeta == 0 ? 1 : Program.maxbeta);
                return fin1;
            }


            public double mergenorm;


            public int CompareTo(object obj)
            {
                if (obj == null) return 1;

                HeapElem otherTemperature = obj as HeapElem;
                if (otherTemperature != null)
                    return (int)this.GetMergeNorm() - (int)otherTemperature.GetMergeNorm();
                else
                    throw new ArgumentException("Object is not a HeapElement");
            }


        }

        public struct HeapStatElem
        {

            public double range_high_key;
            public double range_rows;
            public double equal_rows;
            public int distint_range_rows;
            public double average_range_rows;
        }

        public class StatStep
        {
            public int step_number;
            public int range_high_key;
            public int range_rows;
            public int equal_rows;
            public int distint_range_rows;
            public double average_range_rows;

            public StatStep(int rangehk)
            {
                range_high_key = rangehk;
            }
            public StatStep() { }
        }


        public class ErrorListElem : IComparable
        {
            public double countStat;
            public double countAct;
            public int num;
            public double qE;

            int IComparable.CompareTo(object obj)
            {
                ErrorListElem c = (ErrorListElem)obj;
                if (this.qE < c.qE)
                    return 1;
                else if (this.qE > c.qE)
                    return -1;

                return 0;
            }

        }

        public readonly Random _random = new Random();
        static void Main(string[] args)
        {
            try
            {
                string folderName = @"D:\Work\Stats\Exper";
                string dirName = DateTime.Now.ToString("MM_dd_yyyy_HH_mm_tt");
                string FoldToCreateFiles = Path.Combine(folderName, dirName);

                var gamma = new Gamma(2.0, 1.5, new MersenneTwister());
                Random random = new Random();
                LogNormal estimation = LogNormal.Estimate(Enumerable.Repeat(0, 134550).Select(i => random.NextDouble() * 55.0).ToArray());

                List<int> interim = estimation.Samples().Take(SAMPLECOUNT).Select(n => (int)n * new Random().Next(500)).ToList();
                List<double> colVal = interim.ConvertAll(x => (double)x);

                // We have a list of stat steps, 
                // Now use that for queries that we need
                colVal.Sort();
                double min = colVal.First();
                double last = colVal.Last();

                // Now create some rangeRow queries

                List<Tuple<int, int>> rangeRows = new List<Tuple<int, int>>();
                for (int i = 0; i < 300; i++)
                {
                    int val = random.Next((int)min, (int)last - (int)(.3 * (last - min)));
                    Tuple<int, int> ti = new Tuple<int, int>(val, random.Next(val + 1, (int)last));
                    rangeRows.Add(ti);
                }

                List<double> errorRateList = new List<double>();

                int num = 0;
                List<ErrorListElem> erroriterator = new List<ErrorListElem>();

                // Create a list of steps.
                List<StatStep> lisEquiDepth = new List<StatStep>();
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

                List<double> keyEl = new List<double>(dictCol.Keys);

                int sumTotal = 0;
                foreach (double val in keyEl)
                {
                    sumTotal += dictCol[val];
                }

                Histogram h = CreateHistogramFromEquiWidth(colVal, lisEquiDepth, sumTotal);



                //using (SqlCommand command = new SqlCommand(sql, connection))
                //{

                //    using (SqlDataReader reader = command.ExecuteReader())
                //    {
                //        while (reader.Read())
                //        {
                //            StatStep ss = new StatStep();

                //            ss.step_number = reader.GetInt32(0);
                //            ss.range_high_key = (double)reader.GetDecimal(1);
                //            ss.range_rows = reader.Getdouble(2);
                //            ss.equal_rows = reader.Getdouble(3);
                //            ss.distint_range_rows = int.Parse(reader["distinct_range_rows"].ToString());
                //            ss.average_range_rows = reader.Getdouble(5);

                //            lisEquiDepth.Add(ss);
                //        }
                //    }
                //}

                // We have range queries
                GetErrorList(rangeRows, colVal, errorRateList, erroriterator, lisEquiDepth);

                // We have the erroRate list and now we will sort it
                errorRateList.Sort();
                errorRateList.ForEach(Console.WriteLine);


                var csv = new StringBuilder();

                foreach (var v in erroriterator)
                {
                    var newLine = string.Format("{0},{1},{2},{3}", v.num, v.countAct, v.countStat, v.qE);
                    csv.AppendLine(newLine);
                }

                string fileNameForFirstStatErrorRate = "EquiWidthHistogramMain.csv";
                if (!Directory.Exists(FoldToCreateFiles))
                {
                    Directory.CreateDirectory(FoldToCreateFiles);
                }

                string Fullname = Path.Combine(FoldToCreateFiles, fileNameForFirstStatErrorRate);

                //after your loop
                File.WriteAllText(Fullname, csv.ToString());

                List<StatStep> histLs = new List<StatStep>();
                CreateHistogramFromAlgorithm(colVal, histLs, 2);
                // We are working on the follow
                //  we have te new histogram. We will now need to do the same estimation things

                List<double> newErroRate = new List<double>();
                List<ErrorListElem> eeForNewIterator = new List<ErrorListElem>();

                GetErrorList(rangeRows, colVal, newErroRate, eeForNewIterator, histLs);

                //Let us also see the errors that we are getting. 
                newErroRate.Sort();
                Console.WriteLine("=================================================");
                newErroRate.ForEach(Console.WriteLine);

                csv = new StringBuilder();

                foreach (var v in eeForNewIterator)
                {
                    var newLine = string.Format("{0},{1},{2},{3}", v.num, v.countAct, v.countStat, v.qE);
                    csv.AppendLine(newLine);
                }
                string fileNameForFirstStatErrorRateNew = "HistogramSampleMain.csv";
                string FullnameHist = Path.Combine(FoldToCreateFiles, fileNameForFirstStatErrorRateNew);

                //after your loop , you write all the errors to the CSV file, in the folder that you wwant to see
                File.WriteAllText(FullnameHist, csv.ToString());

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
        static void GetErrorList(List<Tuple<int, int>> rangeRows, List<double> colVal, List<double> errorRateList, List<ErrorListElem> erroriterator, List<StatStep> lisS)
        {
            int num = 0;
            foreach (var tup in rangeRows)
            {
                // we need to query how many values are there. 
                var count = colVal.Select(x => x).Count(r => r >= tup.Item1 && r <= tup.Item2);

                // Now we have the actual count, 
                //next we need the count from stat
                var countFromStat = CalculateValuesFromHistogram(tup.Item1, tup.Item2, lisS);

                double errorRate = ((double)(countFromStat + 1)) / (double)(count + 1);

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

            erroriterator.Sort();
        }

        // Have some random numbers, based on a distribution
        // Now create the 
        static Histogram CreateHistogramFromEquiWidth(List<double> colVal, List<StatStep> listS, int sumTotal)
        {
            //we  have the histogram
            int countHistInit = dictCol.Count();
            if (countHistInit < STEPS)
            {
                Debug.Assert(false, "Had less than 200 values in the steps");
                // we have very low number of distinct values and this will not be good enough for a stat measure
                return null;
            }
            List<double> keyEl = new List<double>(dictCol.Keys);
            keyEl.Sort();
            Histogram h = new Histogram(colVal, STEPS);

            StatStep current = new StatStep();
            current.range_high_key = -1;

            for (int i = 0; i < h.BucketCount; i++)
            {
                Bucket b = h[i];

                // Now take the bucket and then convert into a list of steps
                // THis will be returned for caculation
                StatStep ss = new StatStep();
                int upperbound = Convert.ToInt32(b.UpperBound);
                if (!keyEl.Contains(upperbound))
                {
                    int prev = 0;
                    foreach (var k in keyEl)
                    {
                        if (k >= upperbound)
                        {
                            upperbound = Convert.ToInt32(k);
                            break;
                        }
                    }

                    ss.range_high_key = upperbound;
                    ss.range_rows = Convert.ToInt32(b.Count) - dictCol[upperbound];
                    ss.equal_rows = dictCol[upperbound];

                    // We havd a genuine upper bound now
                    foreach (var l in keyEl)
                    {
                        if (l > current.range_high_key && l < upperbound)
                        {
                            ss.distint_range_rows++;
                        }

                        if (l >= upperbound)
                            break;
                    }

                    if (ss.distint_range_rows > 0)
                        ss.average_range_rows = ss.range_rows / ss.distint_range_rows;
                    // else it will be 0, which is fine.


                    // Now add the new stat step that was created 
                    listS.Add(ss);
                }
                else
                {
                    ss.range_high_key = upperbound;
                    ss.range_rows = Convert.ToInt32(b.Count) - dictCol[upperbound];
                    ss.equal_rows = dictCol[upperbound];
                    // We havd a genuine upper bound now
                    foreach (var l in keyEl)
                    {
                        if (l > current.range_high_key && l < upperbound)
                        {
                            ss.distint_range_rows++;
                        }
                    }

                    if (ss.distint_range_rows > 0)
                        ss.average_range_rows = ss.range_rows / ss.distint_range_rows;
                }

                current = ss;

            }

            return h;

        }



        // This is the one that creates various histogram elements
        // from the values we already store and keep
        static void CreateHistogramFromAlgorithm(List<double> colVal, List<StatStep> histLs, int num)
        {

            //we  have the histogram
            int countHistInit = dictCol.Count();
            List<double> keyEl = new List<double>(dictCol.Keys);


            List<HeapStatElem> lhse = new List<HeapStatElem>();
            List<double> errorList = new List<double>();


            // Next will be the priority heap implementation
            // So if there are modulo values, they will be picked in the end
            // in a separate process.
            for (int i = 0; i < countHistInit / num - 1; i++)
            {
                HeapStatElem hs = new HeapStatElem();

                hs.range_high_key = keyEl[i * num + num - 1];

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
            hsElem.equal_rows = dictCol[keyEl[countHistInit - 1]];
            hsElem.range_high_key = keyEl[countHistInit - 1];
            int lastStepTot = 0;
            int d = 0;
            for (int i = ((countHistInit / num) - 1) * num; i < countHistInit - 1; i++)
            {
                if (i < countHistInit - 1)
                {
                    lastStepTot += dictCol[keyEl[i]];
                    d++;
                }

            }

            hsElem.range_rows = lastStepTot;
            hsElem.average_range_rows = lastStepTot / d;
            hsElem.distint_range_rows = d;

            // Final bucket if there are some values left, we will work on it
            lhse.Add(hsElem);

            // We have the hist Elem. 
            // Now use that to find the  -q list and fill the q-error for each bucket.

            // Now have the q-errors 

            List<double> qeList = new List<double>();
            List<HeapElem> heElemList = new List<HeapElem>();
            maxalpha = double.MinValue;
            maxbeta = double.MinValue;
            minalpha = double.MaxValue;

            foreach (var v in keyEl)
            {
                double estimate = CalculateEMQEsitmateFromHist(ref lhse, v);
                double actual = dictCol[v];

                //  now we have the actual and estimate
                double qe = (estimate + 1) / (actual + 1);
                if (qe < 1)
                {
                    qe = (double)(1) / qe;
                }

                qeList.Add(qe);
            }

            for (int i = 0; i < lhse.Count() - 1; i++)
            {
                HeapElem he = new HeapElem();
                he.start = i * num;
                he.end = (i + 1) * (num) - 1;
                he.hsElem = lhse[i];

                he.errorList = new List<double>();

                double prod1 = 0, sum2 = 0, nls = 0, ls = 0;
                he.spread = (int)lhse[i].range_rows + (int)lhse[i].equal_rows;


                for (int j = 0; j < num; j++)
                {

                    he.errorList.Add(qeList[i * num + j]);

                }

                he.errorList.Sort();

                // From the error list, form the slope and intercept
                // using the OLS formula for slope and intercept
                for (int j = 0; j < num; j++)
                {
                    double val = (j + 1) * he.errorList[j];
                    prod1 += val;
                    sum2 += he.errorList[j];
                }


                double sumn = (num) * (num + 1) / 2;
                double sumns = (num) * (num + 1) * (2 * num + 1) / 6;
                double betanom = num * prod1 - (sumn) * sum2;
                double betaden = num * sumns - (sumn) * (sumn);
                double beta = betanom / betaden;
                /*
                double alphanom = prod1 * sumns - sum2 * sumn;
                double alphaden = betaden;
                
                double alpha = alphanom / alphaden;
                */

                double alpha = sum2 / num - (beta * (sumn / num));

                he.intercept = alpha;
                he.mergeintercept = alpha;
                he.slope = beta;

                if (alpha > maxalpha)
                {
                    maxalpha = alpha;
                }

                // This is needed for finding the normalized value for the alpha
                // so that we can use to find the contri and norm in a proepr fashion
                if (alpha < minalpha)
                {
                    minalpha = alpha;
                }

                if (beta > maxbeta)
                {
                    maxbeta = beta;
                }

                // We will get the merged norm, that we will need 
                he.mergenorm = he.GetMergeNorm();
                heElemList.Add(he);
                // At the end of the three, we will need to find the beta and alpha
            }

            HeapElem heElem = new HeapElem();
            int c = 0;
            int index = lhse.Count() - 1;

            heElem.start = index * num;
            heElem.end = (index + 1) * num - 1;

            heElem.hsElem = lhse[index];

            double prod1o = 0, sum2o = 0, nlso = 0, lso = 0;
            heElem.errorList = new List<double>();

            heElem.spread += (int)heElem.hsElem.range_rows + (int)heElem.hsElem.equal_rows;

            // Now for the last bucket
            for (int i = (countHistInit / num) * num; i < countHistInit; i++)
            {
                c++;
                heElem.errorList.Add(qeList[i]);

            }
            heElem.errorList.Sort();

            for (int i = (countHistInit / num) * num, k = 0; i < countHistInit; i++, k++)
            {

                double val = (k + 1) * heElem.errorList[k];
                prod1o += val;

                sum2o += heElem.errorList[k];
            }

            heElem.spread = c;

            double sumno = c * (c + 1) / 2;
            double sumnso = c * (c + 1) * (2 * c + 1) / 6;
            double betanomo = c * prod1o - sumno * sum2o;
            double betadeno = c * sumnso - sumno * sumno;

            double betao = betanomo / betadeno;

            double alphanomo = sum2o * sumnso - prod1o * sumno;
            double alphao = alphanomo / betadeno;

            alphao = sum2o / num - (betao * (sumno / num));

            if (alphao > maxalpha)
            {
                maxalpha = alphao;

            }

            if (alphao < minalpha)
            {
                minalpha = alphao;
            }

            if (betao > maxbeta)
            {
                maxbeta = betao;
            }

            heElem.intercept = alphao;
            heElem.mergeintercept = alphao;
            heElem.slope = betao;
            heElem.mergenorm = heElem.GetMergeNorm();
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

                // So starting with index = 0 all the way till the n-2
                // we will merge with the next one and find the merged norm
                // This will give us a normalized value for the slope
                // and that will help with finding the one with the lowest
                // slope , that we are targetting.

            }

            // Heap Elem List has all the heaps with their mergeability information defined. 
            MyComparer mc = new MyComparer(); ;
            Heap.PriorityQueue<HeapElem, Tuple<double, double>> heapForWork = new Heap.PriorityQueue<HeapElem, Tuple<double, double>>(mc);

            // THe custom comparer will always look at the merged norms
            for (int i = 0; i < heElemList.Count - 1; i++)
            {
                heapForWork.Enqueue(heElemList[i], new Tuple<double, double>(heElemList[i].mergeslope, heElemList[i].mergeintercept));
            }

            // A quick sanity check
            // check that every start for a heap element is right before 
            // the start for the next
            int prevs = 0, preve = 0;
            int elecnt = 0;
            foreach (var he in heElemList)
            {
                if (elecnt == 0)
                {
                    prevs = he.start;
                    preve = he.end;
                    elecnt++;
                    continue;
                }

                if (he.start != preve + 1)
                {
                    // we have an issue here
                    int a = 10;
                }

                prevs = he.start;
                preve = he.end;
                elecnt++;
            }

            // At this point the heap is ready. Now pop and keep merging till the end.
            while (heElemList.Count > STEPS)
            {
                HeapElem he = heapForWork.Dequeue().Key;

                // We got the he elem, now the hard work of the merged one to be inserted.

                HeapElem heNew = new HeapElem();

                heNew.hsElem = he.mergehsElemM;

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
                        heElemList[i - 1] = heNew;
                        break;
                    }
                }

                if (k < heElemList.Count - 1)
                {
                    // Now to merge it with the next element. 
                    // This will be 1 past the next one. 
                    MergeTwoHeapElem(heNew, heElemList[k + 1]);

                }

                if (k < heElemList.Count)
                {
                    // We will keep removing it after every change.
                    heElemList.RemoveAt(k);
                }

                if (k>1 && heElemList[k-1)
                {

                }

                // Now do a quick check
                if (k > 1 && k < heElemList.Count &&
                    heElemList[k - 2].end + 1 != heElemList[k - 1].start)
                {
                    // we have an issue
                    int a = 10;
                }

                if (k < heElemList.Count - 1 && heElemList[k].end + 1 != heElemList[k + 1].start)
                {
                    // we have another problem
                    int a = 10;
                }

                if (k > 1)
                {
                    MergeTwoHeapElem(heElemList[k - 2], heElemList[k - 1]);
                    // So both the sides are merged and we can have them in the heap inserted back again.
                }
            }

            // At this point, we will have merged a few cases here and here

            // Now build the list

            foreach (var v in heElemList)
            {
                // Should have the lmited he element list
                StatStep ss = new StatStep();
                ss.average_range_rows = v.hsElem.average_range_rows;
                ss.distint_range_rows = v.hsElem.distint_range_rows;
                ss.equal_rows = Convert.ToInt32(v.hsElem.equal_rows);
                ss.range_high_key = Convert.ToInt32(v.hsElem.range_high_key);
                ss.range_rows = Convert.ToInt32(v.hsElem.range_rows);
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
            List<double> keyEl = new List<double>(dictCol.Keys);

            // We have 2 in the list
            HeapStatElem hse = new HeapStatElem();
            HeapElem he = new HeapElem();

            he.spread = h1.spread + h2.spread;
            he.start = h1.start;
            he.end = h2.end;

            hse.range_high_key = h2.hsElem.range_high_key;
            hse.equal_rows = h2.hsElem.equal_rows;

            // Add 1 for the h1.hsElem top equal rows
            // h1.rangerows would have already taken into account all the equal rows till now.
            hse.distint_range_rows = h1.hsElem.distint_range_rows + 1 + h2.hsElem.distint_range_rows;
            hse.range_rows = h1.hsElem.range_rows + h1.hsElem.equal_rows + h2.hsElem.range_rows;
            hse.average_range_rows = hse.range_rows / hse.distint_range_rows;

            he.hsElem = hse;

            List<double> lisError = new List<double>();

            // Now that we will go from the start to the end
            // indices, so that we can find out the error values
            for (int j = he.start; j <= he.end; j++)
            {
                double estimate = (j < he.end) ? hse.average_range_rows : hse.equal_rows;

                double actual = dictCol[keyEl[j]];

                double err = (estimate + 1) / (actual + 1);
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
            double prod1 = 0, sum1 = 0;
            int k = 0;
            for (int j = he.start; j <= he.end; j++, k++)
            {
                double val = (k + 1) * lisError[k];
                prod1 += val;
                sum1 += lisError[k];
            }


            double sumn = k * (k + 1) / 2;
            double sumns = k * (k + 1) * (2 * k + 1) / 6;

            double betanom = k * prod1 - sumn * sum1;
            double betaden = k * (sumns) - sumn * sumn;

            double betam = betanom / betaden;

            double alphanom = sum1 * sumns - prod1 * sumn;
            double alpha = alphanom / betaden;
            alpha = sum1 / k - (betam * (sumn / k));

            // If we dont update the merged max alpha
            // and the merged maxbeta , then we will not be able
            // to normalize and get the latest values for merge norm

            if (alpha > maxalpha)
            {
                maxalpha = alpha;

            }

            // Even when we are merging, we need
            // to make sure that the max and min are taken care of
            // since the heap can have a mix of min and maxes from 
            // merged as well as unmerged cases
            if (alpha < minalpha)
            {
                minalpha = alpha;
            }

            if (betam > maxbeta)
            {
                maxbeta = betam;
            }

            h1.mergeend = he.end;
            h1.mergestart = he.start;
            h1.mergehsElemM = hse;
            h1.mergeerrorList = lisError;
            h1.mergeintercept = alpha;
            h1.mergeslope = betam;
            h1.mergespread = he.spread;
        }



        static double CalculateEMQEsitmateFromHist(ref List<HeapStatElem> his, double val)
        {
            double prevHi = Int32.MinValue;

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
            double prevHi = Int32.MinValue;

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
                    double frac = (high - prevHi) / (lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;
                    double tot = numDis * lisS[i].average_range_rows;
                    return totCount + (int)tot;

                }
                else if (low < prevHi && high > lisS[i].range_high_key)
                {
                    totCount += (int)lisS[i].range_rows + (int)lisS[i].equal_rows;
                }
                else if (low > prevHi && high < lisS[i].range_high_key)
                {
                    double frac = (high - low) / (lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;
                    double tot = numDis * lisS[i].average_range_rows;
                    return totCount + (int)tot;
                }
                prevHi = lisS[i].range_high_key;

            }

            return totCount;
        }
    }

}
