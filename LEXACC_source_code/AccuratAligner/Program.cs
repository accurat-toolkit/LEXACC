/*
 * C# implementation of LEXACC. See the paper:
 * 
 * Dan Ştefănescu, Radu Ion, Sabine Hunsicker, 
 * Hybrid Parallel Sentence Mining from Comparable Corpora, 
 * in Proceedings of the 16th Conference of the European Association for Machine Translation (EAMT 2012), Trento, Italy
 * 
 * (C) ICIA 2011-2012, Author: Dan ŞTEFĂNESCU
 * 
 * www.racai.ro/~danstef
 * danstef@racai.ro
 * dstefanescu@gmail.com
 * 
*/

namespace AccuratAligner
{
    using DataStructUtils;
    using Lucene.Net.Analysis;
    using Lucene.Net.Documents;
    using Lucene.Net.Index;
    using Lucene.Net.Search;
    using PexaccSim;
    using Statistics;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Linq;

    internal class Program
    {
        private static Measure m;
        private static HashSet<string> stopwords;
        private static Dictionary<string, Dictionary<string, double>> te;

        private static void Main(string[] args)
        {
            string inputFile = null;
            string prFile = null;

            string resourcesDir = "." + Path.DirectorySeparatorChar.ToString() + "res";
            string dictionaryDir = "." + Path.DirectorySeparatorChar.ToString() + "dict";

            string outputFile = "results.txt";
            string srcLang = "en";
            string trgLang = "ro";
            bool keepIntermediaryFiles = false;
            bool alreadySegmented = false;
            double threshold = 0;
            bool filter = true;
            bool thresholdMod = false;
            List<string> inputParams = new List<string>();
            int maxRepeated = 1;

            string testFile = null;
            string testFileUnique = null;
            string gsSrc = null;
            string gsTrg = null;
            int lowMemory = 0;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--docalign")
                {
                    if (i + 1 < args.Length)
                    {
                        inputFile = args[i + 1];
                    }
                }
                else if (args[i] == "--res")
                {
                    if (i + 1 < args.Length)
                    {
                        resourcesDir = args[i + 1];
                    }
                }
                else if (args[i] == "--dic")
                {
                    if (i + 1 < args.Length)
                    {
                        dictionaryDir = args[i + 1];
                    }
                }
                else if (args[i] == "--input")
                {
                    if (i + 1 < args.Length)
                    {
                        inputParams.Add(args[i + 1]);
                    }
                }
                else if (args[i] == "--source")
                {
                    if (i + 1 < args.Length)
                    {
                        srcLang = args[i + 1].ToLower();
                    }
                }
                else if (args[i] == "--target")
                {
                    if (i + 1 < args.Length)
                    {
                        trgLang = args[i + 1].ToLower();
                    }
                }
                else if (args[i] == "--output")
                {
                    if (i + 1 < args.Length)
                    {
                        outputFile = args[i + 1].ToLower();
                    }
                }
                else if (args[i] == "--param" && i + 1 < args.Length)
                {
                    if (args[i + 1].ToLower() == "filter=false")
                    {
                        filter = false;
                    }
                    if (args[i + 1].ToLower() == "seg=true")
                    {
                        alreadySegmented = true;
                    }
                    else if (args[i + 1].ToLower() == "kif=true")
                    {
                        keepIntermediaryFiles = true;
                    }
                    else if (args[i + 1].ToLower().StartsWith("t="))
                    {
                        threshold = double.Parse(args[i + 1].Substring(args[i + 1].IndexOf("=") + 1), CultureInfo.InvariantCulture);
                        thresholdMod = true;
                    }
                    else if (args[i + 1].ToLower().StartsWith("maxrep="))
                    {
                        maxRepeated = int.Parse(args[i + 1].Substring(args[i + 1].IndexOf("=") + 1), CultureInfo.InvariantCulture);
                    }
                }
                else if (args[i] == "--test" && i + 4 < args.Length)
                {
                    testFile = args[i + 1];
                    gsSrc = args[i + 2];
                    gsTrg = args[i + 3];
                    lowMemory = int.Parse(args[i + 4]);
                }
                else if (args[i] == "--test1" && i + 3 < args.Length)
                {
                    testFileUnique = args[i + 1];
                    gsSrc = args[i + 2];
                    gsTrg = args[i + 3];
                }
                else if (args[i] == "--octave")
                {
                    if (i + 1 < args.Length)
                    {
                        prFile = args[i + 1];
                    }
                }
            }

            if (testFile != null)
            {
                //Testing module
                StreamWriter wrt = new StreamWriter("prValues_" + srcLang + "_" + trgLang + ".txt", false, Encoding.UTF8);
                wrt.AutoFlush = true;

                if (lowMemory == 1)
                {
                    while (threshold <= 1)
                    {
                        Console.WriteLine(threshold);
                        getOverThresholdTest(testFile, "___.txt", threshold);
                        double recall = 0.0;
                        double precision = 0.0;
                        wrt.WriteLine("t:{0}\tf1:{1:#.##}\tp:{2:#.##}\tr:{3:#.##}", threshold, 100.0 * f1("___.txt", ref recall, ref precision, gsSrc, gsTrg), 100.0 * precision, 100.0 * recall);
                        threshold = Math.Round(threshold + 0.01, 2);
                    }
                }
                else
                {
                    Dictionary<string, double> results = readResults(testFile);
                    HashSet<string> gs = loadGsPlain(gsSrc, gsTrg);
                    HashSet<string> goodFound = getGood(ref results, gs);

                    string[] ks = results.Keys.ToArray();
                    double[] vs = results.Values.ToArray();
                    Array.Sort(vs, ks);

                    double lowestValue = vs[0];
                    Console.WriteLine(lowestValue);

                    double precision = -1;
                    double recall = -1;
                    double f_1 = -1;

                    while (threshold <= 1)
                    {
                        Console.WriteLine(threshold);
                        double good = 0;
                        double j = 0;

                        if (precision == -1 || threshold >= lowestValue)
                        {
                            for (int i = vs.Length - 1; i >= 0; i--)
                            {
                                if (vs[i] < threshold)
                                    break;

                                j++;
                                if (goodFound.Contains(ks[i]))
                                {
                                    good++;
                                }
                            }

                            precision = good / j;
                            recall = good / gs.Count;
                            f_1 = (2.0 * precision * recall) / (precision + recall);
                        }
                        wrt.WriteLine("t:{0}\tf1:{1:#.##}\tp:{2:#.##}\tr:{3:#.##}", threshold, 100.0 * f_1, 100.0 * precision, 100.0 * recall);
                        threshold = Math.Round(threshold + 0.01, 2);
                    }
                }

                wrt.Close();
            }
            else if (testFileUnique != null)
            {
                StreamWriter wrt = new StreamWriter("recall_" + srcLang + "_" + trgLang + ".txt", false, Encoding.UTF8);
                wrt.AutoFlush = true;

                double recall = 0.0;
                double precision = 0.0;
                wrt.WriteLine("f1:{0:#.##}\tp:{1:#.##}\tr:{2:#.##}", 100.0 * f1(testFileUnique, ref recall, ref precision, gsSrc, gsTrg), 100.0 * precision, 100.0 * recall);
                threshold = Math.Round(threshold + 0.01, 2);

                wrt.Close();
            }
            else if (prFile != null)
            {
                generateOData(prFile);
            }
            else if ((inputFile != null) ^ (inputParams.Count == 2))
            {
                if (!thresholdMod && srcLang == "en")
                {
                    threshold = 0.2;
                }

                if (inputFile != null)
                {
                    extractData(resourcesDir, dictionaryDir, srcLang, trgLang, inputFile, outputFile, filter, keepIntermediaryFiles, alreadySegmented, threshold, maxRepeated);
                }
                else
                {
                    extractData(resourcesDir, dictionaryDir, srcLang, trgLang, inputParams[0], inputParams[1], outputFile, filter, keepIntermediaryFiles, alreadySegmented, threshold, maxRepeated);
                }
            }
            else
            {
                if (File.Exists("lexacc.README"))
                {
                    Console.WriteLine(DataStructReader.readWholeTextFile("lexacc.README", Encoding.UTF8));
                }
                else
                {
                    Console.WriteLine("Running instructions are written in lexacc.README file");
                }

                //Console.WriteLine("Usage: lexacc.exe [--res <resourcesDirectory>] [--dir <dictionaryDicrectory>] [--input <inSrcFile> --input <inTrgFile>] [--docalign <inFILE>] [--ouput <outFILE>] [--source <srcLANG>] [--target <trgLANG>] [--param kif(keep intermediary files)=TRUE/FALSE] [--param t(thershold)=<double value>]");
                //Console.WriteLine();
                //Console.WriteLine("Default Resources Directory: ." + Path.DirectorySeparatorChar.ToString() + "res");
                //Console.WriteLine("Default Dictionary Directory: ." + Path.DirectorySeparatorChar.ToString() + "dict");
                //Console.WriteLine("Default Output File: results.txt");
                //Console.WriteLine("Default Output File: results.txt");
                //Console.WriteLine("Default Source Language: en");
                //Console.WriteLine("Default Target Language: ro");
                //Console.WriteLine("Default Keep Intermediary Files (kif): false");
                //Console.WriteLine("Default Threshold (t): 0.2");
            }
        }

