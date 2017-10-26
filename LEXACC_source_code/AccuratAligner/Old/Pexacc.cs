/*
 * C# port of the PEXACC translation similarity measure. Input file to the LEXACC tool.
 * 
 * (C) ICIA 2011-2012, Author: Radu ION
 * 
 * ver 0.1, Radu ION, 01.12.2011: created.
 * ver 0.2, Radu ION, 02.12.2011: added symmetrical computation.
 * ver 0.3, Radu ION, 06.01.2012: added 'featFunWordsAligned'.
 * ver 0.4, Radu ION, 23.01.2012: added code for PEXACC similarity weight tunning with Octave and Logistic Regression.
 * ver 0.5, Radu ION, 24.01.2012: added el, lt, lv, et and sl diacritic transliteration. Also, for el, complete romanization of their alphabet.
 * ver 0.51, Radu ION, 24.01.2012: added Path.DirectorySeparatorChar for Linux compatibility (Thanks Sabine).
 * ver 0.52, Radu ION, 24.01.2012: fixed the second Resources constructor to accept paths to dir and res directories.
 * ver 0.53, Radu ION, 24.01.2012: fixed the float number parsing according to used locale when reading dictionaries (Thanks Marcis).
 * ver 0.6, Radu ION, 25.01.2012: added similarity measure weights reading from files (trained with a Logistic Regression classifier).
 * ver 0.65, Radu ION, 28.01.2012: added hr diacritic transliteration.
 * ver 0.7, Radu ION, 02.02.2012: fixed the lemmatizeWord function so as not to lemmatize function words.
 * ver 0.8, Radu ION, 02.02.2012: modified the lemmatizeWord to return all possible lemmas. Reading dictionary as it is and doing lemmatization when queried.
 * 
 * 
*/

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Statistics;
using System.Globalization;

namespace PexaccSim
{


    class Configure
    {
        //Probability threshold from the main dictionary
        public const float GIZAPPTHR = 0.001F;
        //If to lemmatize every word or not
        public const bool LEMMAS = true;
        //Sentence ratio: max between source len/target len and target len/source len
        public const float SENTRATIO = 1.5F;
        //String similarity (for cognates) threshold (up to 1.0F)
        public const float SSTHR = 0.7F;
        //Debug flag
        public const bool DEBUG = false;
        //How high the GIZA++ probability is such that the pair is considered correct
        public const float SUREGIZAPPTHR = 0.2F;
        //Size of the half of the target sentence window in which to search for TEQ
        public const int HALFWINTARGET = 5;
        //Size of the half of the target sentence window in which we search for 
        public const int HALFWINFUNCWORDS = 3;
        //How high the GIZA++ probability is such that the functional word pair is considered correct
        public const float SUREGIZAPPTHRFW = 0.1F;
    }

    class PexaccValue
    {
        public float featCntWordTrans_1;
        public float featFuncWordTrans_2;
        public float featAlignScrambled_3;
        public float featTransEnds_4;
        public float featHaveSamePct_5;
        public float pexaccSimScore;

        //Defaults
        public PexaccValue()
        {
            featCntWordTrans_1 = 0.0F;
            featFuncWordTrans_2 = 0.0F;
            featAlignScrambled_3 = 0.0F;
            featTransEnds_4 = 0.0F;
            featHaveSamePct_5 = 0.0F;
            pexaccSimScore = 0.0F;
        }
    }

    class PexaccWeights
    {
        public float weightCntWordTrans_1;
        public float weightFuncWordTrans_2;
        public float weightAlignScrambled_3;
        public float weightTransEnds_4;
        public float weightHaveSamePct_5;
        //This holds the number of weights. For tests.
        public const uint howManyWeights = 5;

        //Defaults
        public PexaccWeights()
        {
            weightCntWordTrans_1 = 0.45F;
            weightFuncWordTrans_2 = 0.2F;
            weightAlignScrambled_3 = 0.15F;
            weightTransEnds_4 = 0.15F;
            weightHaveSamePct_5 = 0.05F;
        }
    }

    class Measure
    {
        string SRCL, TRGL;
        public Resources simResourcesST;
        public Resources simResourcesTS;

        public Measure(string sl, string tl)
        {
            SRCL = sl;
            TRGL = tl;
            simResourcesST = new Resources(sl, tl);
            simResourcesTS = new Resources(tl, sl);
        }

        public Measure(string sl, string tl, string resDir, string dictDir)
        {
            SRCL = sl;
            TRGL = tl;
            simResourcesST = new Resources(sl, tl, resDir, dictDir);
            simResourcesTS = new Resources(tl, sl, resDir, dictDir);
        }

        //Symmetical measure.
        public Tuple<PexaccValue, PexaccValue, float> scoreSentPair(string sourcetext, string targettext)
        {
            PexaccValue pvST = new PexaccValue();
            PexaccValue pvTS = new PexaccValue();

            pvST.pexaccSimScore = scoreSentPairLP(sourcetext, targettext, simResourcesST, ref pvST);
            pvTS.pexaccSimScore = scoreSentPairLP(targettext, sourcetext, simResourcesTS, ref pvTS);

            float score = (pvST.pexaccSimScore + pvTS.pexaccSimScore) / 2;

            //Source-Target and Target-Source values
            return new Tuple<PexaccValue, PexaccValue, float>(pvST, pvTS, score);
        }


        /////////////////////////Octave stuff...
        private string[] readData(string infile)
        {
            List<string> data = new List<string>();
            StreamReader DATA = null;

            try
            {
                DATA = new StreamReader(infile, Encoding.UTF8);
            }
            catch (FileNotFoundException fnfe)
            {
                Console.Error.WriteLine("PexaccSim::Measure::readData: file '" + infile + "' could not be found!" + "(" + fnfe.Message + ")");
                return data.ToArray();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("PexaccSim::Measure::readData: cannot open file '" + infile + "'!" + "(" + e.Message + ")");
                return data.ToArray();
            }

            string line;

            while ((line = DATA.ReadLine()) != null)
            {
                line = Regex.Replace(line, @"^\s+", "");
                line = Regex.Replace(line, @"\s+$", "");

                data.Add(line);
            }

            DATA.Close();
            return data.ToArray();
        }

