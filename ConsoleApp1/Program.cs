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

        public static int SAMPLECOUNT = 30000;
        public static int RANGENUM = 200;
        public static double[] SumAll = new double[SAMPLECOUNT];
        public static int NumOFRuns = 10;

        public static Dictionary<Tuple<int, int>, double> HashMapDictionary = new Dictionary<Tuple<int, int>, double>();

        //public static double[,] ErrorValues = new double[SAMPLECOUNT, SAMPLECOUNT];
        public static bool fQuantile = true, fVOptimal = true, fEquiD = true, fEquiW = true, fAlgo = true;
        public static TimeSpan tsQuantile, tsVOptimal, tsEquiD, tsEquiW, tsAlgo;

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

            public double GetMergeNorm2()
            {
                double fin1 = (mergeintercept - Program.minalpha) / (Program.maxalpha != Program.minalpha ? (Program.maxalpha - Program.minalpha) : 1);

                fin1 = fin1 * Program.contrib + ((1 - Program.contrib) * mergeslope) / (Program.maxbeta == 0 ? 1 : Program.maxbeta);
                return fin1;
            }

            public double GetMergeInterceptNorm()
            {
                double fin1 = (mergeintercept - Program.minalpha) / (Program.maxalpha != Program.minalpha ? (Program.maxalpha - Program.minalpha) : 1);

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

        /// <summary>
        ///  WE need 2 different distribution families to 
        ///  get data from each
        /// </summary>
        public enum MULTNOMIALDIST
        {
            MULTINOMIAL, 
            DIRCHLET
        }

        /// <summary>
        /// Basic distributions will be iterated
        /// and they are going to be computed later.
        /// </summary>
        public enum BASICDISTYPE
        {
            NORMAL,
            UNIFORM,
            CHI,
            PARETO,
            RANDOM,
            LAPLACE,
            CAUCHY,
            ZIPFIAN
        }

        public enum TYPETEST
        {
            EMQ, RGE, DCT, AEMQ
        }

        public readonly Random _random = new Random();
        static void Main(string[] args)
        {
            int readFromFile = 0;
            try
            {
                string folderName = @"D:\Work\Stats\Exper";
                string dirName = DateTime.Now.ToString("MM_dd_yyyy_HH_mm_tt");
                string FoldToCreateFiles = Path.Combine(folderName, dirName);

                var gamma = new Gamma(2.0, 1.5, new MersenneTwister());
                Random random = new Random(111);
                LogNormal estimation = LogNormal.Estimate(Enumerable.Repeat(0, 134550).Select(i => random.NextDouble() * 55.0).ToArray());
                List<int> interim = new List<int>();



                // Need to fill a array with quantile data for each 
                // aglorithm type
                foreach (BASICDISTYPE enumDistType in Enum.GetValues(typeof(BASICDISTYPE)))
                {
                    for (int i = 0; i < NumOFRuns; i++)
                    {
                        RunTestsAccordingly(enumDistType);
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

        static void RunTestsAccordingly()
        {
            // Original distribution
            //interim = estimation.Samples().Take(SAMPLECOUNT).Select(n => (int)n * new Random().Next(500)).ToList();

            // Normal distirbution
            //============================
            //var original = new Normal(100.6, 20, new Mrg32k3a(100000));
            //var estimated = Normal.Estimate(original.Samples().Take(10000));
            //var samples = new double[SAMPLECOUNT];
            //Normal.Samples(SystemRandomSource.Default, samples, 1000.6, 200);

            // Uniform
            // ========================
            //Random randomDist = new Random(121231);
            //for (int i = 0; i < SAMPLECOUNT; i++)
            //{
            //    interim.Add(randomDist.Next(1, 1000));
            //}
            //string distfile = Path.Combine("c:\\temp\\", "uniform.txt");
            //List<double> colVal = interim.ConvertAll(x => (double)x);
            //File.WriteAllText(distfile, String.Join("\n", colVal));

            //. Random distirbution
            //Random randomDist = new Random(121231);
            //for (int i = 0; i < SAMPLECOUNT; i++)
            //{
            //    interim.Add(randomDist.Next(1, 3000000));
            //}
            //List<double> colVal = interim.ConvertAll(x => (double)x);
            //string distfile = Path.Combine("c:\\temp\\", "Random.txt");
            //File.WriteAllText(distfile, String.Join("\n", colVal));
            ////List<double> colVal = samples.Select(i => Math.Floor(i)).ToList();


            // We have a list of stat steps, 
            // Now use that for queries that we need


            // Laplace
            // ------------------------
            //Laplace.Samples(samples, 1000.0, 200.0);
            //List<double> colVal = samples.Select(i => Math.Floor(i)).ToList();
            //string distfile = Path.Combine("c:\\temp\\", "laplace.txt");
            //File.WriteAllText(distfile, String.Join("\n", colVal));


            // Cauchy
            // ===========================
            //var n = new Cauchy(100,1000);
            //n.Samples(samples);
            //List<double> colVal = samples.Select(i => Math.Floor(i)).ToList();
            //string distfile = Path.Combine("c:\\temp\\", "cauchy.txt");
            //File.WriteAllText(distfile, String.Join("\n", colVal));


            // Chi
            //==============================
            //var n = new Chi(3, new Random(1000));
            //n.Samples(samples);
            //List<double> colVal = samples.Select(i => Math.Floor(i*1000.0)).ToList();
            //string distfile = Path.Combine("c:\\temp\\", "ch.txt");
            //File.WriteAllText(distfile, String.Join("\n", colVal));


            // Pareto distritubution
            //============================
            //var n = new Pareto(0.5, 1.5);
            //n.Samples(samples);
            //List<double> colVal = samples.Select(i => Math.Floor(i * 100.0)).ToList();
            //string distfile = Path.Combine("c:\\temp\\", "pareto.txt");
            //File.WriteAllText(distfile, String.Join("\n", colVal));


            //Zipfian
            //=============================
            //var n = new Zipf(1, 31150);
            //var samp = n.Samples();
            //List<double> colVal = new List<double>();
            //int count = 0;
            //foreach (int i in samp)
            //{
            //    colVal.Add(i*10);
            //    count++;
            //    if (count == 10000)
            //        break;
            //}
            ////.ToList().ConvertAll(x=>(double)x).ToArray();
            ////List<double> colVal = samples.Select(i => Math.Floor(i * 100.0)).ToList();
            //string distfile = Path.Combine("c:\\temp\\", "zipf.txt");
            //File.WriteAllText(distfile, string.Join("\n", colVal));


            // MultiNormal ( make sure to do 3 from one format and then one with drichlet
            //=============================================
            //int Min = 0;
            //int Max = 10;
            //const int HOWMANY = 20;
            //Random randNum = new Random();
            //double[] test2 = Enumerable.Repeat(0, HOWMANY).Select(i => randNum.Next(Min, Max)).ToList().ConvertAll(x=>(double)(x)).ToArray();

            //var original = new Multinomial(test2,10000);

            //var estimated = original.Samples().Take(SAMPLECOUNT/ HOWMANY);
            //var samples = new double[SAMPLECOUNT];
            //int count = 0;
            //foreach (var v in estimated)
            //{
            //    for (int i = 0; i < HOWMANY; i++)
            //    {
            //        samples[count*HOWMANY + i] = v[i];
            //    }
            //    count++;
            //}
            //List<double> colVal = samples.Select(i => Math.Floor(i)).ToList();
            //string distfile = Path.Combine("c:\\temp\\", "multinorm2.txt");
            //File.WriteAllText(distfile, string.Join("\n", colVal));

            // Drichlet
            //=======================================
            int Min = 1000000;
            int Max = 1000100;
            const int HOWMANY = 3;
            Random randNum = new Random();
            //double[] test2 = Enumerable.Repeat(0, HOWMANY).Select(i => randNum.Next(Min, Max)).ToList().ConvertAll(x => (double)(x)).ToArray();

            var original = new Dirichlet(1.2, SAMPLECOUNT);
            var samples = new double[SAMPLECOUNT];

            var estimated = original.Sample();
            //samples[i] = Convert.ToDouble(estimated);

            List<double> colVal = estimated.Select(i => Math.Floor(i * randNum.Next(Min, Max))).ToList();
            //string distfile = Path.Combine("c:\\temp\\", "dirichlet.txt");
            //File.WriteAllText(distfile, string.Join("\n", colVal));

            //Normal.Samples(SystemRandomSource.Default, samples, 1000.6, 200);

            // Now run the test, that the data is generated.
            RunTestsOnceDataIsGenerated(FoldToCreateFiles, colVal);


        }

        /// <summary>
        /// Refactoring from the above function, so that we have t
        /// </summary>
        static void RunTestsOnceDataIsGenerated(string FoldToCreateFiles, List<double> colVal)
        {

            // Now sort the data , that you got from the 
            // different data distribution generators.
            colVal.Sort();


            // Need to create a sum list which helps with 
            // fast calcualtions later on
            for (int i = 0; i < SAMPLECOUNT; i++)
            {
                SumAll[i] = (i == 0) ? colVal[i] : (SumAll[i - 1] + colVal[i]);
            }

            double min = colVal.First();
            double last = colVal.Last();
            string quickFile = Path.Combine(folderName, "allvalues.csv");
            if (false)
            {
                quickFile = Path.Combine(folderName, "allvalues.csv");

                File.WriteAllText(quickFile, String.Join(",", colVal));
            }

            if (false)
            {
                colVal.Clear();
                char[] delim = { ',' };
                colVal = File.ReadAllText(quickFile).Split(delim).Select(Double.Parse).ToList();
            }

            // Now create some rangeRow queries
            string quickFileForRanges = Path.Combine(folderName, "rangevlaue.csv");
            List<Tuple<int, int>> rangeRows = new List<Tuple<int, int>>();


            // Should select between the range queries to be read from text
            // File or directly regenerate
            if (false)
            {
                string[] lines = System.IO.File.ReadAllLines(quickFileForRanges);

                foreach (var l in lines)
                {
                    string[] join = l.Split(new char[] { ',' });
                    Tuple<int, int> ti = new Tuple<int, int>(Convert.ToInt32(join[0]), Convert.ToInt32(join[1]));
                    rangeRows.Add(ti);
                }
            }
            else
            {
                for (int i = 0; i < RANGENUM; i++)
                {
                    int val = random.Next((int)min, (int)last - (int)(.8 * (last - min)));
                    Tuple<int, int> ti = new Tuple<int, int>(val, random.Next(val + 1, (int)last));
                    rangeRows.Add(ti);
                }
            }

            if (false)
            {

                File.WriteAllText(quickFileForRanges, "");
                StreamWriter swq = File.AppendText(quickFileForRanges);

                foreach (var va in rangeRows)
                {
                    swq.WriteLine(string.Format("{0} , {1}", va.Item1, va.Item2));
                }
                swq.Flush();
                swq.Close();
                return;
            }

            // For each type of the num ( EMQ, DCT, RGE 
            // run test ) and find out the error range, the
            // csv files generated will be under a different folder each time


            RunTestForTypeTest(colVal, FoldToCreateFiles, rangeRows);
            FillTimeSeriesDataForTests(FoldToCreateFiles);

        }
        /// <summary>
        /// Print all the times, that will help
        /// 
        /// </summary>
        /// <param name="FoldToCreateFiles"></param>

        static void FillTimeSeriesDataForTests(string FoldToCreateFiles)
        {

            var csv = new StringBuilder();
            var newLine = string.Format("{0},{1},{2},{3},{4}", tsQuantile.ToString("s\\.fff"),
                tsVOptimal.ToString("s\\.fff"),
                tsEquiD.ToString("s\\.fff"),
                tsEquiW.ToString("s\\.fff"),
                tsAlgo.ToString("s\\.fff"));
            csv.AppendLine(newLine);


            //string fileNameForFirstStatErrorRate = "QuantileBased.csv";


            string TImeFullname = Path.Combine(FoldToCreateFiles, "TimeFile.csv");

            //after your loop
            File.WriteAllText(TImeFullname, csv.ToString());
        }


        /// <summary>
        /// This will help reduce the number of lines in the main
        /// function , as it was taking prohibitivley wrong to add
        /// and maintain for all.
        /// </summary>
        /// <param name="t"></param>
        /// <param name="colVal"></param>
        static void RunTestForTypeTest(List<double> colVal, string FoldToCreateFiles, List<Tuple<int, int>> rangeRows)
        {
            List<double> ActualEMQErrorRateList = new List<double>();
            List<double> EMQErrorRateList = new List<double>();
            List<double> RGEErrorRateList = new List<double>();
            List<double> DCTErrorRateList = new List<double>();

            int num = 0;
            List<ErrorListElem> erroriterator = new List<ErrorListElem>();

            // Create a list of steps.
            List<StatStep> lisEquiDepth = new List<StatStep>();
            List<StatStep> lisEquiWidth = new List<StatStep>();
            List<StatStep> lisVOptimal = new List<StatStep>();
            List<StatStep> lisQuantile = new List<StatStep>();
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
            StringBuilder csv;
            string fileNameForFirstStatErrorRate;
            int cnt1 = 0;


            if (!Directory.Exists(FoldToCreateFiles))
            {
                Directory.CreateDirectory(FoldToCreateFiles);
            }

            /// Create one folder for each case.
            foreach (TYPETEST t in Enum.GetValues(typeof(TYPETEST)))
            {
                string SubFoldToCreateFiles = Path.Combine(FoldToCreateFiles, t.ToString());
                if (!Directory.Exists(SubFoldToCreateFiles))
                {
                    Directory.CreateDirectory(SubFoldToCreateFiles);
                }
            }


            if (fQuantile)
            {

                //========================================
                // Quantile implementation 
                //========================================
                List<ErrorListElem> AEMQeeQuantileIterator = new List<ErrorListElem>();
                List<ErrorListElem> EMQeeQuantileIterator = new List<ErrorListElem>();
                List<ErrorListElem> RGEeeQuantileIterator = new List<ErrorListElem>();
                List<ErrorListElem> DCTeeQuantileIterator = new List<ErrorListElem>();

                ActualEMQErrorRateList.Clear();
                EMQErrorRateList.Clear();
                RGEErrorRateList.Clear();
                DCTErrorRateList.Clear();


                DateTime start = DateTime.Now;
                // Also create the stat steps for the equi depth case.
                //CreateVOptimalHistogram(colVal, lisEquiDepth, sumTotal);
                CreateQuantileBasedHistogram(colVal, lisQuantile, sumTotal);
                DateTime end = DateTime.Now;
                tsQuantile = end - start;

                // Once the algorithm has created the histogram, it is now time
                // to find all the errors that came out of it.
                GetAEMQErrorList(rangeRows, colVal, ActualEMQErrorRateList, AEMQeeQuantileIterator, lisQuantile);
                GetEMQErrorList(rangeRows, colVal, EMQErrorRateList, EMQeeQuantileIterator, lisQuantile);
                GetRGEErrorList(rangeRows, colVal, RGEErrorRateList, RGEeeQuantileIterator, lisQuantile);
                GetDCTErrorList(rangeRows, colVal, DCTErrorRateList, DCTeeQuantileIterator, lisQuantile);


                FillFiles(FoldToCreateFiles,
                    rangeRows,
                    EMQErrorRateList, RGEErrorRateList, DCTErrorRateList, ActualEMQErrorRateList,
                    "QuantileBased.csv",
                    EMQeeQuantileIterator,
                    RGEeeQuantileIterator,
                    DCTeeQuantileIterator,
                    AEMQeeQuantileIterator);
            }


            if (fVOptimal)
            {
                //========================================
                // VOPtimal implementation 
                //========================================
                List<ErrorListElem> AEMQeeVOptimalIterator = new List<ErrorListElem>();
                List<ErrorListElem> EMQeeVOptimalIterator = new List<ErrorListElem>();
                List<ErrorListElem> RGEeeVOptimalIterator = new List<ErrorListElem>();
                List<ErrorListElem> DCTeeVOptimalIterator = new List<ErrorListElem>();

                ActualEMQErrorRateList.Clear();
                EMQErrorRateList.Clear();
                RGEErrorRateList.Clear();
                DCTErrorRateList.Clear();

                EMQeeVOptimalIterator.Clear();
                RGEeeVOptimalIterator.Clear();
                DCTeeVOptimalIterator.Clear();
                AEMQeeVOptimalIterator.Clear();


                DateTime start = DateTime.Now;

                // Also create the stat steps for the equi depth case.
                //CreateVOptimalHistogram(colVal, lisEquiDepth, sumTotal);
                CreateVOptimalHistogramJag(colVal, lisVOptimal, sumTotal);
                DateTime end = DateTime.Now;
                tsVOptimal = end - start;



                GetEMQErrorList(rangeRows, colVal, ActualEMQErrorRateList, AEMQeeVOptimalIterator, lisVOptimal);
                GetEMQErrorList(rangeRows, colVal, EMQErrorRateList, EMQeeVOptimalIterator, lisVOptimal);
                GetRGEErrorList(rangeRows, colVal, RGEErrorRateList, RGEeeVOptimalIterator, lisVOptimal);
                GetDCTErrorList(rangeRows, colVal, DCTErrorRateList, DCTeeVOptimalIterator, lisVOptimal);

                // We have the erroRate list and now we will sort it
                EMQErrorRateList.Sort();
                EMQErrorRateList.ForEach(Console.WriteLine);



                FillFiles(FoldToCreateFiles,
                    rangeRows,
                    EMQErrorRateList, RGEErrorRateList,
                    DCTErrorRateList, ActualEMQErrorRateList,
                    "VOptimalHistogram.csv",
                    EMQeeVOptimalIterator,
                    RGEeeVOptimalIterator,
                    DCTeeVOptimalIterator,
                    AEMQeeVOptimalIterator);

            }

            if (fEquiD)
            {
                //========================================
                // Equi Depth implementation
                //===========================================
                List<ErrorListElem> EquiDAEMQeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> EquiDEMQeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> EquiDRGEeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> EquiDDCTeeDepthIterator = new List<ErrorListElem>();


                ActualEMQErrorRateList.Clear();
                EMQErrorRateList.Clear();
                RGEErrorRateList.Clear();
                DCTErrorRateList.Clear();


                DateTime start = DateTime.Now;
                // Also create the stat steps for the equi depth case.
                CreateHistogramFromEquiDepth(colVal, lisEquiDepth, sumTotal);
                DateTime end = DateTime.Now;
                tsEquiD = end - start;

                GetAEMQErrorList(rangeRows, colVal, ActualEMQErrorRateList, EquiDAEMQeeDepthIterator, lisEquiDepth);
                GetEMQErrorList(rangeRows, colVal, EMQErrorRateList, EquiDEMQeeDepthIterator, lisEquiDepth);
                GetRGEErrorList(rangeRows, colVal, RGEErrorRateList, EquiDRGEeeDepthIterator, lisEquiDepth);
                GetDCTErrorList(rangeRows, colVal, DCTErrorRateList, EquiDDCTeeDepthIterator, lisEquiDepth);

                // We have the erroRate list and now we will sort it
                EMQErrorRateList.Sort();
                EMQErrorRateList.ForEach(Console.WriteLine);


                RGEErrorRateList.Sort();
                DCTErrorRateList.Sort();

                FillFiles(FoldToCreateFiles,
                    rangeRows,
                    EMQErrorRateList, RGEErrorRateList, DCTErrorRateList, ActualEMQErrorRateList,
                    "EquiDepthHistogram.csv",
                    EquiDEMQeeDepthIterator,
                    EquiDRGEeeDepthIterator,
                    EquiDDCTeeDepthIterator,
                    EquiDAEMQeeDepthIterator);

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

                //            lisEquiWidth.Add(ss);
                //        }
                //    }
                //}

            }

            if (fEquiW)
            {
                List<ErrorListElem> EquiWAEMQeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> EquiWEMQeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> EquiWRGEeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> EquiWDCTeeDepthIterator = new List<ErrorListElem>();

                ActualEMQErrorRateList.Clear();
                EMQErrorRateList.Clear();
                RGEErrorRateList.Clear();
                DCTErrorRateList.Clear();

                //==========================
                // Equi Width code
                //=================
                DateTime start = DateTime.Now;

                Histogram h = CreateHistogramFromEquiWidth(colVal, lisEquiWidth, sumTotal);
                DateTime end = DateTime.Now;
                tsEquiW = end - start;


                // We have range queries
                GetAEMQErrorList(rangeRows, colVal, ActualEMQErrorRateList, EquiWAEMQeeDepthIterator, lisEquiWidth);
                GetEMQErrorList(rangeRows, colVal, EMQErrorRateList, EquiWEMQeeDepthIterator, lisEquiWidth);
                GetRGEErrorList(rangeRows, colVal, RGEErrorRateList, EquiWRGEeeDepthIterator, lisEquiWidth);
                GetDCTErrorList(rangeRows, colVal, DCTErrorRateList, EquiWDCTeeDepthIterator, lisEquiWidth);

                // We have the erroRate list and now we will sort it
                EMQErrorRateList.Sort();
                EMQErrorRateList.ForEach(Console.WriteLine);
                RGEErrorRateList.Sort();
                DCTErrorRateList.Sort();


                FillFiles(FoldToCreateFiles,
                         rangeRows,
                         EMQErrorRateList, RGEErrorRateList,
                         DCTErrorRateList, ActualEMQErrorRateList,
                         "EquiWidthHistogram.csv",
                         EquiWEMQeeDepthIterator,
                         EquiWRGEeeDepthIterator,
                         EquiWDCTeeDepthIterator,
                         EquiWAEMQeeDepthIterator);

            }

            if (fAlgo)
            {

                List<ErrorListElem> AlgoAEMQeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> AlgoEMQeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> AlgoRGEeeDepthIterator = new List<ErrorListElem>();
                List<ErrorListElem> AlgoDCTeeDepthIterator = new List<ErrorListElem>();

                ActualEMQErrorRateList.Clear();
                EMQErrorRateList.Clear();
                RGEErrorRateList.Clear();
                DCTErrorRateList.Clear();
                List<StatStep> histLs = new List<StatStep>();

                DateTime start = DateTime.Now;
                // This is the portion where the actual magic happens 
                // from within the algorithm
                CreateHistogramFromAlgorithm(colVal, histLs, 2);
                // We are working on the follow
                //  we have te new histogram. We will now need to do the same estimation things
                DateTime end = DateTime.Now;
                tsAlgo = end - start;


                GetAEMQErrorList(rangeRows, colVal, ActualEMQErrorRateList, AlgoAEMQeeDepthIterator, histLs);
                GetEMQErrorList(rangeRows, colVal, EMQErrorRateList, AlgoEMQeeDepthIterator, histLs);
                GetRGEErrorList(rangeRows, colVal, RGEErrorRateList, AlgoRGEeeDepthIterator, histLs);
                GetDCTErrorList(rangeRows, colVal, DCTErrorRateList, AlgoDCTeeDepthIterator, histLs);

                FillFiles(FoldToCreateFiles,
                      rangeRows,
                      EMQErrorRateList, RGEErrorRateList,
                      DCTErrorRateList, ActualEMQErrorRateList,
                      "AlgorithmHistogram.csv",
                      AlgoEMQeeDepthIterator,
                      AlgoRGEeeDepthIterator,
                      AlgoDCTeeDepthIterator,
                      AlgoAEMQeeDepthIterator
                      );

            }
        }
        // Have a mechanism to quickly create a error list for agiven range rows
        // for apples to apple comparion the random ranges need to be on the same value boundaries 
        // for the lower and the upper boundaries.
        static void GetRGEErrorList(List<Tuple<int, int>> rangeRows, List<double> colVal, List<double> errorRateList, List<ErrorListElem> erroriterator, List<StatStep> lisS)
        {
            int num = 0;
            foreach (var tup in rangeRows)
            {
                // we need to query how many values are there. 
                var count = colVal.Select(x => x).Count(r => r >= tup.Item1 && r <= tup.Item2);

                // Now we have the actual count, 
                //next we need the count from stat
                var countFromStat = CalculateRGEValuesFromHistogram(tup.Item1, tup.Item2, lisS);

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

        /// <summary>
        /// This will only fill the files so that we can add the information in the folder needed
        /// </summary>
        /// <param name="EMQErrorRateList"></param>
        /// <param name="RGEErrorRateList"></param>
        /// <param name="DCTErrorRateList"></param>
        /// <param name="fileNameForFirstStatErrorRate"></param>
        /// <param name="EMQFullname"></param>
        /// <param name="RGEFullname"></param>
        /// <param name="DCTFullname"></param>
        /// <param name="EMQeeQuantileIterator"></param>
        /// <param name="RGEeeQuantileIterator"></param>
        /// <param name="DCTeeQuantileIterator"></param>
        static void FillFiles(
            string FoldToCreateFiles,
            List<Tuple<int, int>> rangeRows,
            List<double> EMQErrorRateList, List<double> RGEErrorRateList,
            List<double> DCTErrorRateList, List<double> AEMQrrorRateList,
            string fileNameForFirstStatErrorRate,

            List<ErrorListElem> EMQeeQuantileIterator,
            List<ErrorListElem> RGEeeQuantileIterator,
            List<ErrorListElem> DCTeeQuantileIterator,
            List<ErrorListElem> AEMQeeQuantileIterator)
        {
            string EMQFullname;
            string RGEFullname;
            string DCTFullname;
            string AEMQFullname;


            // We have the erroRate list and now we will sort it
            EMQErrorRateList.Sort();
            EMQErrorRateList.ForEach(Console.WriteLine);
            AEMQrrorRateList.Sort();
            RGEErrorRateList.Sort();
            DCTErrorRateList.Sort();

            var csv = new StringBuilder();

            int cnt1 = 0;
            foreach (var v in EMQeeQuantileIterator)
            {
                var newLine = string.Format("{0},{1},{2},{3},{4},{5}", rangeRows[v.num].Item1, rangeRows[v.num].Item2, v.num, v.countAct, v.countStat, v.qE);
                csv.AppendLine(newLine);
                cnt1++;
            }
            //string fileNameForFirstStatErrorRate = "QuantileBased.csv";


            EMQFullname = Path.Combine(FoldToCreateFiles, "EMQ", fileNameForFirstStatErrorRate);

            //after your loop
            File.WriteAllText(EMQFullname, csv.ToString());
            EMQErrorRateList.Clear();



            csv = new StringBuilder();
            cnt1 = 0;
            foreach (var v in RGEeeQuantileIterator)
            {
                var newLine = string.Format("{0},{1},{2},{3},{4},{5}", rangeRows[v.num].Item1, rangeRows[v.num].Item2, v.num, v.countAct, v.countStat, v.qE);
                csv.AppendLine(newLine);
                cnt1++;
            }




            RGEFullname = Path.Combine(FoldToCreateFiles, "RGE", fileNameForFirstStatErrorRate);

            //after your loop
            File.WriteAllText(RGEFullname, csv.ToString());
            RGEErrorRateList.Clear();



            csv = new StringBuilder();
            cnt1 = 0;
            foreach (var v in DCTeeQuantileIterator)
            {
                var newLine = string.Format("{0},{1},{2},{3},{4},{5}", rangeRows[v.num].Item1, rangeRows[v.num].Item2, v.num, v.countAct, v.countStat, v.qE);
                csv.AppendLine(newLine);
                cnt1++;
            }

            DCTFullname = Path.Combine(FoldToCreateFiles, "DCT", fileNameForFirstStatErrorRate);

            //after your loop
            File.WriteAllText(DCTFullname, csv.ToString());
            DCTeeQuantileIterator.Clear();


            csv = new StringBuilder();
            cnt1 = 0;
            foreach (var v in AEMQeeQuantileIterator)
            {
                var newLine = string.Format("{0},{1},{2},{3},{4},{5}", rangeRows[v.num].Item1, rangeRows[v.num].Item2, v.num, v.countAct, v.countStat, v.qE);
                csv.AppendLine(newLine);
                cnt1++;
            }

            AEMQFullname = Path.Combine(FoldToCreateFiles, "AEMQ", fileNameForFirstStatErrorRate);

            //after your loop
            File.WriteAllText(AEMQFullname, csv.ToString());
            AEMQeeQuantileIterator.Clear();

        }

        /// <summary>
        /// Actual keys within the table
        /// </summary>
        /// <param name="rangeRows"></param>
        /// <param name="colVal"></param>
        /// <param name="errorRateList"></param>
        /// <param name="erroriterator"></param>
        /// <param name="lisS"></param>
        static void GetAEMQErrorList(List<Tuple<int, int>> rangeRows, List<double> colVal, List<double> errorRateList, List<ErrorListElem> erroriterator, List<StatStep> lisS)
        {
            int num = 0;
            List<double> keyEl = new List<double>(dictCol.Keys);


            foreach (var tup in keyEl)
            {
                if (num >= rangeRows.Count)
                {
                    break;

                }


                // we need to query how many values are there. 
                int count = 0;

                if (dictCol.ContainsKey(tup))
                {
                    count = dictCol[tup];
                }
                else
                {
                    count = 0;
                }

                // Now we have the actual count, 
                //next we need the count from stat
                var countFromStat = CalculateEMQValuesFromHistogram(tup, lisS);

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

        /// <summary>
        /// Range Range Errors
        /// </summary>
        /// <param name="rangeRows"></param>
        /// <param name="colVal"></param>
        /// <param name="errorRateList"></param>
        /// <param name="erroriterator"></param>
        /// <param name="lisS"></param>
        static void GetEMQErrorList(List<Tuple<int, int>> rangeRows, List<double> colVal, List<double> errorRateList, List<ErrorListElem> erroriterator, List<StatStep> lisS)
        {
            int num = 0;
            List<double> keyEl = new List<double>(dictCol.Keys);


            foreach (var tup in rangeRows)
            {
                // we need to query how many values are there. 
                int count = 0;

                if (dictCol.ContainsKey(tup.Item1))
                {
                    count = dictCol[tup.Item1];
                }
                else
                {
                    count = 0;
                }

                // Now we have the actual count, 
                //next we need the count from stat
                var countFromStat = CalculateEMQValuesFromHistogram(tup.Item1, lisS);

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


        static void GetDCTErrorList(List<Tuple<int, int>> rangeRows, List<double> colVal, List<double> errorRateList, List<ErrorListElem> erroriterator, List<StatStep> lisS)
        {
            int a = 1500;
            int num = 0;    //(a - (a*1 +a*1 - a*1));
            foreach (var tup in rangeRows)
            {
                // we need to query how many values are there. 
                var count = colVal.Select(x => x).Select(r => r >= tup.Item1 && r <= tup.Item2).Distinct().Count();

                // Now we have the actual count, 
                //next we need the count from stat
                var countFromStat = CalculateDCTValuesFromHistogram(tup.Item1, tup.Item2, lisS);

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

        /// <summary>
        /// Will return the error sSE for a range
        /// </summary>
        /// <param name="colVal"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        static double CalculateErrorInRange(List<double> colVal, int start, int end)
        {

            if (start == end)
            {
                return 0;
            }

            var tuple = new Tuple<int, int>(start, end);
            // Since we will cache a lot of values, this will reduce one mroe caulcations
            if (HashMapDictionary.ContainsKey(tuple))
            {
                // Need to return that using the tuplkey
                return HashMapDictionary[tuple];
            }

            int count = end - start + 1;

            double sum = SumAll[end] - SumAll[start];

            double mean = (double)sum / (double)count;

            double errorsum = 0;
            for (int i = start; i < end; i++)
            {
                errorsum += Math.Pow(colVal[i] - mean, 2);
            }

            // Now add it to the hash map and the dicionary
            HashMapDictionary.Add(tuple, errorsum);
            return errorsum;
        }

        static double SqError(int start, int end, ref long[] SumFreq, ref long[] SumSquareFreq)
        {
            double s1 = SumFreq[end] - SumFreq[start];
            double s2 = SumSquareFreq[end] - SumSquareFreq[start];

            return (s2 - (s1 * s1) / (double)(end - start + 1));
        }

        /// <summary>
        /// QUantile based will help divide, the numebr of rows into a quantile and if the number of elements iw within that uantile select that and allow it to be the used 
        /// as the boucket boudnaries
        /// </summary>
        /// <param name="colVal"></param>
        /// <param name="listS"></param>
        /// <param name="sumTotal"></param>
        static void CreateQuantileBasedHistogram(List<double> colVal, List<StatStep> listS, int sumTotal)
        {
            List<double> keyEl = new List<double>(dictCol.Keys);
            int numberOfElements = colVal.Count / STEPS;

            int prevEnd = -1;
            for (int i = 0; i < STEPS; i++)
            {
                double val = colVal[numberOfElements * (i + 1) - 1];
                StatStep ss = new StatStep();
                ss.range_high_key = (int)val;
                ss.equal_rows = dictCol[val];

                int j = 0;
                for (j = 0; j < keyEl.Count; j++)
                {
                    if (keyEl[j] == val)
                    {
                        break;
                    }
                }

                if (prevEnd != -1)
                {
                    for (int k = prevEnd; k < j; k++)
                    {
                        ss.range_rows += dictCol[keyEl[k]];
                        ss.distint_range_rows++;
                    }
                }
                else
                {
                    for (int k = 0; k < j; k++)
                    {
                        ss.range_rows += dictCol[keyEl[k]];
                        ss.distint_range_rows++;
                    }
                }

                ss.average_range_rows = ss.range_rows / ((ss.distint_range_rows == 0) ? 1 : ss.distint_range_rows);

                listS.Add(ss);

                // if this is the last element, then ther eis nothing to be looked for more 
                if (j == keyEl.Count)
                    break;
            }

        }
        /// <summary>
        /// Implementaiton by Jagadeesh taken from
        /// http://www.mathcs.emory.edu/~cheung/Courses/584/Syllabus/06-Histograms/Progs/Jagadish.java
        /// The original author of v-optimal
        /// </summary>
        /// <param name="colVal"></param>
        /// <param name="listS"></param>
        /// <param name="sumTotal"></param>
        static void CreateVOptimalHistogramJag(List<double> colVal, List<StatStep> listS, int sumTotal)
        {
            List<double> keyEl = new List<double>(dictCol.Keys);
            double[,] VarianceArray = new double[STEPS, keyEl.Count];
            int[,] xarray = new int[STEPS, keyEl.Count];
            int[] minindex = new int[keyEl.Count];
            long[] SumFreq = new long[keyEl.Count];
            long[] sumSquare = new long[keyEl.Count];

            for (int i = 0; i < keyEl.Count; i++)
            {
                SumFreq[i] = (i == 0) ? dictCol[keyEl[i]] : SumFreq[i - 1] + dictCol[keyEl[i]];
                sumSquare[i] = (i == 0) ? dictCol[keyEl[i]] * dictCol[keyEl[i]] : (sumSquare[i - 1] + dictCol[keyEl[i]] * dictCol[keyEl[i]]);
            }

            for (int i = 0; i < STEPS; i++)
            {
                for (int j = 0; j < keyEl.Count; j++)
                {
                    if (i == 0)
                    {
                        VarianceArray[i, j] = SqError(i, j, ref SumFreq, ref sumSquare);
                    }
                    else if (j > i)
                    {
                        double vMin = Double.MaxValue;

                        for (int k = 0; k < j; k++)
                        {
                            int start1 = 0;
                            int end1 = k;
                            int star2 = end1 + 1;
                            int end2 = j;

                            double v = VarianceArray[i - 1, k] + SqError(star2, end2, ref SumFreq, ref sumSquare);

                            if (v < vMin)
                            {
                                vMin = v;
                                xarray[i, j] = k;
                                minindex[j] = star2;
                                VarianceArray[i, j] = v;
                            }

                        }
                    }
                }
            }

            int end = 0, start = 0;
            for (int i = 0; i < STEPS; i++)
            {

                if (i == 0)
                {
                    StatStep ss = new StatStep();
                    end = keyEl.Count - 1;
                    start = minindex[keyEl.Count - 1];
                    ss.equal_rows = dictCol[keyEl[end]];
                    ss.range_high_key = (int)keyEl[end];

                    if (start == end)
                    {
                        //special case, where the entire range
                        // had same value , which ideally should not happen
                        int a = 10;
                        continue;
                    }

                    for (int k = start; k < end; k++)
                    {
                        if (keyEl[k] == ss.range_high_key) break;
                        ss.range_rows += dictCol[keyEl[k]];

                        ss.distint_range_rows++;
                    }

                    ss.average_range_rows = ss.range_rows / ss.distint_range_rows;
                    listS.Add(ss);
                }
                else
                {
                    end = start - 1;
                    while (start >= 0 && start == minindex[start])
                    {
                        start--;
                    }

                    if (start < 0)
                    {
                        break;
                    }

                    start = minindex[start];
                    if (end == start)
                    {
                        start--;

                    }
                    if (start < 0)
                    {
                        break;
                    }
                    StatStep ss = new StatStep();
                    ss.equal_rows = dictCol[keyEl[end]];
                    ss.range_high_key = (int)keyEl[end];

                    if (start == end)
                    {
                        int a = 10;
                    }

                    for (int k = start; k < end; k++)
                    {
                        if (keyEl[k] == ss.range_high_key) break;
                        ss.range_rows += dictCol[keyEl[k]];

                        ss.distint_range_rows++;
                    }


                    if (ss.distint_range_rows == 0)
                    {
                        int a = 10;
                    }
                    ss.average_range_rows = ss.range_rows / ss.distint_range_rows;
                    listS.Insert(0, ss);

                }
            }

        }


        /// <summary>
        /// Converted from the python solution
        /// https://github.com/mikalcelay/V_Optimal_Histogram/blob/master/main.py
        /// The code is simple and follows , frequency as the srouce parameter
        /// the value as the sort parameter.
        /// One more algorithm can be used, where the area will be used for optimization 
        /// and not the frequency.
        /// </summary>
        /// <param name="colVal"></param>
        /// <param name="listS"></param>
        /// <param name="sumTotal"></param>
        static void CreateVOptimalHistogram(List<double> colVal, List<StatStep> listS, int sumTotal)
        {
            double[,] VarianceArray = new double[STEPS, colVal.Count];
            int[,] xarray = new int[STEPS, colVal.Count];
            int[] minindex = new int[STEPS];

            for (int i = 0; i < STEPS; i++)
            {
                for (int j = 0; j < colVal.Count; j++)
                {


                    if (i == 0)
                    {
                        VarianceArray[i, j] = CalculateErrorInRange(colVal, i, j);
                    }
                    else if (j > i)
                    {
                        double vMin = Double.MaxValue;

                        for (int k = 1; k < j; k++)
                        {
                            int start1 = 0;
                            int end1 = k;
                            int star2 = end1 + 1;
                            int end2 = j;

                            double v = VarianceArray[i - 1, k] + CalculateErrorInRange(colVal, star2, end2);

                            if (v < vMin)
                            {
                                vMin = v;
                                xarray[i, j] = k;
                                minindex[i] = k;
                            }
                            VarianceArray[i, j] = vMin;
                        }
                    }
                }
            }


            for (int i = 0; i < STEPS; i++)
            {
                int end = 0, start = 0;
                if (i == 0)
                {
                    StatStep ss = new StatStep();
                    end = colVal.Count - 1;
                    start = xarray[STEPS - 1, colVal.Count - 1] + 1;
                    ss.equal_rows = dictCol[colVal[colVal.Count - 1]];
                    ss.range_high_key = (int)colVal[colVal.Count - 1];

                    while (start < end && colVal[start] == colVal[start + 1])
                    {
                        start++;
                    }

                    if (start == end)
                    {
                        //special case, where the entire range
                        // had same value , which ideally should not happen
                        int a = 10;
                        continue;
                    }

                    double prev = -1;
                    for (int k = start; k < end; k++)
                    {
                        if (colVal[k] == ss.range_high_key) break;
                        ss.range_rows++;

                        if (prev != colVal[k])
                        {
                            ss.distint_range_rows++;
                        }

                        prev = colVal[k];
                    }

                    ss.average_range_rows = ss.range_rows / ss.distint_range_rows;
                    listS.Add(ss);
                }
                else
                {
                    end = start - 1;
                    start = xarray[STEPS - 1 - i, end] + 1;

                    StatStep ss = new StatStep();
                    ss.equal_rows = dictCol[colVal[end]];
                    ss.range_high_key = (int)colVal[end];

                    while (start < end && colVal[start] == colVal[start + 1])
                    {
                        start++;
                    }

                    if (start == end)
                    {
                        int a = 10;
                    }
                    double prev = -1;
                    for (int k = start; k < end; k++)
                    {
                        if (colVal[k] == ss.range_high_key) break;
                        ss.range_rows++;

                        if (prev != colVal[k])
                        {
                            ss.distint_range_rows++;
                        }

                        prev = colVal[k];
                    }

                    ss.average_range_rows = ss.range_rows / ss.distint_range_rows;
                    listS.Insert(0, ss);

                }
            }


        }

        // This one fgollows the simple principle of divigding
        // the total number of elements and then dividing the same with the number of steps
        // then do a simple iterative process to achieved the singular array of equidepth buckets
        static void CreateHistogramFromEquiDepth(List<double> colVal, List<StatStep> listS, int sumTotal)
        {
            int countInEach = sumTotal / STEPS;
            List<double> keyEl = new List<double>(dictCol.Keys);
            keyEl.Sort();

            for (int i = 0; i < keyEl.Count; i++)
            {
                int sum = 0;
                int pre = 0;
                int count = 0;
                for (; i < keyEl.Count; i++)
                {
                    count++;

                    sum += dictCol[keyEl[i]];
                    if (sum >= countInEach)
                    {
                        StatStep ss = new StatStep();
                        ss.equal_rows = dictCol[keyEl[i]];
                        ss.range_high_key = (int)keyEl[i];
                        ss.distint_range_rows = count - 1;
                        ss.range_rows = pre;
                        ss.average_range_rows = ss.range_rows / ((ss.distint_range_rows == 0) ? 1 : ss.distint_range_rows);
                        listS.Add(ss);
                        break;
                    }

                    pre = sum;
                }
            }
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
                        ss.average_range_rows = ss.range_rows / ((ss.distint_range_rows == 0) ? 1 : ss.distint_range_rows);
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
                        ss.average_range_rows = ss.range_rows / ((ss.distint_range_rows == 0) ? 1 : ss.distint_range_rows);
                    listS.Add(ss);
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
            Dictionary<double, int> stepDictionary = new Dictionary<double, int>();

            List<HeapStatElem> lhse = new List<HeapStatElem>();
            List<double> errorList = new List<double>();


            // Next will be the priority heap implementation
            // So if there are modulo values, they will be picked in the end
            // in a separate process.
            for (int i = 0; i < countHistInit / num - 1; i++)
            {
                HeapStatElem hs = new HeapStatElem();

                hs.range_high_key = keyEl[i * num + num - 1];

                stepDictionary.Add(keyEl[i * num + num - 1], i);

                // For each histogram element , add their values in the q-error metric, which is a temporary thing
                for (int j = 0; j < num - 1; j++)
                {
                    hs.range_rows += dictCol[keyEl[i * num + j]];
                    stepDictionary.Add(keyEl[i * num + j], i);
                }

                hs.equal_rows = dictCol[keyEl[i * num + (num - 1)]];
                hs.distint_range_rows = num - 1;
                hs.average_range_rows = hs.range_rows / ((hs.distint_range_rows == 0) ? 1 : hs.distint_range_rows);
                lhse.Add(hs);
            }

            HeapStatElem hsElem = new HeapStatElem();
            hsElem.equal_rows = dictCol[keyEl[countHistInit - 1]];
            stepDictionary.Add(keyEl[countHistInit - 1], lhse.Count());

            hsElem.range_high_key = keyEl[countHistInit - 1];
            int lastStepTot = 0;
            int d = 0;
            for (int i = ((countHistInit / num) - 1) * num; i < countHistInit - 1; i++)
            {
                if (i < countHistInit - 1)
                {
                    lastStepTot += dictCol[keyEl[i]];
                    stepDictionary.Add(keyEl[i], lhse.Count());
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
                double estimate = CalculateEMQEsitmateFromHist(ref lhse, v, ref stepDictionary);
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

                //if (he.start == 342)
                //{
                //    int a = 10;
                //}
            }

            int heapCount = 0;
            int listLimit = 2 * (heElemList.Count / STEPS + 1);
            bool stillLeft = false;
            // At this point the heap is ready. Now pop and keep merging till the end.
            while (heElemList.Count > STEPS)
            {
                heapCount += 1;

                HeapElem he = heapForWork.Dequeue().Key;

                while (he != null && heapForWork.Count > 0 &&
                    he.mergeerrorList.Count >= listLimit)
                {
                    //heElemList.Remove(he);
                    he = heapForWork.Dequeue().Key;
                }


                if (he == null)
                {
                    stillLeft = true;
                    break;
                }
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

                        //if (heElemList[i - 1].start == 340)
                        //{
                        //    int a = 10;
                        //}

                        heElemList[i - 1] = heNew;
                        break;
                    }
                }

                if (k < heElemList.Count - 1)
                {
                    // Now to merge it with the next element. 
                    // This will be 1 past the next one. 

                    MergeTwoHeapElem(heNew, heElemList[k + 1]);
                    heapForWork.Enqueue(heNew, new Tuple<double, double>(heNew.mergeslope, heNew.mergeintercept));

                }

                if (k < heElemList.Count)
                {
                    //if (heElemList[k].start == 340)
                    //{
                    //    int a = 10;
                    //}

                    if (k + 1 < heElemList.Count &&
                        heapForWork.Contains(new KeyValuePair<HeapElem, Tuple<double, double>>(heElemList[k], new Tuple<double, double>(heElemList[k].mergeslope, heElemList[k].mergeintercept))))
                    {
                        heapForWork.Remove(new KeyValuePair<HeapElem, Tuple<double, double>>(heElemList[k], new Tuple<double, double>(heElemList[k].mergeslope, heElemList[k].mergeintercept)));
                    }
                    else if (k + 1 < heElemList.Count)
                    {
                        int a = 10;
                    }

                    // We will keep removing it after every change.
                    heElemList.RemoveAt(k);

                }

                //if (k > 1 && k < heElemList.Count &&
                //    heElemList[k - 1].end + 1 != heElemList[k].start)
                //{
                //    int a = 10;
                //}

                //// Now do a quick check
                //if (k > 1 && k < heElemList.Count &&
                //    heElemList[k - 2].end + 1 != heElemList[k - 1].start)
                //{
                //    // we have an issue
                //    int a = 10;
                //}

                //if (k < heElemList.Count - 1 && heElemList[k].end + 1 != heElemList[k + 1].start)
                //{
                //    // we have another problem
                //    int a = 10;
                //}

                if (k > 1)
                {
                    var into1 = new KeyValuePair<HeapElem, Tuple<double, double>>(heElemList[k - 2], new Tuple<double, double>(heElemList[k - 2].mergeslope, heElemList[k - 2].mergeintercept));
                    if (heapForWork.Contains(into1))
                    {
                        heapForWork.Remove(into1);
                    }
                    else
                    {
                        int a = 10;
                    }

                    MergeTwoHeapElem(heElemList[k - 2], heElemList[k - 1]);

                    // Once we have modified te k-1 element, that means, that the k-2 guy should have updated merge values. 
                    // For the algorithm to keep getting the reai and latest values, this is needed.
                    heapForWork.Enqueue(heElemList[k - 2], new Tuple<double, double>(heElemList[k - 2].mergeslope, heElemList[k - 2].mergeintercept));
                    // So both the sides are merged and we can have them in the heap inserted back again.
                }

                //while (heElemList.Count > STEPS)
                //{
                //    for (int i = 0; i < heElemList.Count-1; i++)
                //    {

                //    }
                //}

                if (false)
                {

                    for (int i = 0; i < heElemList.Count - 1; i++)
                    {
                        if (heElemList[i].mergeerrorList == null ||
                            heElemList[i].mergeerrorList.Count == 0 ||
                            heElemList[i].mergeerrorList.Count > 20)
                        {
                            int a = 10;
                        }

                    }
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


        /// <summary>
        ///  THis is different that the caulcation from histogram. 
        ///  THis is done on the temporary elemebts and not the
        ///  major histogram
        /// </summary>
        /// <param name="his"></param>
        /// <param name="val"></param>
        /// <returns></returns>
        static double CalculateEMQEsitmateFromHist(ref List<HeapStatElem> his, double val, ref Dictionary<double, int> sortDict)
        {

            double prevHi = Int32.MinValue;

            int value = sortDict[val];
            if (his[value].range_high_key == val)
            {
                return his[value].equal_rows;
            }
            else
            {
                return his[value].average_range_rows;
            }

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


        /// <summary>
        /// The EMQ queries will be only for low.
        /// </summary>
        /// <param name="low"></param>
        /// <param name="lisS"></param>
        /// <returns></returns>
        static int CalculateEMQValuesFromHistogram(double low, List<StatStep> lisS)
        {
            int num = 0;
            while (num < lisS.Count() && (int)lisS[num].range_high_key < low)
            {
                num++;
            }

            if (num == lisS.Count)
            {
                return 0;
            }

            if (lisS[num].range_high_key == low)
            {
                return lisS[num].equal_rows;
            }
            else
            {
                return (int)lisS[num].average_range_rows;
            }



        }

        static int CalculateDCTValuesFromHistogram(int low, int high, List<StatStep> lisS)
        {
            int totCount = 0;
            double prevHi = Int32.MinValue;

            if (low == high && high == lisS[0].range_high_key)
            {

                return 1;
            }

            if (high < lisS[0].range_high_key)
            {
                double frac = (double)(high - low) / lisS[0].range_high_key;
                double numDis = frac * lisS[0].distint_range_rows;
                return (int)numDis;

            }

            for (int i = 0; i < lisS.Count(); i++)
            {
                // First is for the rnage that is below the contained value. 
                if (i == 0 && low < lisS[0].range_high_key && high < lisS[0].range_high_key)
                {
                    double frac = (double)(low - 0) / (double)(lisS[0].range_high_key - 0);
                    double numDis = frac * lisS[0].distint_range_rows;

                    return (int)numDis;

                }
                else if (i == 0 && low < lisS[0].range_high_key && high == lisS[0].range_high_key)
                {
                    double frac = (double)(low - 0) / (double)(lisS[0].range_high_key - 0);
                    double numDis = frac * lisS[0].distint_range_rows;

                    totCount += (int)numDis;
                    totCount += 1;
                    return totCount;
                }
                else if (i == 0 && low < lisS[0].range_high_key && high > lisS[0].range_high_key)
                {
                    double frac = (double)(low - 0) / (double)(lisS[0].range_high_key - 0);
                    double numDis = frac * lisS[0].distint_range_rows;

                    totCount += (int)numDis;
                    continue;
                }
                else if (low <= prevHi && high < lisS[i].range_high_key)
                {
                    double frac = (double)(high - prevHi) / (double)(lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;

                    return totCount + (int)numDis;

                }
                else if (low <= prevHi && high > lisS[i].range_high_key)
                {
                    totCount += (int)lisS[i].distint_range_rows + 1;
                }
                else if (i != 0 && low > prevHi && high <= lisS[i].range_high_key)
                {
                    double frac = (high - low) / (lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;
                    double tot = numDis;
                    tot += (high == lisS[i].range_high_key) ? 1 : 0;
                    return totCount + (int)tot;
                }
                else if (i != 0 && low > prevHi && low < lisS[i].range_high_key &&
                    high > lisS[i].range_high_key)
                {
                    double frac = (lisS[i].range_high_key - low) / (lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;
                    double tot = numDis;
                    totCount += (int)tot + 1;
                }
                else if (low > lisS[i].range_high_key && high > lisS[i].range_high_key)
                {
                    // do nothing, keeping it for all possible combination case
                    if (i == lisS.Count - 1)
                        return totCount;
                }
                else if (low == lisS[i].range_high_key)
                {
                    totCount += 1;
                }

                prevHi = lisS[i].range_high_key;

            }
            return totCount;
        }

        static int CalculateRGEValuesFromHistogram(int low, int high, List<StatStep> lisS)
        {
            int totCount = 0;
            double prevHi = Int32.MinValue;

            if (low == high && high == lisS[0].range_high_key)
            {
                totCount = (int)lisS[0].equal_rows;
                return totCount;
            }

            if (high < lisS[0].range_high_key)
            {
                double frac = (double)(high - low) / lisS[0].range_high_key;
                double numDis = frac * lisS[0].distint_range_rows;
                double tot = numDis * lisS[0].average_range_rows;
                return totCount + (int)tot;

            }

            for (int i = 0; i < lisS.Count(); i++)
            {
                // First is for the rnage that is below the contained value. 
                if (i == 0 && low < lisS[0].range_high_key && high < lisS[0].range_high_key)
                {
                    double frac = (double)(low - 0) / (double)(lisS[0].range_high_key - 0);
                    double numDis = frac * lisS[0].distint_range_rows;

                    totCount += (int)(numDis * (int)lisS[0].average_range_rows);
                    return totCount;

                }
                else if (i == 0 && low < lisS[0].range_high_key && high == lisS[0].range_high_key)
                {
                    double frac = (double)(low - 0) / (double)(lisS[0].range_high_key - 0);
                    double numDis = frac * lisS[0].distint_range_rows;

                    totCount += (int)(numDis * (int)lisS[0].average_range_rows);
                    totCount += lisS[0].equal_rows;
                    return totCount;
                }
                else if (i == 0 && low < lisS[0].range_high_key && high > lisS[0].range_high_key)
                {
                    double frac = (double)(low - 0) / (double)(lisS[0].range_high_key - 0);
                    double numDis = frac * lisS[0].distint_range_rows;

                    totCount += (int)(numDis * (int)lisS[0].average_range_rows);
                    totCount += lisS[0].equal_rows;
                    continue;
                }
                else if (low <= prevHi && high < lisS[i].range_high_key)
                {
                    double frac = (double)(high - prevHi) / (double)(lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;
                    double tot = numDis * lisS[i].average_range_rows;
                    return totCount + (int)tot;

                }
                else if (low <= prevHi && high > lisS[i].range_high_key)
                {
                    totCount += (int)lisS[i].range_rows + (int)lisS[i].equal_rows;
                }
                else if (i != 0 && low > prevHi && high <= lisS[i].range_high_key)
                {
                    double frac = (high - low) / (lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;
                    double tot = numDis * lisS[i].average_range_rows;
                    tot += (high == lisS[i].range_high_key) ? lisS[i].equal_rows : 0;
                    return totCount + (int)tot;
                }
                else if (i != 0 && low > prevHi && low < lisS[i].range_high_key &&
                    high > lisS[i].range_high_key)
                {
                    double frac = (lisS[i].range_high_key - low) / (lisS[i].range_high_key - prevHi);
                    double numDis = frac * lisS[i].distint_range_rows;
                    double tot = numDis * lisS[i].average_range_rows;
                    totCount += (int)tot + lisS[i].equal_rows;
                }
                else if (low > lisS[i].range_high_key && high > lisS[i].range_high_key)
                {
                    // do nothing, keeping it for all possible combination case
                }
                else if (low == lisS[i].range_high_key)
                {
                    totCount += lisS[i].equal_rows;
                }

                prevHi = lisS[i].range_high_key;

            }

            return totCount;
        }
    }

}