        private static HashSet<string> getGood(ref Dictionary<string, double> data, HashSet<string> gs)
        {
            HashSet<string> ret = new HashSet<string>();

            foreach (string line in data.Keys)
            {
                string[] parts = line.Split('\t');

                string val1 = onlyAlphaNum(parts[0]);
                string val2 = onlyAlphaNum(parts[1]);

                string val = val1 + "\t" + val2;

                if (gs.Contains(val))
                {
                    ret.Add(line);
                }
            }

            return ret;
        }

        private static void extractData(string resourcesDir, string dictionaryDir, string srcLang, string trgLang, string inputFile, string outputFile, bool filter, bool keepIntermediaryFiles, bool alreadySegmented, double threshold, int maxRepeated)
        {
            DateTime dt = DateTime.Now;

            //step 0: loading resources
            Console.WriteLine("Loading resources...", trgLang);

            resourcesDir = resourcesDir.Replace("\\", Path.DirectorySeparatorChar.ToString()).Replace("/", Path.DirectorySeparatorChar.ToString());
            dictionaryDir = dictionaryDir.Replace("\\", Path.DirectorySeparatorChar.ToString()).Replace("/", Path.DirectorySeparatorChar.ToString());

            if (resourcesDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                resourcesDir = resourcesDir.Substring(0, resourcesDir.Length - 1);
            }
            if (dictionaryDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                dictionaryDir = dictionaryDir.Substring(0, dictionaryDir.Length - 1);
            }

            m = new Measure(srcLang, trgLang, resourcesDir, dictionaryDir);
            te = readTEs(dictionaryDir + Path.DirectorySeparatorChar.ToString() + srcLang + "_" + trgLang);
            stopwords = DataStructReader.readHashSet(resourcesDir + Path.DirectorySeparatorChar.ToString() + "stopwords_" + srcLang + ".txt", Encoding.UTF8, 0, '\t', false, null);

            HashSet<string> fileMap = DataStructReader.readHashSet(inputFile, Encoding.UTF8, 0, '*', false, null);
            Dictionary<string, string> fileIndex = getFileIndex(fileMap);

            //step1: index
            string indexFolder = "_index_" + trgLang;
            Console.WriteLine("Indexing {0} text...", trgLang);
            if (!Directory.Exists(indexFolder))
            {
                Directory.CreateDirectory(indexFolder);
            }
            index(fileIndex, indexFolder, alreadySegmented);

            //step2: find tes in index
            Console.WriteLine("Search in index...");
            double thresholdScore = 0.1;
            double thresholdCount = 50;

            string seResultsFile = "_se_" + srcLang + "_" + trgLang + ".txt";
            getCorrespondences(fileMap.ToArray(), fileIndex, alreadySegmented, indexFolder, thresholdScore, thresholdCount, 100, seResultsFile);

            string filter1File = "_f1_" + srcLang + "_" + trgLang + ".txt";
            string filter2File = "_f2_" + srcLang + "_" + trgLang + ".txt";
            string pexaccSimilarityFunctionFile = "_PSF_" + srcLang + "_" + trgLang + ".txt";

            if (filter)
            {
                //step3: 1st filtering
                Console.WriteLine("1st filtering...");
                recompute(seResultsFile, filter1File, 0, false, 1, filter, true);

                //ste4: 2nd filtering above average
                Console.WriteLine("\n2nd filtering...");
                List<double> values = readValues(filter1File);
                double mean = SFunctions._mean(values);
                getOverThreshold(filter1File, filter2File, mean, false, 0, 0, null);

                //ste5:pexacc filtering 0
                Console.WriteLine("Find similarities...");
                recompute(filter2File, pexaccSimilarityFunctionFile, 0, false, 2, filter, true);
            }
            else
            {
                //ste3:pexacc filtering 0
                Console.WriteLine("Find similarities...");
                recompute(seResultsFile, pexaccSimilarityFunctionFile, 0, false, 2, filter, true);
            }

            string outFileDebug = "_completeDebug_" + srcLang + "_" + trgLang + ".txt";
            getOverThreshold(pexaccSimilarityFunctionFile, outFileDebug, threshold, true, maxRepeated, maxRepeated, fileMap.ToArray());

            prepareOutput(outFileDebug, outputFile);

            TimeSpan ts = DateTime.Now - dt;
            Console.WriteLine("done... {0}", ts.TotalMinutes);
            //Console.ReadLine();

            if (!keepIntermediaryFiles)
            {
                string[] files = Directory.GetFiles(indexFolder);
                foreach (string file in files)
                {
                    File.Delete(file);
                }
                Directory.Delete(indexFolder);

                File.Delete(seResultsFile);
                if (filter)
                {
                    File.Delete(filter1File);
                    File.Delete(filter2File);
                }
                File.Delete(pexaccSimilarityFunctionFile);
                File.Delete(outFileDebug);
            }
        }