        public void createOctaveData(string outfilebasename, string langS, string fileS, string langT, string fileT, int yclass)
        {
            string[] sourcesents = readData(fileS);
            string[] targetsents = readData(fileT);

            if (sourcesents.Length != targetsents.Length)
            {
                Console.Error.WriteLine("PexaccSim::Measure::createOctaveData: source and target files do not have the same number of sentences !");
            }

            TextWriter DATAFST = null;
            TextWriter DATAFTS = null;
            string fnamefeatST = outfilebasename + "-" + langS + "-" + langT + ".dat";
            string fnamefeatTS = outfilebasename + "-" + langT + "-" + langS + ".dat";

            try
            {
                DATAFST = new StreamWriter(fnamefeatST, false, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("PexaccSim::Measure::createOctaveData: cannot open file '" + fnamefeatST + "'!" + "(" + e.Message + ")");
                return;
            }

            try
            {
                DATAFTS = new StreamWriter(fnamefeatTS, false, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("PexaccSim::Measure::createOctaveData: cannot open file '" + fnamefeatTS + "'!" + "(" + e.Message + ")");
                return;
            }

            //Write features
            for (uint i = 0; i < (uint)sourcesents.Length; i++)
            {
                string sourcetext = sourcesents[i];
                string targettext = targetsents[i];
                Tuple<PexaccValue, PexaccValue, float> scores = scoreSentPair(sourcetext, targettext);
                PexaccValue pvST = scores.Item1;
                PexaccValue pvTS = scores.Item2;

                if (i > 0 && i % 1000 == 0)
                {
                    System.Console.WriteLine("PexaccSim::Measure::createOctaveData: processed " + i + " lines...");
                }

                DATAFST.WriteLine("{0:0.00000},{1:0.00000},{2:0.00000},{3:0.00000},{4:0.00000},{5:0.00000}", pvST.featCntWordTrans_1, pvST.featFuncWordTrans_2, pvST.featAlignScrambled_3, pvST.featTransEnds_4, pvST.featHaveSamePct_5, (float)yclass);
                DATAFTS.WriteLine("{0:0.00000},{1:0.00000},{2:0.00000},{3:0.00000},{4:0.00000},{5:0.00000}", pvTS.featCntWordTrans_1, pvTS.featFuncWordTrans_2, pvTS.featAlignScrambled_3, pvTS.featTransEnds_4, pvTS.featHaveSamePct_5, (float)yclass);
            }

            DATAFST.Close();
            DATAFTS.Close();
        }
        //////////////////////////End Octave stuff...

        //Takes the input source and target sentences/phrases/chunks and returns a similarity score.
        public float scoreSentPairLP(string sourcetext, string targettext, Resources simRes, ref PexaccValue pxval)
        {
            string[] srcsent = TextProcessing.tokenizeText(sourcetext);
            string[] trgsent = TextProcessing.tokenizeText(targettext);

            //Reject heuristics
            if (srcsent.Length == 0 || trgsent.Length == 0)
            {
                return 0.0F;
            }

            //Reject heuristics
            //Sentences are too long/short or vice-versa
            if (
                (float)srcsent.Length / (float)trgsent.Length > Configure.SENTRATIO ||
                (float)trgsent.Length / (float)srcsent.Length > Configure.SENTRATIO
            )
            {
                return 0.0F;
            }

            List<string> srcsentcwlst = new List<string>();
            List<string> trgsentcwlst = new List<string>();

            //Count content words in source
            foreach (string s in srcsent)
            {
                //Disregard punctuation
                if (Regex.IsMatch(s, @"^\p{P}+$"))
                {
                    continue;
                }
                //Disregard stopwords
                if (simRes.srcStopWords.ContainsKey(s.ToLower()))
                {
                    continue;
                }

                srcsentcwlst.Add(s);
            }

            //Count content words in target
            foreach (string s in trgsent)
            {
                //Disregard punctuation
                if (Regex.IsMatch(s, @"^\p{P}+$"))
                {
                    continue;
                }
                //Disregard stopwords
                if (simRes.trgStopWords.ContainsKey(s.ToLower()))
                {
                    continue;
                }

                trgsentcwlst.Add(s);
            }

            int srcsentlennopunct = srcsentcwlst.Count;
            int trgsentlennopunct = trgsentcwlst.Count;
            string[] srcsentcw = srcsentcwlst.ToArray();
            string[] trgsentcw = trgsentcwlst.ToArray();

            List<float> probs = new List<float>();
            bool foundcontentword = false;
            List<string> foundteqlines = new List<string>();
            Dictionary<int, Tuple<int, float>> maxjskip = new Dictionary<int, Tuple<int, float>>();
            Dictionary<int, Tuple<int, float>> maxjskipcw = new Dictionary<int, Tuple<int, float>>();
            int cwi = -1;

            //Source loop
            for (int i = 0; i < srcsent.Length; i++)
            {
                string w1 = srcsent[i];

                //Disregard punctuation
                if (Regex.IsMatch(w1, @"^\p{P}+$"))
                {
                    continue;
                }
                //Disregard stopwords
                if (simRes.srcStopWords.ContainsKey(w1.ToLower()))
                {
                    continue;
                }

                //For maxjskipcw...
                cwi++;

                int halfwin = Configure.HALFWINTARGET;
                int trglandingj = (int)(((float)trgsent.Length / (float)srcsent.Length) * (float)i);
                int trgleftidx = trglandingj - halfwin;
                int trgrightidx = trglandingj + halfwin;
                bool adjustright = false;
                bool adjustleft = false;

                if (trgleftidx < 0)
                {
                    trgleftidx = 0;
                    adjustright = true;
                }

                if (trgrightidx >= trgsent.Length)
                {
                    trgrightidx = trgsent.Length - 1;
                    adjustleft = true;
                }

                if (adjustright)
                {
                    while (trgrightidx - trgleftidx < 2 * Configure.HALFWINTARGET)
                    {
                        trgrightidx++;
                    }

                    if (trgrightidx >= trgsent.Length)
                    {
                        trgrightidx = trgsent.Length - 1;
                    }
                }

                if (adjustleft)
                {
                    while (trgrightidx - trgleftidx < 2 * Configure.HALFWINTARGET)
                    {
                        trgleftidx--;
                    }

                    if (trgleftidx < 0)
                    {
                        trgleftidx = 0;
                    }
                }


                float maxp = 0.0F;
                int maxj = -1;
                bool foundteq = false;
                int cwj = -1;
                int maxcwj = -1;

                //Setting the last cwj...
                for (int j = 0; j < trgleftidx; j++)
                {
                    string w2 = trgsent[j];

                    //Disregard punctuation
                    if (Regex.IsMatch(w2, @"^\p{P}+$"))
                    {
                        continue;
                    }
                    //Disregard stopwords
                    if (simRes.trgStopWords.ContainsKey(w2.ToLower()))
                    {
                        continue;
                    }

                    cwj++;
                }

                for (int j = trgleftidx; j <= trgrightidx; j++)
                {
                    string w2 = trgsent[j];

                    //Disregard punctuation
                    if (Regex.IsMatch(w2, @"^\p{P}+$"))
                    {
                        continue;
                    }
                    //Disregard stopwords
                    if (simRes.trgStopWords.ContainsKey(w2.ToLower()))
                    {
                        continue;
                    }

                    //For maxjskipcw...
                    cwj++;

                    if (maxjskip.ContainsKey(j))
                    {
                        continue;
                    }

                    float tpprob = 0.0F;
                    string w1norm = TextProcessing.normalizeWord(w1, simRes.srcLang);
                    string w2norm = TextProcessing.normalizeWord(w2, simRes.trgLang);
                    float w1w2ss = TextProcessing.similarity(w1norm.ToLower(), w2norm.ToLower());

                    if (w1.ToLower() == w2.ToLower())
                    {
                        tpprob = 1F;
                        foundteq = true;
                        foundteqlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: found <'" + w1 + "', '" + w2 + "'> with prob = " + tpprob + ".");
                    }
                    else if (w1w2ss >= Configure.SSTHR)
                    {
                        tpprob = w1w2ss;
                        foundteq = true;
                        foundteqlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: found <'" + w1 + "', '" + w2 + "'> with prob = " + tpprob + ".");
                    }
                    else
                    {
                        float w1w2prob = simRes.pairInDict(w1, w2);

                        if (w1w2prob > 0)
                        {
                            tpprob = w1w2prob;
                            foundteq = true;
                            foundteqlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: found <'" + w1 + "', '" + w2 + "'> with prob = " + tpprob + ".");
                        }
                    }

                    if (maxp < tpprob)
                    {
                        maxp = tpprob;
                        maxj = j;
                        maxcwj = cwj;
                    }
                } //end target window

                if (foundteq)
                {
                    probs.Add(maxp);
                    foundcontentword = true;

                    if (maxj >= 0)
                    {
                        maxjskip[maxj] = new Tuple<int, float>(i, maxp);
                        maxjskipcw[maxcwj] = new Tuple<int, float>(cwi, maxp);
                    }
                }

            } //end source sent

            float score = 0.0F;
            List<string> heurprintlines = new List<string>();

            //Score computing
            if (probs.Count > 0 && foundcontentword)
            {
                //How many TEQs did we find
                float T = (float)probs.Count;
                //How long is the source sentence (no punctuation)
                float S = (float)srcsentlennopunct;
                float tpsum = 0.0F;
                //float tpmean = 0.0F;
                //float tpstddev = 0.0F;

                foreach (float p in probs)
                {
                    tpsum += p;
                }

                /*
                tpmean = tpsum / T;

                foreach ( float p in probs ) {
                    tpstddev += ( p - tpmean ) * ( p - tpmean );
                }

                tpstddev = (float)Math.Sqrt( tpstddev / T );
                */

                //Arithmetic mean destabilized by how many words from the source sentence have translations...
                //float scoreTEQ1 = (float)( Math.Pow( T / S, S / T ) * ( tpsum / T ) );
                //float scoreTEQ2 = tpmean;
                float scoreTEQ = tpsum / S;

                //Reject heuristics
                if (!featHaveSameNumbers(srcsent, trgsent))
                {
                    if (Configure.DEBUG)
                    {
                        Console.Error.WriteLine("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]:------------------------");
                        Console.Error.WriteLine(string.Join(Environment.NewLine, foundteqlines.ToArray()));
                        Console.Error.WriteLine("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]:\n");
                        Console.Error.WriteLine("\t" + string.Join(" ", srcsent));
                        Console.Error.WriteLine("\t" + string.Join(" ", trgsent));
                        Console.Error.WriteLine("\t" + "REJECTED by 'featHaveSameNumbers'");
                        Console.Error.WriteLine("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]:------------------------");
                    }

                    return 0.0F;
                }

                //Score heuristics
                //1. CW translation
                float wTEQ = simRes.trainedWeights.weightCntWordTrans_1;

                if (pxval != null)
                {
                    pxval.featCntWordTrans_1 = scoreTEQ;
                }

                score += wTEQ * scoreTEQ;
                heurprintlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: 'featTranslationEQ' score = " + scoreTEQ + ", contribution[w:" + wTEQ + "] = " + (wTEQ * scoreTEQ) + ".");


                //2. FW translation
                float wFWA = simRes.trainedWeights.weightFuncWordTrans_2;
                float scoreFWA = featFunWordsAligned(maxjskip, srcsent, trgsent, simRes);

                if (pxval != null)
                {
                    pxval.featFuncWordTrans_2 = scoreFWA;
                }

                score += wFWA * scoreFWA;
                heurprintlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: 'featFunWordsAligned' score = " + scoreFWA + ", contribution[w:" + wFWA + "] = " + (wFWA * scoreFWA) + ".");


                //3. Alignment not scrambled
                float wANS = simRes.trainedWeights.weightAlignScrambled_3;
                float scoreANS = featAlignNotScrambled(maxjskipcw, srcsentcw.Length - 1, trgsentcw.Length - 1);

                if (pxval != null)
                {
                    pxval.featAlignScrambled_3 = scoreANS;
                }

                score += wANS * scoreANS;
                heurprintlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: 'featAlignNotScrambled' score = " + scoreANS + ", contribution[w:" + wANS + "] = " + (wANS * scoreANS) + ".");


                //4. Starts or ends with a translation
                float wEWT = simRes.trainedWeights.weightTransEnds_4;
                float scoreEWT = featStartOrEndWithTranslations(maxjskipcw, srcsentcw.Length - 1, trgsentcw.Length - 1);

                if (pxval != null)
                {
                    pxval.featTransEnds_4 = scoreEWT;
                }

                score += wEWT * scoreEWT;
                heurprintlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: 'featStartOrEndWithTranslations' score = " + scoreEWT + ", contribution[w:" + wEWT + "] = " + (wEWT * scoreEWT) + ".");


                //5. Have the same punctuation at end
                float wSFP = simRes.trainedWeights.weightHaveSamePct_5;
                float scoreSFP = featHaveSameFinalPunct(srcsent, trgsent);

                if (pxval != null)
                {
                    pxval.featHaveSamePct_5 = scoreSFP;
                }

                score += wSFP * scoreSFP;
                heurprintlines.Add("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]: 'featHaveSameFinalPunct' score = " + scoreSFP + ", contribution[w:" + wSFP + "] = " + (wSFP * scoreSFP) + ".");
            }

            if (score > 0)
            {
                if (Configure.DEBUG)
                {
                    Console.Error.WriteLine("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]:------------------------");
                    Console.Error.WriteLine(string.Join(Environment.NewLine, foundteqlines.ToArray()));
                    Console.Error.WriteLine(string.Join(Environment.NewLine, heurprintlines.ToArray()));
                    Console.Error.WriteLine("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]:\n");
                    Console.Error.WriteLine("\t" + string.Join(" ", srcsent));
                    Console.Error.WriteLine("\t" + string.Join(" ", trgsent));
                    Console.Error.WriteLine("\t" + score);
                    Console.Error.WriteLine("PexaccSim::Measure::scoreSentPair[" + simRes.srcLang + "-" + simRes.trgLang + "]:------------------------");
                }
            }


            return score;
        } //end scoreSentPair

        //ready
        bool featHaveSameNumbers(string[] srcsent, string[] trgsent)
        {
            Dictionary<string, bool> foundsrcent = new Dictionary<string, bool>();

            foreach (string srcw in srcsent)
            {
                if (Regex.IsMatch(srcw, @"[0-9]"))
                {
                    foundsrcent[srcw] = true;
                }
            } //end all src words

            foreach (string trgw in trgsent)
            {
                if (Regex.IsMatch(trgw, @"[0-9]"))
                {
                    bool foundeq = false;

                    foreach (string k in foundsrcent.Keys.ToArray())
                    {
                        if (TextProcessing.similarity(k.ToLower(), trgw.ToLower()) >= 0.5)
                        {
                            foundeq = true;
                            break;
                        }
                    }

                    if (!foundeq)
                    {
                        return false;
                    }
                }
            } //end all trg words

            return true;
        }

        //ready
        float featHaveSameFinalPunct(string[] srcsent, string[] trgsent)
        {
            string lastsrcw = srcsent[srcsent.Length - 1];
            string lasttrgw = trgsent[trgsent.Length - 1];

            if (Regex.IsMatch(lastsrcw, @"^\p{P}+$") || Regex.IsMatch(lasttrgw, @"^\p{P}+$"))
            {
                if (lastsrcw == lasttrgw)
                {
                    return 1.0F;
                }

                return 0.0F;
            }

            return 1.0F;
        }

        //ready
        float featStartOrEndWithTranslations(Dictionary<int, Tuple<int, float>> maxjskip, int maxi, int maxj)
        {
            int[] sortj = maxjskip.Keys.ToArray();

            Array.Sort(sortj);

            bool lefttranslate = false;
            bool righttranslate = false;
            int j = 0;

            while (j <= sortj.Length - 1 && sortj[j] <= 2)
            {
                Tuple<int, float> i4j = maxjskip[sortj[j]];

                if (i4j.Item1 <= 2)
                {
                    if (i4j.Item2 >= Configure.SUREGIZAPPTHR)
                    {
                        lefttranslate = true;
                        break;
                    }
                }

                j++;
            } //end left

            j = sortj.Length - 1;

            while (j >= 0 && Math.Abs(maxj - sortj[j]) <= 2)
            {
                Tuple<int, float> i4j = maxjskip[sortj[j]];

                if (Math.Abs(maxi - i4j.Item1) <= 2)
                {
                    if (i4j.Item2 >= Configure.SUREGIZAPPTHR)
                    {
                        righttranslate = true;
                        break;
                    }
                }

                j--;
            } //end right


            if (lefttranslate && righttranslate)
            {
                return 1.0F;
            }

            return 0.0F;
        }

        //ready
        float featAlignNotScrambled(Dictionary<int, Tuple<int, float>> maxjskip, int maxi, int maxj)
        {
            if (maxjskip.Count == 0)
            {
                return 0.0F;
            }

            if (maxi == 0 || maxj == 0)
            {
                return 0.0F;
            }

            float scrambled = 0.0F;
            int[] jdxs = maxjskip.Keys.ToArray();
            List<double> srcidxes = new List<double>();
            List<double> trgidxes = new List<double>();

            foreach (int j in jdxs)
            {
                Tuple<int, float> i4j = maxjskip[j];

                scrambled += Math.Abs((float)j / (float)maxj - (float)i4j.Item1 / (float)maxi);
                srcidxes.Add((double)i4j.Item1);
                trgidxes.Add((double)j);
            }

            double maxPossibilities = Math.Min(maxi + 1, maxj + 1);

            //return (float)( 1 - scrambled / Math.Pow( (double)maxjskip.Count, (double)2 ) );
            return (float)Math.Abs(SFunctions._correlation_coefficient(srcidxes, trgidxes) * (1 / (1 + Math.Exp(-((double)(maxjskip.Keys.Count) / (maxPossibilities / 2.0) - 1) * 5))));
        }

        float featFunWordsAligned(Dictionary<int, Tuple<int, float>> maxjskip, string[] srcsent, string[] trgsent, Resources simRes)
        {
            int[] jdxs = maxjskip.Keys.ToArray();
            uint howmanytransfuncw = (uint)jdxs.Length;
            uint transfuncw = 0;

            //For each alignment
            foreach (int j in jdxs)
            {
                Tuple<int, float> i4j = maxjskip[j];
                int i = i4j.Item1;
                float tprob = i4j.Item2;
                bool foundtrans = false;
                bool arefuncwords = false;

                for (int x = i - PexaccSim.Configure.HALFWINFUNCWORDS; x >= 0 && x < srcsent.Length && x <= i + PexaccSim.Configure.HALFWINFUNCWORDS; x++)
                {
                    string srcw = srcsent[x];

                    if (!simRes.isSrcStopWord(srcw))
                    {
                        continue;
                    }

                    arefuncwords = true;

                    for (int y = j - PexaccSim.Configure.HALFWINFUNCWORDS; y >= 0 && y < trgsent.Length && y <= j + PexaccSim.Configure.HALFWINFUNCWORDS; y++)
                    {
                        string trgw = trgsent[y];

                        if (!simRes.isTrgStopWord(trgw))
                        {
                            continue;
                        }

                        if (simRes.pairInDict(srcw, trgw) >= PexaccSim.Configure.SUREGIZAPPTHRFW)
                        {
                            foundtrans = true;
                            transfuncw++;
                            break;
                        }
                    }

                    if (foundtrans)
                    {
                        break;
                    } //end target window
                } //end source window

                if (!arefuncwords)
                {
                    howmanytransfuncw--;
                }
            } //end all alignments

            //If not functional words, this is 1.
            if (howmanytransfuncw == 0)
            {
                return 1.0F;
            }

            return (float)transfuncw / (float)howmanytransfuncw;
        }

        //static void Main( string[] args ) {
        //    Measure m = new Measure( "en", "ro" );

        //    Tuple<PexaccValue, PexaccValue, float> res = m.scoreSentPair(
        //        @"Oxfam warns food prices to double by 2030",
        //        @"Oxfam avertizează: Preţul alimentelor se va dubla până în 2030"
        //    );

        //    //m.createOctaveData( "train-neg", "en", "train.neg.en", "ro", "train.neg.ro", 0 );

        //    Console.ReadLine();
        //}
    }


    class Resources
    {
        public string srcLang;
        public string trgLang;
        public Dictionary<string, int> srcInflections;
        public Dictionary<string, int> trgInflections;
        public Dictionary<string, int> srcStopWords;
        public Dictionary<string, int> trgStopWords;
        public Dictionary<string, float> gizaDictionary;
        public PexaccWeights trainedWeights;

        //Source language, two chars language code, lower case, e.g. "en"
        //Target language, two chars language code, lower case, e.g. "ro"
        public Resources(string sl, string tl)
        {
            srcInflections = readInflectionList(@"res" + Path.DirectorySeparatorChar + "endings_" + sl + ".txt");
            trgInflections = readInflectionList(@"res" + Path.DirectorySeparatorChar + "endings_" + tl + ".txt");
            //Stop words must be read before dictionary reading...
            srcStopWords = readStopWordsList(@"res" + Path.DirectorySeparatorChar + "stopwords_" + sl + ".txt");
            //Stop words must be read before dictionary reading...
            trgStopWords = readStopWordsList(@"res" + Path.DirectorySeparatorChar + "stopwords_" + tl + ".txt");
            trainedWeights = readTrainedWeights(@"res" + Path.DirectorySeparatorChar + "weights_" + sl + "-" + tl + ".txt");
            //Uses stop words lists!
            gizaDictionary = readGIZAPPDict(@"dict" + Path.DirectorySeparatorChar + sl + "_" + tl);
            srcLang = sl;
            trgLang = tl;
        }

        //Source language, two chars language code, lower case, e.g. "en"
        //Target language, two chars language code, lower case, e.g. "ro"
        //Directory where all the resource files are
        public Resources(string sl, string tl, string resPath, string dictPath)
        {
            if (!Regex.IsMatch(resPath, Path.DirectorySeparatorChar + "$"))
            {
                resPath += Path.DirectorySeparatorChar;
            }

            if (!Regex.IsMatch(dictPath, Path.DirectorySeparatorChar + "$"))
            {
                dictPath += Path.DirectorySeparatorChar;
            }

            srcInflections = this.readInflectionList(resPath + "endings_" + sl + ".txt");
            trgInflections = this.readInflectionList(resPath + "endings_" + tl + ".txt");
            //Stop words must be read before dictionary reading...
            srcStopWords = readStopWordsList(resPath + "stopwords_" + sl + ".txt");
            //Stop words must be read before dictionary reading...
            trgStopWords = readStopWordsList(resPath + "stopwords_" + tl + ".txt");
            trainedWeights = readTrainedWeights(resPath + "weights_" + sl + "-" + tl + ".txt");
            //Uses stop words lists!
            gizaDictionary = readGIZAPPDict(dictPath + sl + "_" + tl);
            srcLang = sl;
            trgLang = tl;
        }

        //One line containing the weights
        private PexaccWeights readTrainedWeights(string wfile)
        {
            StreamReader THT = null;

            try
            {
                THT = new StreamReader(wfile, Encoding.UTF8);
            }
            catch (FileNotFoundException fnfe)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readTrainedWeights: " + fnfe.Message);
                Console.Error.WriteLine("PexaccSim::Resources::readTrainedWeights: using defaults...");
                return new PexaccWeights();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readTrainedWeights: cannot open file '" + wfile + "'!" + "(" + e.Message + ")");
                return null;
            }

            string line;
            PexaccWeights pxwh = new PexaccWeights();

            while ((line = THT.ReadLine()) != null)
            {
                line = Regex.Replace(line, @"^\s+", "");
                line = Regex.Replace(line, @"\s+$", "");

                //Skip empty lines
                if (line == "")
                {
                    continue;
                }
                //Skip comments
                else if (Regex.IsMatch(line, "^#"))
                {
                    continue;
                }

                string[] weights = Regex.Split(line, @"\s+");

                if (weights.Length != PexaccWeights.howManyWeights)
                {
                    continue;
                }
                else
                {
                    pxwh.weightCntWordTrans_1 = System.Convert.ToSingle(weights[0], CultureInfo.InvariantCulture);
                    pxwh.weightFuncWordTrans_2 = System.Convert.ToSingle(weights[1], CultureInfo.InvariantCulture);
                    pxwh.weightAlignScrambled_3 = System.Convert.ToSingle(weights[2], CultureInfo.InvariantCulture);
                    pxwh.weightTransEnds_4 = System.Convert.ToSingle(weights[3], CultureInfo.InvariantCulture);
                    pxwh.weightHaveSamePct_5 = System.Convert.ToSingle(weights[4], CultureInfo.InvariantCulture);
                    break;
                }
            }

            THT.Close();
            return pxwh;
        }

        //ready
        Dictionary<string, int> readInflectionList(string rfile)
        {
            Dictionary<string, int> infl = new Dictionary<string, int>();

            infl["LONGEST"] = 0;

            StreamReader INF = null;

            try
            {
                INF = new StreamReader(rfile, Encoding.UTF8);
            }
            catch (FileNotFoundException fnfe)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readInflectionList: file '" + rfile + "' could not be found!" + "(" + fnfe.Message + ")");
                return infl;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readInflectionList: cannot open file '" + rfile + "'!" + "(" + e.Message + ")");
                return infl;
            }

            string line;

            while ((line = INF.ReadLine()) != null)
            {
                line = Regex.Replace(line, @"^\s+", "");
                line = Regex.Replace(line, @"\s+$", "");

                //sh si tz la diacritice
                infl[line.Replace('\x0163', '\x021B').ToLower()] = line.Length;
                infl[line.Replace('\x021B', '\x0163').ToLower()] = line.Length;
                infl[line.Replace('\x015F', '\x0219').ToLower()] = line.Length;
                infl[line.Replace('\x0219', '\x015F').ToLower()] = line.Length;
                infl[line.Replace('\x0163', '\x021B').Replace('\x015F', '\x0219').ToLower()] = line.Length;
                infl[line.Replace('\x0163', '\x021B').Replace('\x0219', '\x015F').ToLower()] = line.Length;
                infl[line.Replace('\x021B', '\x0163').Replace('\x015F', '\x0219').ToLower()] = line.Length;
                infl[line.Replace('\x021B', '\x0163').Replace('\x0219', '\x015F').ToLower()] = line.Length;
                infl[line.ToLower()] = line.Length;

                if (infl["LONGEST"] < line.Length)
                {
                    infl["LONGEST"] = line.Length;
                }
            } //EOF

            INF.Close();
            return infl;
        } //end readInflectionList