        private static Dictionary<string, string> getFileIndex(HashSet<string> fileMap)
        {
            Dictionary<string, string> ret = new Dictionary<string, string>();

            int count = 0;
            foreach (string entry in fileMap)
            {
                string[] parts = entry.Split('\t');
                if (!ret.ContainsKey(parts[1]))
                {
                    ret.Add(parts[1], (count++).ToString());
                }
            }

            return ret;
        }

        private static void extractData(string resourcesDir, string dictionaryDir, string srcLang, string trgLang, string fileSrc, string fileTrg, string outputFile, bool filter, bool keepIntermediaryFiles, bool alreadySegmented, double threshold, int maxRepeated)
        {
            DateTime dt = DateTime.Now;

            //step 0: loading resources
            Console.WriteLine("Loading resources...", trgLang);

            resourcesDir = resourcesDir.Replace("\\", Path.DirectorySeparatorChar.ToString()).Replace("/", Path.DirectorySeparatorChar.ToString());
            dictionaryDir = dictionaryDir.Replace("\\", Path.DirectorySeparatorChar.ToString()).Replace("/", Path.DirectorySeparatorChar.ToString());

            if (resourcesDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                resourcesDir = resourcesDir.Substring(0, resourcesDir.Length - 1);
            }
            if (dictionaryDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                dictionaryDir = dictionaryDir.Substring(0, dictionaryDir.Length - 1);
            }

            m = new Measure(srcLang, trgLang, resourcesDir, dictionaryDir);
            te = readTEs(dictionaryDir + Path.DirectorySeparatorChar.ToString() + srcLang + "_" + trgLang);
            stopwords = DataStructReader.readHashSet(resourcesDir + Path.DirectorySeparatorChar.ToString() + "stopwords_" + srcLang + ".txt", Encoding.UTF8, 0, '\t', false, null);

            //step1: index
            string indexFolder = "_index_" + trgLang;
            Console.WriteLine("Indexing {0} text...", trgLang);
            if (!Directory.Exists(indexFolder))
            {
                Directory.CreateDirectory(indexFolder);
            }
            index(fileTrg, indexFolder, alreadySegmented);

            //step2: find tes in index
            Console.WriteLine("Search in index...");
            double thresholdScore = 0.1;
            double thresholdCount = 50;

            string seResultsFile = "_se_" + srcLang + "_" + trgLang + ".txt";
            getCorrespondences(fileSrc, alreadySegmented, indexFolder, thresholdScore, thresholdCount, 100, seResultsFile);

            string filter1File = "_f1_" + srcLang + "_" + trgLang + ".txt";
            string filter2File = "_f2_" + srcLang + "_" + trgLang + ".txt";
            string pexaccSimilarityFunctionFile = "_PSF_" + srcLang + "_" + trgLang + ".txt";

            if (filter)
            {
                //step3: 1st filtering
                Console.WriteLine("1st filtering...");
                recompute(seResultsFile, filter1File, 0, false, 1, filter, false);

                //step4: 2nd filtering above average
                Console.WriteLine("\n2nd filtering...");
                List<double> values = readValues(filter1File);
                double mean = SFunctions._mean(values);
                getOverThreshold(filter1File, filter2File, mean, false, 0, 0, null);

                //step5:pexacc filtering 0
                Console.WriteLine("Find similarities...");
                recompute(filter2File, pexaccSimilarityFunctionFile, 0, false, 2, filter, false);
            }
            else
            {
                //step5:pexacc filtering 0
                Console.WriteLine("Find similarities...");
                recompute(seResultsFile, pexaccSimilarityFunctionFile, 0, false, 2, filter, false);
            }

            string outFileDebug = "_completeDebug_" + srcLang + "_" + trgLang + ".txt";
            getOverThreshold(pexaccSimilarityFunctionFile, outFileDebug, threshold, true, maxRepeated, maxRepeated, null);

            prepareOutput(outFileDebug, outputFile);

            TimeSpan ts = DateTime.Now - dt;
            Console.WriteLine("done... {0}", ts.TotalMinutes);
            //Console.ReadLine();

            if (!keepIntermediaryFiles)
            {
                string[] files = Directory.GetFiles(indexFolder);
                foreach (string file in files)
                {
                    File.Delete(file);
                }
                Directory.Delete(indexFolder);


                File.Delete(seResultsFile);
                if (filter)
                {
                    File.Delete(filter1File);
                    File.Delete(filter2File);
                }
                File.Delete(pexaccSimilarityFunctionFile);
                File.Delete(outFileDebug);
            }
        }