        //ready
        Dictionary<string, int> readStopWordsList(string rfile)
        {
            Dictionary<string, int> swl = new Dictionary<string, int>();
            StreamReader SWL = null;

            try
            {
                SWL = new StreamReader(rfile, Encoding.UTF8);
            }
            catch (FileNotFoundException fnfe)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readStopWordsList: file '" + rfile + "' could not be found!" + "(" + fnfe.Message + ")");
                return swl;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readStopWordsList: cannot open file '" + rfile + "'!" + "(" + e.Message + ")");
                return swl;
            }

            string line;

            while ((line = SWL.ReadLine()) != null)
            {
                line = Regex.Replace(line, @"^\s+", "");
                line = Regex.Replace(line, @"\s+$", "");

                swl[line.Replace('\x0163', '\x021B').ToLower()] = line.Length;
                swl[line.Replace('\x021B', '\x0163').ToLower()] = line.Length;
                swl[line.Replace('\x015F', '\x0219').ToLower()] = line.Length;
                swl[line.Replace('\x0219', '\x015F').ToLower()] = line.Length;
                swl[line.Replace('\x0163', '\x021B').Replace('\x015F', '\x0219').ToLower()] = line.Length;
                swl[line.Replace('\x0163', '\x021B').Replace('\x0219', '\x015F').ToLower()] = line.Length;
                swl[line.Replace('\x021B', '\x0163').Replace('\x015F', '\x0219').ToLower()] = line.Length;
                swl[line.Replace('\x021B', '\x0163').Replace('\x0219', '\x015F').ToLower()] = line.Length;
                swl[line.ToLower()] = line.Length;
            }

            SWL.Close();
            return swl;
        } //end readStopWordsList

        public bool isSrcStopWord(string word)
        {
            return srcStopWords.ContainsKey(word.ToLower());
        }

        public bool isTrgStopWord(string word)
        {
            return trgStopWords.ContainsKey(word.ToLower());
        }

        //symmetric measure ready.
        Dictionary<string, float> readGIZAPPDict(string rfile)
        {
            Dictionary<string, float> gizapp = new Dictionary<string, float>();
            StreamReader DICT = null;

            try
            {
                DICT = new StreamReader(rfile, Encoding.UTF8);
            }
            catch (FileNotFoundException fnfe)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readGIZAPPDict: file '" + rfile + "' could not be found!" + "(" + fnfe.Message + ")");
                return gizapp;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("PexaccSim::Resources::readGIZAPPDict: cannot open file '" + rfile + "'!" + "(" + e.Message + ")");
                return gizapp;
            }

            string line;
            int linecnt = 0;

            while ((line = DICT.ReadLine()) != null)
            {
                linecnt++;

                if (linecnt % 100000 == 0)
                {
                    Console.Error.WriteLine("PexaccSim::Resources::readGIZAPPDict['" + rfile + "']: read " + linecnt + " lines...");
                }

                line = Regex.Replace(line, @"^\s+", "");
                line = Regex.Replace(line, @"\s+$", "");

                if (line == "")
                {
                    continue;
                }

                string sprx = @"\s+";

                if (Regex.IsMatch(line, @"\t") && Regex.IsMatch(line, @" "))
                {
                    sprx = @"\t+";
                }

                string[] toks = Regex.Split(line, sprx);

                if (toks.Length == 3)
                {
                    string enw = toks[0].ToLower();
                    string row = toks[1].ToLower();
                    float score = System.Convert.ToSingle(toks[2], CultureInfo.InvariantCulture);

                    //Do not read pairs with low translation probability.
                    if (score < Configure.GIZAPPTHR)
                    {
                        continue;
                    }

                    //Romanian diacritics...
                    Dictionary<string, int> envar = new Dictionary<string, int>();
                    Dictionary<string, int> rovar = new Dictionary<string, int>();

                    envar[enw.Replace('\x0163', '\x021B')] = enw.Length;
                    envar[enw.Replace('\x021B', '\x0163')] = enw.Length;
                    envar[enw.Replace('\x015F', '\x0219')] = enw.Length;
                    envar[enw.Replace('\x0219', '\x015F')] = enw.Length;
                    envar[enw.Replace('\x0163', '\x021B').Replace('\x015F', '\x0219')] = enw.Length;
                    envar[enw.Replace('\x0163', '\x021B').Replace('\x0219', '\x015F')] = enw.Length;
                    envar[enw.Replace('\x021B', '\x0163').Replace('\x015F', '\x0219')] = enw.Length;
                    envar[enw.Replace('\x021B', '\x0163').Replace('\x0219', '\x015F')] = enw.Length;
                    envar[enw] = enw.Length;

                    rovar[row.Replace('\x0163', '\x021B')] = row.Length;
                    rovar[row.Replace('\x021B', '\x0163')] = row.Length;
                    rovar[row.Replace('\x015F', '\x0219')] = row.Length;
                    rovar[row.Replace('\x0219', '\x015F')] = row.Length;
                    rovar[row.Replace('\x0163', '\x021B').Replace('\x015F', '\x0219')] = row.Length;
                    rovar[row.Replace('\x0163', '\x021B').Replace('\x0219', '\x015F')] = row.Length;
                    rovar[row.Replace('\x021B', '\x0163').Replace('\x015F', '\x0219')] = row.Length;
                    rovar[row.Replace('\x021B', '\x0163').Replace('\x0219', '\x015F')] = row.Length;
                    rovar[row] = row.Length;

                    foreach (string e in envar.Keys.ToArray())
                    {
                        foreach (string r in rovar.Keys.ToArray())
                        {
                            if (Configure.LEMMAS)
                            {
                                Dictionary<string, int> elems = lemmatizeSrcWord(e);
                                Dictionary<string, int> rlems = lemmatizeTrgWord(r);

                                foreach (string el in elems.Keys.ToArray())
                                {
                                    foreach (string rl in rlems.Keys.ToArray())
                                    {
                                        string tpair = el + "#" + rl;

                                        //If we have multiple entries... select the maximum score.
                                        if (!gizapp.ContainsKey(tpair))
                                        {
                                            gizapp[tpair] = score;
                                        }
                                        else if (gizapp[tpair] < score)
                                        {
                                            gizapp[tpair] = score;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                string tpair = e + "#" + r;

                                //If we have multiple entries... select the maximum score.
                                if (!gizapp.ContainsKey(tpair))
                                {
                                    gizapp[tpair] = score;
                                }
                                else if (gizapp[tpair] < score)
                                {
                                    gizapp[tpair] = score;
                                }
                            }
                        }
                    }
                }
            } //end all dict file

            return gizapp;
        } //readGIZAPPDict

        //symmetric measure ready.
        //ver 0.8 ready. Doing lemmatization here if set.
        public float pairInDict(string srcw, string trgw)
        {
            srcw = srcw.ToLower();
            trgw = trgw.ToLower();

            if (Configure.LEMMAS)
            {
                Dictionary<string, int> srcwlems = lemmatizeSrcWord(srcw);
                Dictionary<string, int> trgwlems = lemmatizeTrgWord(trgw);
                float maxscore = 0.0F;

                foreach (string sl in srcwlems.Keys.ToArray())
                {
                    foreach (string tl in trgwlems.Keys.ToArray())
                    {
                        string tpair = sl + "#" + tl;

                        //If we have multiple entries... select the maximum score.
                        if (gizaDictionary.ContainsKey(tpair))
                        {
                            if (gizaDictionary[tpair] > maxscore)
                            {
                                maxscore = gizaDictionary[tpair];
                            }
                        }
                    } //end all target lemmas
                } //end all source lemmas

                return maxscore;
            }
            else
            {
                string tpair = srcw + "#" + trgw;

                if (gizaDictionary.ContainsKey(tpair))
                {
                    return gizaDictionary[tpair];
                }
            }

            return 0.0F;
        }

        //ready: hash table of word and its length in chars.		
        public Dictionary<string, int> lemmatizeSrcWord(string word)
        {
            Dictionary<string, int> lemmas = new Dictionary<string, int>();

            lemmas[word] = word.Length;

            //For function words, return the word as it is.
            if (isSrcStopWord(word))
            {
                return lemmas;
            }

            return lemmatizeWord(word, srcInflections);
        }

        public Dictionary<string, int> lemmatizeTrgWord(string word)
        {
            Dictionary<string, int> lemmas = new Dictionary<string, int>();

            lemmas[word] = word.Length;

            //For function words, return the word as it is.
            if (isTrgStopWord(word))
            {
                return lemmas;
            }

            return lemmatizeWord(word, trgInflections);
        }

        private Dictionary<string, int> lemmatizeWord(string word, Dictionary<string, int> infllist)
        {
            Dictionary<string, int> lemmas = new Dictionary<string, int>();

            word = word.ToLower();
            lemmas[word] = word.Length;

            char[] wordlett = word.ToCharArray();
            string lemword = word;

            //Match the longest suffix ...
            for (int i = wordlett.Length - 1; i >= 1; i--)
            {
                string crtsfx = "";

                for (int j = i; j <= wordlett.Length - 1; j++)
                {
                    crtsfx += wordlett[j];
                }

                if (infllist.ContainsKey(crtsfx))
                {
                    lemword = word;
                    lemword = Regex.Replace(lemword, crtsfx + '$', "");
                    lemmas[lemword] = lemword.Length;
                }
            }

            return lemmas;
        }

    } //end of Resources


    class TextProcessing
    {
        //ready
        public static string normalizeWord(string word, string lang)
        {
            //en-ro specific normalizations...
            if (lang == "ro")
            {
                //acirc
                word = word.Replace('\x00E2', 'a');
                //Acirc
                word = word.Replace('\x00C2', 'A');
                //icirc
                word = word.Replace('\x00EE', 'i');
                //Icirc
                word = word.Replace('\x00CE', 'I');
                //abreve
                word = word.Replace('\x0103', 'a');
                //Abreve
                word = word.Replace('\x0102', 'A');
                //scedil
                word = word.Replace('\x015F', 's');
                word = word.Replace('\x0219', 's');
                //Scedil
                word = word.Replace('\x015E', 'S');
                word = word.Replace('\x0218', 'S');
                //tcedil
                word = word.Replace('\x0163', 't');
                word = word.Replace('\x021B', 't');
                //Tcedil
                word = word.Replace('\x0162', 'T');
                word = word.Replace('\x021A', 'T');
            }
            else if (lang == "en")
            {
                //'f' for 'ph'
                word = Regex.Replace(word, "[pP][hH]", "f");
                //remove h in front of a consonant
                word = Regex.Replace(word, "[Hh]([^aAeEiIoOuUyY])", delegate(Match mev) { return mev.Groups[1].Value; });
                //remove double consonant
                word = Regex.Replace(word, @"([^aAeEiIoOuUyY])\1", delegate(Match mev) { return mev.Groups[1].Value; });
            }
            else if (lang == "el")
            {
                //Greek UN/ELOT transliteration standard
                //Special cases
                word = Regex.Replace(word, "^μπ", "b");
                word = Regex.Replace(word, "(.)μπ(.)", delegate(Match mev) { return mev.Groups[1].Value + "mp" + mev.Groups[2].Value; });
                word = Regex.Replace(word, "([βγδζλμνραεηιουω])αυ", delegate(Match mev) { return mev.Groups[1].Value + "av"; });
                word = Regex.Replace(word, "([θκξπστφχψ])αυ$", delegate(Match mev) { return mev.Groups[1].Value + "af"; });
                word = Regex.Replace(word, "([βγδζλμνραεηιουω])ευ", delegate(Match mev) { return mev.Groups[1].Value + "ev"; });
                word = Regex.Replace(word, "([θκξπστφχψ])ευ$", delegate(Match mev) { return mev.Groups[1].Value + "ef"; });
                word = Regex.Replace(word, "([βγδζλμνραεηιουω])ηυ", delegate(Match mev) { return mev.Groups[1].Value + "iv"; });
                word = Regex.Replace(word, "([θκξπστφχψ])ηυ$", delegate(Match mev) { return mev.Groups[1].Value + "if"; });

                //Vowel digraphs
                word = word.Replace("αι", "ai");
                word = word.Replace("ει", "ei");
                word = word.Replace("οι", "oi");
                word = word.Replace("ου", "ou");
                word = word.Replace("υι", "yi");

                //Consonant digraphs
                word = word.Replace("γγ", "ng");
                word = word.Replace("γξ", "nx");
                word = word.Replace("γκ", "gk");
                word = word.Replace("γχ", "nch");
                word = word.Replace("ντ", "nt");

                //Alphabet
                word = word.Replace('α', 'a');
                word = word.Replace('β', 'v');
                word = word.Replace('γ', 'g');
                word = word.Replace('δ', 'd');
                word = word.Replace('ε', 'e');
                word = word.Replace('ζ', 'z');
                word = word.Replace('η', 'i');
                word = word.Replace("θ", "th");
                word = word.Replace('ι', 'i');
                word = word.Replace('κ', 'k');
                word = word.Replace('λ', 'l');
                word = word.Replace('μ', 'm');
                word = word.Replace('ν', 'n');
                word = word.Replace('ξ', 'x');
                word = word.Replace('ο', 'o');
                word = word.Replace('π', 'p');
                word = word.Replace('ρ', 'r');
                word = word.Replace('σ', 's');
                word = word.Replace('τ', 't');
                word = word.Replace('υ', 'y');
                word = word.Replace('φ', 'f');
                word = word.Replace("χ", "ch");
                word = word.Replace("ψ", "ps");
                word = word.Replace('ω', 'o');
                word = word.Replace('Α', 'A');
                word = word.Replace('Β', 'V');
                word = word.Replace('Γ', 'G');
                word = word.Replace('Δ', 'D');
                word = word.Replace('Ε', 'E');
                word = word.Replace('Ζ', 'Z');
                word = word.Replace('Η', 'I');
                word = word.Replace("Θ", "TH");
                word = word.Replace('Ι', 'I');
                word = word.Replace('Κ', 'K');
                word = word.Replace('Λ', 'L');
                word = word.Replace('Μ', 'M');
                word = word.Replace('Ν', 'N');
                word = word.Replace('Ξ', 'X');
                word = word.Replace('Ο', 'O');
                word = word.Replace('Π', 'P');
                word = word.Replace('Ρ', 'R');
                word = word.Replace('Σ', 'S');
                word = word.Replace('Τ', 'T');
                word = word.Replace('Υ', 'Y');
                word = word.Replace('Φ', 'F');
                word = word.Replace("Χ", "CH");
                word = word.Replace("Ψ", "PS");
                word = word.Replace('Ω', 'O');

                //Diacritics
                word = word.Replace('ά', 'a');
                word = word.Replace('έ', 'e');
                word = word.Replace('ή', 'i');
                word = word.Replace('ί', 'i');
                word = word.Replace('ϊ', 'i');
                word = word.Replace('ΐ', 'i');
                word = word.Replace('ό', 'o');
                word = word.Replace('ύ', 'y');
                word = word.Replace('ϋ', 'y');
                word = word.Replace('ΰ', 'y');
                word = word.Replace('ώ', 'o');
                word = word.Replace('Ά', 'A');
                word = word.Replace('Έ', 'E');
                word = word.Replace('Ή', 'I');
                word = word.Replace('Ί', 'I');
                word = word.Replace('Ϊ', 'I');
                word = word.Replace('Ό', 'O');
                word = word.Replace('Ύ', 'Y');
                word = word.Replace('Ϋ', 'Y');
                word = word.Replace('Ώ', 'O');
            }
            else if (lang == "lv")
            {
                //Diacritics
                word = word.Replace('ā', 'a');
                word = word.Replace('Ā', 'A');
                word = word.Replace('č', 'c');
                word = word.Replace('Č', 'C');
                word = word.Replace('ē', 'e');
                word = word.Replace('Ē', 'E');
                word = word.Replace('ģ', 'g');
                word = word.Replace('Ģ', 'G');
                word = word.Replace('ī', 'i');
                word = word.Replace('Ī', 'I');
                word = word.Replace('ķ', 'k');
                word = word.Replace('Ķ', 'K');
                word = word.Replace('ļ', 'l');
                word = word.Replace('Ļ', 'L');
                word = word.Replace('ņ', 'n');
                word = word.Replace('Ņ', 'N');
                word = word.Replace('š', 's');
                word = word.Replace('Š', 'S');
                word = word.Replace('ū', 'u');
                word = word.Replace('Ū', 'U');
                word = word.Replace('ž', 'z');
                word = word.Replace('Ž', 'Z');
            }
            else if (lang == "lt")
            {
                //Diacritics
                word = word.Replace('Ą', 'A');
                word = word.Replace('Č', 'C');
                word = word.Replace('Ę', 'E');
                word = word.Replace('Ė', 'E');
                word = word.Replace('Į', 'I');
                word = word.Replace('Š', 'S');
                word = word.Replace('Ų', 'U');
                word = word.Replace('Ū', 'U');
                word = word.Replace('Ž', 'Z');
                word = word.Replace('ą', 'a');
                word = word.Replace('č', 'c');
                word = word.Replace('ę', 'e');
                word = word.Replace('ė', 'e');
                word = word.Replace('į', 'i');
                word = word.Replace('š', 's');
                word = word.Replace('ų', 'u');
                word = word.Replace('ū', 'u');
                word = word.Replace('ž', 'z');
            }
            else if (lang == "et")
            {
                //Diacritics
                word = word.Replace('Š', 'S');
                word = word.Replace('š', 's');
                word = word.Replace('Ž', 'Z');
                word = word.Replace('ž', 'z');
                word = word.Replace('Õ', 'O');
                word = word.Replace('õ', 'o');
                word = word.Replace('Ä', 'A');
                word = word.Replace('ä', 'a');
                word = word.Replace('Ö', 'O');
                word = word.Replace('ö', 'o');
                word = word.Replace('Ü', 'U');
                word = word.Replace('ü', 'u');
            }
            else if (lang == "sl")
            {
                //Diacritics
                word = word.Replace('č', 'c');
                word = word.Replace('Č', 'C');
                word = word.Replace('ć', 'c');
                word = word.Replace('Ć', 'C');
                word = word.Replace('Đ', 'D');
                word = word.Replace('đ', 'd');
                word = word.Replace('š', 's');
                word = word.Replace('Š', 'S');
                word = word.Replace('ž', 'Ž');
                word = word.Replace('Ž', 'Z');
                word = word.Replace('é', 'e');
                word = word.Replace('É', 'E');
                word = word.Replace('â', 'a');
                word = word.Replace('Â', 'A');
                word = word.Replace('á', 'a');
                word = word.Replace('Á', 'A');
                word = word.Replace('à', 'a');
                word = word.Replace('À', 'A');
                word = word.Replace('í', 'i');
                word = word.Replace('Í', 'I');
                word = word.Replace('ì', 'i');
                word = word.Replace('Ì', 'I');
                word = word.Replace('é', 'e');
                word = word.Replace('É', 'E');
                word = word.Replace('Ó', 'O');
                word = word.Replace('ó', 'o');
                word = word.Replace('ú', 'u');
                word = word.Replace('Ú', 'U');
                word = word.Replace('ŕ', 'r');
                word = word.Replace('Ŕ', 'R');
            }
            else if (lang == "hr")
            {
                word = word.Replace('Č', 'C');
                word = word.Replace('č', 'c');
                word = word.Replace('Ć', 'C');
                word = word.Replace('ć', 'c');
                word = word.Replace('Đ', 'D');
                word = word.Replace('đ', 'd');
                word = word.Replace('Š', 'S');
                word = word.Replace('š', 's');
                word = word.Replace('Ž', 'Z');
                word = word.Replace('ž', 'z');
            }

            return word;
        } //end of normalizeWord

        //ready
        public static string[] tokenizeText(string text)
        {
            string[] toktext = Regex.Split(text, @"\s+");
            List<string> finaltoktext = new List<string>();

            foreach (string t in toktext)
            {
                string tt = t;

                if (tt == "")
                {
                    continue;
                }

                //Remove punctuation from the beginning
                Regex nonwordrx = new Regex(@"^(\W+)");
                Match nonwordm = nonwordrx.Match(tt);

                if (nonwordm.Success)
                {
                    char[] punct = nonwordm.Groups[1].Value.ToCharArray();
                    List<string> punctstr = new List<string>();

                    foreach (char c in punct)
                    {
                        punctstr.Add(c.ToString());
                    }

                    finaltoktext.AddRange(punctstr);
                    tt = Regex.Replace(tt, @"^\W+", "");
                }

                //Remove punctuation from the end
                nonwordrx = new Regex(@"(\W+)$");
                nonwordm = nonwordrx.Match(tt);

                if (nonwordm.Success)
                {
                    char[] punct = nonwordm.Groups[1].Value.ToCharArray();
                    List<string> punctstr = new List<string>();

                    foreach (char c in punct)
                    {
                        punctstr.Add(c.ToString());
                    }

                    tt = Regex.Replace(tt, @"\W+$", "");

                    if (tt != "")
                    {
                        finaltoktext.Add(tt);
                    }

                    finaltoktext.AddRange(punctstr);
                }
                else
                {
                    if (tt != "")
                    {
                        finaltoktext.Add(tt);
                    }
                }
            } //end all tokens

            return finaltoktext.ToArray();
        } // end of tokenizeText

        public static float similarity(string s, string t)
        {
            float ret = 0.0F;

            if (s == null || t == null)
            {
                return ret;
            }

            int n = s.Length; // length of s
            int m = t.Length; // length of t

            if (n == 0)
            {
                //m
                return ret;
            }
            else if (m == 0)
            {
                //n
                return ret;
            }
            else
            {
                int[] p = new int[n + 1]; // 'previous' cost array, horizontally
                int[] d = new int[n + 1]; // cost array, horizontally
                int[] _d; // placeholder to assist in swapping p and d

                // indexes into strings s and t
                int i; // iterates through s
                int j; // iterates through t
                char t_j; // jth character of t
                int cost; // cost

                for (i = 0; i <= n; i++)
                {
                    p[i] = i;
                }

                for (j = 1; j <= m; j++)
                {
                    t_j = t[j - 1];
                    d[0] = j;

                    for (i = 1; i <= n; i++)
                    {
                        cost = s[i - 1] == t_j ? 0 : 1;
                        // minimum of cell to the left+1, to the top+1, diagonally
                        // left
                        // and up +cost
                        d[i] = Math.Min(Math.Min(d[i - 1] + 1, p[i] + 1), p[i - 1] + cost);
                    } //end i

                    // copy current distance counts to 'previous row' distance
                    // counts
                    _d = p;
                    p = d;
                    d = _d;
                } //end j

                // our last action in the above loop was to switch d and p, so p now
                // actually has the most recent cost counts
                ret = p[n];
            }

            //More conservative functionality
            return (Math.Max(n, m) - ret) / Math.Max(n, m);
        } // public static float similarity( string s, string t )

        public static string[] splitSentences(string text)
        {
            //Regex ssrx = new Regex( @"(.+?(?<![\s.]\p{Lu})(?<![\s\.]\p{Lu}[bcdfgjklmnprstvxz])(?<![\s\.]\p{Lu}[bcdfgjklmnprstvxz][bcdfgjklmnprstvxz])[.?!]+)((?=\s*[\p{Lu}\[\(""']))" );
            Regex ssrx = new Regex(@"(\p{Ll}\s*[.?!""';:]+?\s*)(\s*\p{Lu}(?:\p{Lu}|\p{Ll})+)");
            Match m = ssrx.Match(text);

            while (m.Success)
            {
                text = text.Replace(m.Value, m.Groups[1].Value + "#CUT#" + m.Groups[2].Value);
                m = m.NextMatch();
            } //end all sentences.

            string[] sentences = Regex.Split(text, "#CUT#");
            List<string> finalsentences = new List<string>();

            foreach (string s in sentences)
            {
                finalsentences.Add(Regex.Replace(Regex.Replace(s, @"\s+$", ""), @"^\s+", ""));
            }

            return finalsentences.ToArray();
        }

    } //end of TextProcessing
}