        private static void prepareOutput(string inputFile, string outputFile)
        {
            StreamWriter wrt = new StreamWriter(outputFile, false, Encoding.UTF8);
            wrt.AutoFlush = true;
            StreamReader rdr = new StreamReader(inputFile, Encoding.UTF8);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                string[] parts = line.Split('\t');
                wrt.WriteLine(parts[0]);
                wrt.WriteLine(parts[1]);
                wrt.WriteLine(parts[2]);
                wrt.WriteLine();
            }
            rdr.Close();
            wrt.Close();
        }

        private static List<double> readValues(string fileIn)
        {
            List<double> ret = new List<double>();

            StreamReader rdr = new StreamReader(fileIn, Encoding.UTF8);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                string[] parts = line.Split('\t');
                ret.Add(double.Parse(parts[2], CultureInfo.InvariantCulture));
            }
            rdr.Close();

            return ret;
        }

        private static void getOverThresholdTest(string fileIn, string fileOut, double threshold)
        {
            string line;
            StreamWriter wrt = new StreamWriter(fileOut, false, Encoding.UTF8);
            wrt.AutoFlush = true;

            int i = 0;
            StringBuilder sb = new StringBuilder();
            StreamReader rdr = new StreamReader(fileIn, Encoding.UTF8);
            while ((line = rdr.ReadLine()) != null)
            {
                i++;
                if (i % 4 == 3)
                {
                    sb.Append(line);
                    double score = double.Parse(line, CultureInfo.InvariantCulture);

                    if (score >= threshold)
                    {
                        wrt.WriteLine(sb.ToString());
                    }
                    sb = new StringBuilder();
                }
                else if (i % 4 != 0)
                {
                    sb.Append(line + "\t");
                }
            }
            rdr.Close();
            wrt.Close();
        }

        private static Dictionary<string, double> readResults(string fileIn)
        {
            Dictionary<string, double> ret = new Dictionary<string, double>();
            string line;
            int i = 0;
            StringBuilder sb = new StringBuilder();
            StreamReader rdr = new StreamReader(fileIn, Encoding.UTF8);
            while ((line = rdr.ReadLine()) != null)
            {
                i++;
                i = i % 4;
                switch (i)
                {
                    case 1:
                        sb = new StringBuilder(line);
                        break;
                    case 2:
                        sb.Append("\t" + line);
                        break;
                    case 3:
                        double score = double.Parse(line, CultureInfo.InvariantCulture);
                        string key = sb.ToString();
                        if (!ret.ContainsKey(key))
                        {
                            ret.Add(key, score);
                        }
                        break;
                    default:
                        break;
                }
            }
            rdr.Close();
            return ret;
        }

        private static void getOverThreshold(string fileIn, string fileOut, double threshold, bool sort, int maxRepeatedSource, int maxRepeatedTarget, string[] fileMap)
        {
            string line;
            StreamWriter wrt = new StreamWriter(fileOut, false, Encoding.UTF8);
            wrt.AutoFlush = true;
            Dictionary<string, double> sortedDict = new Dictionary<string, double>();
            Dictionary<string, string> pairToLine = new Dictionary<string, string>();

            string[] keys = null;
            string[] vals = null;

            StreamReader rdr = new StreamReader(fileIn, Encoding.UTF8);
            while ((line = rdr.ReadLine()) != null)
            {
                string[] parts = line.Split('\t');
                double score = double.Parse(parts[2], CultureInfo.InvariantCulture);

                string filePair = "";
                if (fileMap != null)
                {
                    int idx = int.Parse(parts[parts.Length - 1]);
                    filePair = "\t" + fileMap[idx];
                }

                if (score >= threshold)
                {
                    if (!sort)
                    {
                        wrt.WriteLine(line + filePair);
                    }
                    else
                    {
                        string pair = parts[0] + "\t" + parts[1];
                        if (!sortedDict.ContainsKey(pair))
                        {
                            sortedDict.Add(pair, score);
                            pairToLine.Add(pair, line + filePair);
                        }
                    }
                }
            }
            rdr.Close();

            if (sort)
            {
                string[] ks = sortedDict.Keys.ToArray();
                double[] vs = sortedDict.Values.ToArray();
                Array.Sort(vs, ks);

                Dictionary<string, int> howMany = new Dictionary<string, int>();

                for (int i = ks.Length - 1; i >= 0; i--)
                {
                    string[] parts = ks[i].Split('\t');

                    if ((!howMany.ContainsKey(parts[0]) || howMany[parts[0]] < maxRepeatedSource) && (!howMany.ContainsKey(parts[1]) || howMany[parts[1]] < maxRepeatedTarget))
                    {
                        //wrt.WriteLine("{0}\t{1}", ks[i], vs[i]);
                        wrt.WriteLine(pairToLine[ks[i]]);
                        if (!howMany.ContainsKey(parts[0]))
                        {
                            howMany.Add(parts[0], 0);
                        }
                        if (!howMany.ContainsKey(parts[1]))
                        {
                            howMany.Add(parts[1], 0);
                        }
                        howMany[parts[0]]++;
                        howMany[parts[1]]++;
                    }
                }
            }

            wrt.Close();
        }

        private static void getFirst(string folderIn, string folderOut, int howMany)
        {
            string[] files = Directory.GetFiles(folderIn);
            foreach (string file in files)
            {
                string line = "";

                string oFile = folderOut + Path.DirectorySeparatorChar.ToString() + Path.GetFileName(file);
                StreamWriter wrt = new StreamWriter(oFile, false, Encoding.UTF8);
                wrt.AutoFlush = true;
                StreamReader rdr = new StreamReader(file, Encoding.UTF8);

                string last = "";
                int cnt = 0;

                while ((line = rdr.ReadLine()) != null)
                {
                    string[] parts = line.Split(' ');
                    if (parts[0] != last)
                    {
                        last = parts[0];
                        cnt = 1;
                        wrt.WriteLine(line);
                    }
                    else if (cnt < howMany)
                    {
                        cnt++;
                        wrt.WriteLine(line);
                    }
                }
                rdr.Close();
                wrt.Close();

                Console.WriteLine(file);
            }
        }

        private static void analyze(string file, Dictionary<string, string> gs)
        {
            List<double> goodValues = new List<double>();
            List<double> badValues = new List<double>();
            int good = 0;
            StreamReader rdr = new StreamReader(file, Encoding.UTF8);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                string[] parts = line.Trim().Split(new char[] { '\t' });
                double score = double.Parse(parts[2], CultureInfo.InvariantCulture);
                if (gs.ContainsKey(parts[0]) && (gs[parts[0]] == parts[1]))
                {
                    good++;
                    goodValues.Add(score);
                }
                else
                {
                    badValues.Add(score);
                }
            }
            Console.WriteLine("{0} #G:{1} avgG:{2:#.####} stdevG:{3:#.####} avgB{4:#.####} stdevB{5:#.####} minG{6:#.####} maxB:{7:#.####}", new object[] { Path.GetFileNameWithoutExtension(file), good, SFunctions._mean(goodValues), SFunctions._stdev(goodValues), SFunctions._mean(badValues), SFunctions._stdev(badValues), SFunctions._minimum(goodValues), SFunctions._maximum(badValues) });
            rdr.Close();
        }

        private static double f1(string fileIn, ref double recall, ref double precision, string gsL1, string gsL2)
        {
            HashSet<string> gs = loadGsPlain(gsL1, gsL2);
            StreamReader rdr = new StreamReader(fileIn, Encoding.UTF8);
            string line = "";
            int i = 0;
            double good = 0.0;
            recall = 0.0;
            precision = 0.0;

            HashSet<string> done = new HashSet<string>();

            while ((line = rdr.ReadLine()) != null)
            {
                i++;
                string[] parts = line.Split('\t');

                string val1 = onlyAlphaNum(parts[0]);
                string val2 = onlyAlphaNum(parts[1]);

                string val = val1 + "\t" + val2;

                if (gs.Contains(val) && !done.Contains(val))
                {
                    good++;
                    done.Add(val);
                }
            }

            rdr.Close();
            recall = good / ((double)gs.Count);
            precision = good / ((double)i);
            return (((2.0 * precision) * recall) / (precision + recall));
        }

        private static double alignmentScoreSigmoid(List<double> src, List<double> trg, double maxPossibilities)
        {
            return SFunctions._correlation_coefficient(src, trg) * (1 / (1 + Math.Exp(-((double)(src.Count) / (maxPossibilities / 2.0) - 1) * 5)));
        }

        private static double gapScore(List<int> index)
        {
            int i;
            index.Sort();
            List<int> good = new List<int>();
            for (i = 0; i < index.Count; i++)
            {
                if (index[i] != -1)
                {
                    good.Add(index[i]);
                }
            }
            List<double> distances = new List<double>();
            for (i = 1; i < good.Count; i++)
            {
                distances.Add((double)Math.Abs((int)(good[i] - good[i - 1])));
            }
            double average = 0.0;
            if (distances.Count > 0)
            {
                average = SFunctions._mean(distances);

                //for (i = 0; i < distances.Count; i++)
                //{
                //    average += distances[i];
                //}
                //average /= (double)distances.Count;
            }
            if (average > 0.0)
            {
                return (1.0 / average);
            }
            return 0.0;
        }

        private static void generateOData(string fileName)
        {
            StreamWriter wrtF = new StreamWriter("f.txt", false, Encoding.ASCII);
            StreamWriter wrtP = new StreamWriter("p.txt", false, Encoding.ASCII);
            StreamWriter wrtR = new StreamWriter("r.txt", false, Encoding.ASCII);
            StreamReader rdr = new StreamReader(fileName, Encoding.UTF8);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                string[] parts = line.Split(new char[] { '\t' });
                double val = double.Parse(parts[0].Substring(parts[0].IndexOf(":") + 1), CultureInfo.InvariantCulture);
                double f1 = double.Parse(parts[1].Substring(parts[1].IndexOf(":") + 1), CultureInfo.InvariantCulture);
                double p = double.Parse(parts[2].Substring(parts[2].IndexOf(":") + 1), CultureInfo.InvariantCulture);
                double r = double.Parse(parts[3].Substring(parts[3].IndexOf(":") + 1), CultureInfo.InvariantCulture);
                wrtF.WriteLine("{0},{1}", Math.Round(val, 4), f1);
                wrtP.WriteLine("{0},{1}", Math.Round(val, 4), p);
                wrtR.WriteLine("{0},{1}", Math.Round(val, 4), r);
            }
            rdr.Close();
            wrtF.Close();
            wrtP.Close();
            wrtR.Close();
        }

        private static void getCorrespondences(string[] fileMap, Dictionary<string, string> fileIndex, bool alreadySegmented, string index, double thresholdScore, double thresholdCount, int howMany, string fileOut)
        {
            StreamWriter wrt = new StreamWriter(fileOut, false, Encoding.UTF8);
            wrt.AutoFlush = true;

            //Dictionary<string, Dictionary<string, double>> ret = new Dictionary<string, Dictionary<string, double>>();
            IndexSearcher searcher = new IndexSearcher(index);
            //HashSet<string> files = DataStructReader.readHashSet(inputFile, Encoding.UTF8, 0, '\t', false, null);

            HashSet<string> uniqueFiles = getSourceFiles(fileMap);

            List<double> sentencesLength = getSentencesLength(uniqueFiles.ToArray());
            double mean = Statistics.SFunctions._mean(sentencesLength);
            double stdev = Statistics.SFunctions._stdev(sentencesLength);
            double largeThreshold = mean - stdev;
            double smallThreshold = mean + stdev;

            sentencesLength.Clear();
            uniqueFiles.Clear();

            string line = "";
            //double cnt = 0.0;

            for (int i = 0; i < fileMap.Length; i++)
            {
                HashSet<string> done = new HashSet<string>();

                string entry = fileMap[i];
                string[] parts = entry.Split('\t');
                string fileSrc = parts[0];
                string fileTrg = parts[1];

                HashSet<string> sentences = null;
                if (!alreadySegmented)
                {
                    string wholeText = DataStructReader.readWholeTextFile(fileSrc, Encoding.UTF8);
                    sentences = getSentences(wholeText);
                }
                else
                {
                    sentences = new HashSet<string>();
                    StreamReader rdr = new StreamReader(fileSrc, Encoding.UTF8);
                    while ((line = rdr.ReadLine()) != null)
                    {
                        sentences.Add(line.Trim());
                    }
                    rdr.Close();
                }

                foreach (string sentence in sentences)
                {
                    line = sentence.Trim().Replace("\t", " ");

                    if (!done.Contains(line))
                    {
                        done.Add(line);
                        List<string> tokensLine = getTokensList(line);

                        string largeBool = (tokensLine.Count >= largeThreshold) ? "1" : "0";
                        string smallBool = (tokensLine.Count <= smallThreshold) ? "1" : "0";

                        Dictionary<string, double> hits = getHits(searcher, new HashSet<string>(tokensLine), largeBool, smallBool, thresholdScore, thresholdCount, howMany, fileIndex[fileTrg]);
                        foreach (string key in hits.Keys)
                        {
                            if (!TextProcessing.identicalStrings(line, key))
                            {
                                wrt.WriteLine("{0}\t{1}\t{2}\t{3}", line, key, hits[key], i);
                            }
                        }
                    }
                }
            }

            wrt.Close();

            searcher.Close();
        }

        private static HashSet<string> getSourceFiles(string[] fileMap)
        {
            HashSet<string> ret = new HashSet<string>();
            foreach (string entry in fileMap)
            {
                string[] parts = entry.Split('\t');
                ret.Add(parts[1]);
            }
            return ret;
        }

        private static void getCorrespondences(string inputFile, bool alreadySegmented, string index, double thresholdScore, double thresholdCount, int howMany, string fileOut)
        {
            StreamWriter wrt = new StreamWriter(fileOut, false, Encoding.UTF8);
            wrt.AutoFlush = true;

            //Dictionary<string, Dictionary<string, double>> ret = new Dictionary<string, Dictionary<string, double>>();
            IndexSearcher searcher = new IndexSearcher(index);
            HashSet<string> files = DataStructReader.readHashSet(inputFile, Encoding.UTF8, 0, '\t', false, null);

            List<double> sentencesLength = getSentencesLength(files);
            double mean = Statistics.SFunctions._mean(sentencesLength);
            double stdev = Statistics.SFunctions._stdev(sentencesLength);
            double largeThreshold = mean - stdev;
            double smallThreshold = mean + stdev;

            string line = "";
            //double cnt = 0.0;

            HashSet<string> done = new HashSet<string>();
            foreach (string file in files)
            {
                //cnt++;
                //Console.Write("\r{0:#.##}%    ", (100.0 * cnt) / ((double)files.Length));

                HashSet<string> sentences = null;
                if (!alreadySegmented)
                {
                    string wholeText = DataStructReader.readWholeTextFile(file, Encoding.UTF8);
                    sentences = getSentences(wholeText);
                }
                else
                {
                    sentences = new HashSet<string>();
                    StreamReader rdr = new StreamReader(file, Encoding.UTF8);
                    while ((line = rdr.ReadLine()) != null)
                    {
                        sentences.Add(line.Trim());
                    }
                    rdr.Close();
                }

                foreach (string sentence in sentences)
                {
                    line = sentence.Trim().Replace("\t", " ");

                    if (!done.Contains(line))
                    {
                        done.Add(line);
                        List<string> tokensLine = getTokensList(line);

                        string largeBool = (tokensLine.Count >= largeThreshold) ? "1" : "0";
                        string smallBool = (tokensLine.Count <= smallThreshold) ? "1" : "0";

                        Dictionary<string, double> hits = getHits(searcher, new HashSet<string>(tokensLine), largeBool, smallBool, thresholdScore, thresholdCount, howMany, null);
                        foreach (string key in hits.Keys)
                        {
                            wrt.WriteLine("{0}\t{1}\t{2}", line, key, hits[key]);
                        }
                    }
                }
            }

            wrt.Close();

            searcher.Close();
        }

        private static Dictionary<string, double> getHits(IndexSearcher searcher, HashSet<string> tokens, string largeSentence, string smallSentence, double thresholdScore, double thresholdCount, int howMany, string docConstraint)
        {
            Dictionary<string, double> ret = new Dictionary<string, double>();
            BooleanQuery bqTotal = new BooleanQuery();
            bool okTotal = false;

            foreach (string token in tokens)
            {
                if (!stopwords.Contains(token.ToLower()) /*&& te.ContainsKey(token)*/)
                {
                    string[] ks = new string[1];
                    double[] vs = new double[1];
                    ks[0] = token;
                    vs[0] = 1;

                    BooleanQuery.SetMaxClauseCount(int.MaxValue);
                    BooleanQuery bq = new BooleanQuery();

                    if (te.ContainsKey(token))
                    {
                        //string orgStem = PexaccSim.TextProcessing.lemmatizeWord(token, m.simResourcesST.trgInflections);
                        //bq.Add(new TermQuery(new Term("occurences", orgStem)), BooleanClause.Occur.SHOULD);

                        Dictionary<string, double> candidates = new Dictionary<string, double>();

                        foreach (string equivalent in te[token].Keys)
                        {
                            if (te[token][equivalent] > thresholdScore)
                            {
                                candidates.Add(equivalent, te[token][equivalent]);
                            }
                        }

                        if (!candidates.ContainsKey(token))
                        {
                            candidates.Add(token, 1);
                        }

                        ks = candidates.Keys.ToArray();
                        vs = candidates.Values.ToArray();
                        Array.Sort(vs, ks);
                    }

                    int j = 0;
                    HashSet<string> done = new HashSet<string>() { /*orgStem*/ };

                    for (int i = ks.Length - 1; i >= 0; i--)
                    {
                        Dictionary<string, int> stems = new Dictionary<string, int>();
                        stems.Add(m.simResourcesST.lemmatizeTrgWordUnique(ks[i]), 1);

                        bool added = false;

                        foreach (string stem in stems.Keys)
                        {
                            if (!done.Contains(stem))
                            {
                                if (!added)
                                {
                                    j++;
                                    added = true;
                                }
                                done.Add(stem);
                                Term t = new Term("occurences", stem);
                                bq.Add(new TermQuery(t), BooleanClause.Occur.SHOULD);
                            }
                        }

                        if (j >= thresholdCount)
                        {
                            break;
                        }
                    }

                    okTotal = true;
                    bqTotal.Add(bq, BooleanClause.Occur.SHOULD);
                }
            }

            if (okTotal)
            {
                TermQuery tqL = new TermQuery(new Term("large", largeSentence));
                TermQuery tqS = new TermQuery(new Term("small", smallSentence));
                tqL.SetBoost(2);
                tqS.SetBoost(2);

                bqTotal.Add(tqL, BooleanClause.Occur.SHOULD);
                bqTotal.Add(tqS, BooleanClause.Occur.SHOULD);

                if (docConstraint != null)
                {
                    bqTotal.Add(new TermQuery(new Term("doc", docConstraint)), BooleanClause.Occur.MUST);
                }

                Hits hits = searcher.Search(bqTotal);
                int i = 0;
                while (i < hits.Length() && i < howMany)
                {
                    string sentence = hits.Doc(i).Get("sentence");
                    double score = hits.Score(i);
                    if (!ret.ContainsKey(sentence))
                    {
                        ret.Add(sentence, score);
                    }
                    i++;
                }
            }
            return ret;
        }

        private static double getLexicalSimilarity(List<string> s1, List<string> s2, bool useLD)
        {
            double tscore = 0.0;
            List<int> index = new List<int>();
            double total = 0.0;
            //double total = 1.0;

            List<double> srcIdx = new List<double>();
            List<double> trgIdx = new List<double>();

            for (int i = 0; i < s1.Count; i++)
            {
                index.Add(-1);
                double maxScore = 0.0;
                if (!stopwords.Contains(s1[i]))
                {
                    for (int j = 0; j < s2.Count; j++)
                    {
                        double teScore = 0.0;
                        double ldScore = 0.0;

                        if (useLD)
                        {
                            ldScore = computeLevenshteinDistance(s1[i], s2[j]);
                            ldScore = (1.0 - (ldScore / ((double)Math.Max(s1[i].Length, s2[j].Length)))) / 4.0;
                        }

                        Dictionary<string, double> candidates = new Dictionary<string, double>();

                        if (te.ContainsKey(s1[i]) && te[s1[i]].ContainsKey(s2[j]))
                        {
                            teScore = te[s1[i]][s2[j]];
                        }

                        double localScore = (teScore > ldScore) ? teScore : ldScore;

                        if (localScore > maxScore)
                        {
                            maxScore = localScore;
                            index[i] = j;
                        }
                    }

                    if (index[i] != -1)
                    {
                        srcIdx.Add(i);
                        trgIdx.Add(index[i]);
                    }

                    if (!(maxScore == 0.0))
                    {
                        tscore += maxScore;
                        total++;
                    }
                }
            }
            if (total > 2.0)
            {
                return ((total * ((2.0 * tscore) / ((double)(s1.Count + s2.Count)))) * Math.Pow(gapScore(index), 0.5));
                //return (2.0 * tscore) / ((double)(s1.Count + s2.Count)) * alignmentScoreSigmoid(srcIdx, trgIdx, (double)Math.Min(s1.Count, s2.Count));
            }
            return 0.0;
        }

        private static HashSet<string> getSentences(string text)
        {
            HashSet<string> ret = new HashSet<string>();
            Regex regex = new Regex(
                      "....+?(\\.|\\!|\\?|$)+",
                    RegexOptions.Singleline
                    );

            string[] lines = text.Split('\n');

            foreach (string l in lines)
            {
                string line = l.Trim();

                Match match = regex.Match(line);
                while (match.Success)
                {
                    string sentence = match.Value.Trim();
                    if (sentence.Split(' ').Length > 2)
                    {
                        ret.Add(sentence);
                    }
                    match = match.NextMatch();
                }
            }

            return ret;
        }

        private static HashSet<string> getSentencesComplexRegex(string text)
        {
            HashSet<string> ret = new HashSet<string>();

            string final = ".!?";
            text = text.Replace("\n", ". ");
            if (text != "")
            {
                if (!final.Contains(text.Substring(text.Length - 1)))
                {
                    text = text + ".";
                }
                while (text.Contains("  "))
                {
                    text = text.Replace("  ", " ");
                }
                while (text.Contains(" ."))
                {
                    text = text.Replace(" .", ".");
                }
                while (text.Contains(".."))
                {
                    text = text.Replace("..", ".");
                }
                text = text + "A";
            }
            Regex regexSentences = new Regex(
                ".+?(?<![\\s\\.]\\p{Lu})(?<![\\s\\.]\\p{Lu}[bcdfgjklmnprstvxz" +
                  "])(?<![\\s\\.]\\p{Lu}[bcdfgjklmnprstvxz][bcdfgjklmnprstvxz])" +
                  "[\\.?!]+(?=\\s*[\\p{Lu}\\[\\(\\\"\\'])",
                RegexOptions.None
                );

            Match sentenceMatch = regexSentences.Match(text);

            while (sentenceMatch.Success)
            {
                string value = sentenceMatch.Value.Trim().Replace("\r", "");
                while (value.Contains(".."))
                {
                    value = value.Replace("..", ".");
                }
                ret.Add(value);
                sentenceMatch = sentenceMatch.NextMatch();
            }

            return ret;
        }

        private static string getTokens(string line)
        {
            Regex regex = new Regex(@"[\w-]+", RegexOptions.None);
            StringBuilder sb = new StringBuilder();
            for (Match m = regex.Match(line); m.Success; m = m.NextMatch())
            {
                sb.Append(m.Value + " ");
            }
            return sb.ToString().Trim();
        }

        private static List<string> getTokensList(string line)
        {
            List<string> ret = new List<string>();
            Regex regex = new Regex(@"[\w-]+", RegexOptions.None);
            StringBuilder sb = new StringBuilder();
            for (Match m = regex.Match(line); m.Success; m = m.NextMatch())
            {
                ret.Add(m.Value);
            }
            return ret;
        }

        private static void index(Dictionary<string, string> fileIndex, string indexFolder, bool alreadySegmented)
        {
            IndexWriter writer = new IndexWriter(indexFolder, new WhitespaceAnalyzer(), true);
            string line = "";

            string[] files = fileIndex.Keys.ToArray();

            List<double> sentencesLength = getSentencesLength(files);

            double mean = Statistics.SFunctions._mean(sentencesLength);
            double stdev = Statistics.SFunctions._stdev(sentencesLength);

            double largeThreshold = mean - stdev;
            double smallThreshold = mean + stdev;

            for (int i = 0; i < files.Length; i++)
            {
                HashSet<string> sentences = null;
                string file = files[i];

                if (!alreadySegmented)
                {
                    string wholeText = DataStructReader.readWholeTextFile(file, Encoding.UTF8);
                    sentences = getSentences(wholeText);
                }
                else
                {
                    sentences = new HashSet<string>();
                    StreamReader rdr = new StreamReader(file, Encoding.UTF8);
                    while ((line = rdr.ReadLine()) != null)
                    {
                        sentences.Add(line.Trim());
                    }
                    rdr.Close();
                }

                foreach (string sentence in sentences)
                {
                    line = sentence.Trim().Replace("\t", " ");
                    List<string> tokensLine = getTokensList(line);
                    List<string> lemmasLine = pexaccNorm(tokensLine);

                    string largeBool = (lemmasLine.Count >= largeThreshold) ? "1" : "0";
                    string smallBool = (lemmasLine.Count <= smallThreshold) ? "1" : "0";

                    Document doc = new Document();
                    doc.Add(new Field("occurences", string.Join(" ", lemmasLine), Field.Store.YES, Field.Index.ANALYZED));
                    doc.Add(new Field("large", largeBool, Field.Store.YES, Field.Index.ANALYZED));
                    doc.Add(new Field("small", smallBool, Field.Store.YES, Field.Index.ANALYZED));
                    doc.Add(new Field("doc", fileIndex[file], Field.Store.YES, Field.Index.ANALYZED));

                    doc.Add(new Field("sentence", line, Field.Store.YES, Field.Index.NO));
                    writer.AddDocument(doc);
                }
            }
            writer.Close();
        }

        private static void index(string inputFile, string indexFolder, bool alreadySegmented)
        {
            IndexWriter writer = new IndexWriter(indexFolder, new WhitespaceAnalyzer(), true);
            HashSet<string> files = DataStructReader.readHashSet(inputFile, Encoding.UTF8, 0, '\t', false, null);
            string line = "";

            List<double> sentencesLength = getSentencesLength(files);

            double mean = Statistics.SFunctions._mean(sentencesLength);
            double stdev = Statistics.SFunctions._stdev(sentencesLength);

            double largeThreshold = mean - stdev;
            double smallThreshold = mean + stdev;

            foreach (string file in files)
            {
                HashSet<string> sentences = null;

                if (!alreadySegmented)
                {
                    string wholeText = DataStructReader.readWholeTextFile(file, Encoding.UTF8);
                    sentences = getSentences(wholeText);
                }
                else
                {
                    sentences = new HashSet<string>();
                    StreamReader rdr = new StreamReader(file, Encoding.UTF8);
                    while ((line = rdr.ReadLine()) != null)
                    {
                        sentences.Add(line.Trim());
                    }
                    rdr.Close();
                }

                foreach (string sentence in sentences)
                {
                    line = sentence.Trim().Replace("\t", " ");
                    List<string> tokensLine = getTokensList(line);
                    List<string> lemmasLine = pexaccNorm(tokensLine);

                    string largeBool = (lemmasLine.Count >= largeThreshold) ? "1" : "0";
                    string smallBool = (lemmasLine.Count <= smallThreshold) ? "1" : "0";

                    Document doc = new Document();
                    doc.Add(new Field("occurences", string.Join(" ", lemmasLine), Field.Store.YES, Field.Index.ANALYZED));
                    doc.Add(new Field("large", largeBool, Field.Store.YES, Field.Index.ANALYZED));
                    doc.Add(new Field("small", smallBool, Field.Store.YES, Field.Index.ANALYZED));
                    doc.Add(new Field("sentence", line, Field.Store.YES, Field.Index.NO));
                    writer.AddDocument(doc);
                }
            }
            writer.Close();
        }

        private static List<string> pexaccNorm(List<string> tokensLine)
        {
            List<string> ret = new List<string>();

            foreach (string token in tokensLine)
            {
                //ret.AddRange(m.simResourcesST.lemmatizeTrgWord(token).Keys);
                ret.Add(m.simResourcesST.lemmatizeTrgWordUnique(token));
            }

            return ret;
        }

        private static List<double> getSentencesLength(IEnumerable<string> files)
        {
            List<double> ret = new List<double>();
            string line = "";

            foreach (string file in files)
            {
                StreamReader rdr = new StreamReader(file, Encoding.UTF8);
                while ((line = rdr.ReadLine()) != null)
                {
                    line = line.Trim();
                    ret.Add(getTokensList(line).Count);
                }
                rdr.Close();
            }

            return ret;
        }

        private static Dictionary<string, string> loadGs(string file1, string file2)
        {
            Dictionary<string, string> ret = new Dictionary<string, string>();
            StreamReader rdr1 = new StreamReader(file1, Encoding.UTF8);
            StreamReader rdr2 = new StreamReader(file2, Encoding.UTF8);
            for (int i = 0; i < 100; i++)
            {
                ret.Add(rdr1.ReadLine().Trim(), rdr2.ReadLine().Trim());
            }
            rdr1.Close();
            rdr2.Close();
            return ret;
        }

        private static HashSet<string> loadGsPlain(string file1, string file2)
        {
            HashSet<string> ret = new HashSet<string>();
            StreamReader rdr1 = new StreamReader(file1, Encoding.UTF8);
            StreamReader rdr2 = new StreamReader(file2, Encoding.UTF8);
            string line = "";
            while ((line = rdr1.ReadLine()) != null)
            {
                string val1 = onlyAlphaNum(line);
                string val2 = onlyAlphaNum(rdr2.ReadLine());

                ret.Add(val1 + "\t" + val2);
            }
            rdr1.Close();
            rdr2.Close();
            return ret;
        }

        private static string onlyAlphaNum(string text)
        {
            text = text.Trim().ToLower();

            Regex regex = new Regex(
                  "\\w",
                RegexOptions.None
                );
            StringBuilder sb = new StringBuilder();
            Match m = regex.Match(text);
            while (m.Success)
            {
                sb.Append(m.Value);
                m = m.NextMatch();
            }
            return sb.ToString();
        }

        private static Dictionary<string, Dictionary<string, double>> readTEs(string fileIn)
        {
            Dictionary<string, Dictionary<string, double>> ret = new Dictionary<string, Dictionary<string, double>>();
            string line = "";
            StreamReader rdr = new StreamReader(fileIn, Encoding.UTF8);
            while ((line = rdr.ReadLine()) != null)
            {
                string[] parts = line.Split(new char[] { '\t', ' ' });

                if (parts.Length == 3)
                {
                    double val = 0;
                    try
                    {
                        val = double.Parse(parts[2], CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        Console.WriteLine("AccuratAligner::Program::readTEs: \"{0}\" in \"{1}\"", line, fileIn);
                    }

                    if (val != 0)
                    {
                        if (!ret.ContainsKey(parts[0]))
                        {
                            ret.Add(parts[0], new Dictionary<string, double>());
                        }
                        if (ret[parts[0]].ContainsKey(parts[1]))
                        {
                            if (val > ret[parts[0]][parts[1]])
                            {
                                ret[parts[0]][parts[1]] = val;
                            }
                        }
                        else
                        {
                            ret[parts[0]].Add(parts[1], val);
                        }
                    }
                }
            }
            rdr.Close();
            return ret;
        }

        private static void recompute(string iFile, string oFile, double threshold, bool useLD, int howManyScoresShouldBeThere, bool filter, bool filePairExists)
        {
            StreamWriter wrt = new StreamWriter(oFile, false, Encoding.UTF8);
            wrt.AutoFlush = true;

            string line = "";
            double total = 0;
            StreamReader rdr = new StreamReader(iFile, Encoding.UTF8);
            while ((line = rdr.ReadLine()) != null)
            {
                total++;
            }
            rdr.Close();

            rdr = new StreamReader(iFile, Encoding.UTF8);
            int cnt = 0;
            while ((line = rdr.ReadLine()) != null)
            {
                cnt++;
                Console.Write("\r{0:#.##}%    ", 100.0 * cnt / total);

                string[] parts = line.Trim().Split('\t');

                //if (parts.Length == howManyScoresShouldBeThere + 2)
                //{
                double score = 0;
                double seScore = 0;
                double danScore = 0;

                string filePair = "";
                if (filePairExists)
                {
                    filePair = "\t" + parts[parts.Length - 1];
                }

                if (howManyScoresShouldBeThere == 1)
                {
                    seScore = double.Parse(parts[2], CultureInfo.InvariantCulture);

                    List<string> tokens1 = getTokensList(parts[0]);
                    List<string> tokens2 = getTokensList(parts[1]);
                    double c1 = tokens1.Count;
                    double c2 = tokens2.Count;
                    score = (((seScore * getLexicalSimilarity(tokens1, tokens2, useLD)) * (1.0 - (Math.Abs((double)(c1 - c2)) / Math.Max(c1, c2)))) * Math.Min(c1, c2)) / 100.0;

                    if (score > threshold)
                    {
                        wrt.WriteLine("{0}\t{1}\t{2}\t{3}{4}", parts[0], parts[1], score, seScore, filePair);
                    }
                }
                else
                {
                    Tuple<PexaccValue, PexaccValue, float> results = m.scoreSentPair(parts[0], parts[1]);

                    score = results.Item3;
                    string wst = getWeights(results.Item1);
                    string wts = getWeights(results.Item2);

                    if (filter)
                    {
                        danScore = double.Parse(parts[2], CultureInfo.InvariantCulture);
                        seScore = double.Parse(parts[3], CultureInfo.InvariantCulture);
                        if (score > threshold)
                        {
                            wrt.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}{7}", parts[0], parts[1], score, wst, wts, danScore, seScore, filePair);
                        }
                    }
                    else
                    {
                        seScore = double.Parse(parts[2], CultureInfo.InvariantCulture);
                        if (score > threshold)
                        {
                            wrt.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}{6}", parts[0], parts[1], score, wst, wts, seScore, filePair);
                        }
                    }
                }
                //}
            }
            rdr.Close();
            wrt.Close();
        }

        private static string getWeights(PexaccValue pexaccValue)
        {
            return pexaccValue.featCntWordTrans_1 + "\t" + pexaccValue.featFuncWordTrans_2 + "\t" + pexaccValue.featAlignScrambled_3 + "\t" + pexaccValue.featTransEnds_4 + "\t" + pexaccValue.featHaveSamePct_5;
        }

        public static int computeLevenshteinDistance(string s, string t)
        {
            if ((s == null) || (t == null))
            {
                throw new Exception("Strings must not be null");
            }
            int n = s.Length;
            int m = t.Length;
            if (n == 0)
            {
                return m;
            }
            if (m == 0)
            {
                return n;
            }
            int[] p = new int[n + 1];
            int[] d = new int[n + 1];
            int i = 0;
            while (i <= n)
            {
                p[i] = i;
                i++;
            }
            for (int j = 1; j <= m; j++)
            {
                char t_j = t[j - 1];
                d[0] = j;
                for (i = 1; i <= n; i++)
                {
                    int cost = (s[i - 1] == t_j) ? 0 : 1;
                    d[i] = Math.Min(Math.Min((int)(d[i - 1] + 1), (int)(p[i] + 1)), p[i - 1] + cost);
                }
                int[] _d = p;
                p = d;
                d = _d;
            }
            return p[n];
        }
    }
}