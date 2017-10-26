/*
 * C# Statistics
 * 
 * (C) ICIA 2011-2012, Author: Dan ŞTEFĂNESCU
 * 
 * www.racai.ro/~danstef
 * danstef@racai.ro
 * dstefanescu@gmail.com
 * 
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace Statistics
{
    public static class SFunctions
    {
        public static double _maximum(List<double> al, ref int index)
        {
            if (al.Count == 0)
            {
                index = -1;
                return 0;
            }
            if (al.Count == 1)
            {
                index = 0;
                return al[0];
            }
            double max = al[0];
            index = 0;
            for (int i = 1; i < al.Count; i++)
                if (max < al[i])
                {
                    max = al[i];
                    index = i;
                }

            return max;
        }

        public static double _maximum(List<double> al)
        {
            if (al.Count == 0)
                return 0;
            if (al.Count == 1)
                return al[0];

            double max = al[0];
            for (int i = 1; i < al.Count; i++)
                if (max < al[i])
                    max = al[i];

            return max;
        }

        public static double _minimum(List<double> al, ref int index)
        {
            if (al.Count == 0)
            {
                index = -1;
                return 0;
            }
            if (al.Count == 1)
            {
                index = 0;
                return al[0];
            }
            double min = al[0];
            index = 0;
            for (int i = 1; i < al.Count; i++)
                if (min > al[i])
                {
                    min = al[i];
                    index = i;
                }

            return min;
        }

        public static double _minimum(List<double> al)
        {
            if (al.Count == 0)
                return 0;
            if (al.Count == 1)
                return al[0];
            double min = al[0];

            for (int i = 1; i < al.Count; i++)
                if (min > al[i])
                    min = al[i];

            return min;
        }

        public static double _median(List<double> al)
        {
            if (al.Count > 1)
            {
                al.Sort();
                if (al.Count % 2 == 1)
                    return al[Math.Abs(al.Count / 2)];
                else
                    return (al[al.Count / 2 - 1] + al[al.Count / 2]) / 2.0;
            }
            else
            {
                if (al.Count == 0)
                    return 0;
                else
                    return al[0];
            }
        }

        public static double _mean(List<double> al)
        {
            double mean = 0;

            for (int i = 0; i < al.Count; i++)
                mean += al[i];

            mean = mean / (double)al.Count;

            return mean;
        }

        public static double _stdev(List<double> al)
        {
            double stdev = 0;
            double mean = _mean(al);

            for (int i = 0; i < al.Count; i++)
                stdev += Math.Pow((double)al[i] - mean, 2);

            stdev = Math.Sqrt(stdev / ((double)(al.Count)));

            return stdev;
        }

        public static double _covar(List<double> al1, List<double> al2)
        {
            double mean1 = _mean(al1);
            double mean2 = _mean(al2);
            double ret = 0;

            for (int i = 0; i < al1.Count; i++)
                ret += (al1[i] - mean1) * (al2[i] - mean2);

            ret = ret / (double)al1.Count;

            return ret;
        }

        public static double _correlation_coefficient(List<double> al1, List<double> al2)
        {
            //egalizam numarul de elemente in fiecare vector in jurul centrului
            List<double> v1 = new List<double>();
            List<double> v2 = new List<double>();

            if (al1.Count != al2.Count)
            {
                int dif = (int)Math.Round((double)(Math.Abs(al1.Count - al2.Count) / 2));

                if (al1.Count > al2.Count)
                {
                    for (int i = 0; i < al2.Count; i++)
                    {
                        v2.Add(al2[i]);
                        v1.Add(al1[i + dif]);
                    }
                }
                else
                {
                    for (int i = 0; i < al1.Count; i++)
                    {
                        v1.Add(al1[i]);
                        v2.Add(al2[i + dif]);
                    }
                }
            }
            else
                for (int i = 0; i < al1.Count; i++)
                {
                    v1.Add(al1[i]);
                    v2.Add(al2[i]);
                }
            //egalizare incheiata

            //calculam meanurile celor 2 distributii
            double mean1 = 0, mean2 = 0;

            for (int i = 0; i < v1.Count; i++)
            {
                mean1 += v1[i];
                mean2 += v2[i];
            }

            mean1 = mean1 / (double)v1.Count;
            mean2 = mean2 / (double)v2.Count;
            //am calculat

            //calculam numaratorul din formula cc
            double numarator = 0;

            for (int i = 0; i < v1.Count; i++)
            {
                numarator += (v1[i] - mean1) * (v2[i] - mean2);
            }
            //am calculat

            //calculam si numitorul care e radicalul produsului a doua sume
            double suma1 = 0, suma2 = 0;

            for (int i = 0; i < v1.Count; i++)
            {
                suma1 += Math.Pow((v1[i] - mean1), 2);
                suma2 += Math.Pow((v2[i] - mean2), 2);
            }

            double numitor = Math.Sqrt(suma1 * suma2);
            //am calculat

            if (numitor == 0)
                return 0;

            double cc = numarator / numitor;
            return cc;
        }

        public static List<double> _z_distribution(List<double> al)
        {
            List<double> ret = new List<double>();

            double mean = _mean(al);
            double stdev = _stdev(al);

            for (int i = 0; i < al.Count; i++)
            {
                if (stdev != 0)
                {
                    double nr = (al[i] - mean) / stdev;
                    ret.Add(nr);
                }
                else
                {
                    ret.Add(0);
                }
            }

            return ret;
        }

        public static List<double> _percent_distribution(List<double> al)
        {
            List<double> ret = new List<double>();

            double max = _maximum(al);
            double min = _minimum(al);

            for (int i = 0; i < al.Count; i++)
                ret.Add((al[i] - min) / (max - min));

            return ret;
        }

        public static List<double> _distribution_selection(List<double> al, int nr_steps)
        {
            List<int> indexes = new List<int>();
            _get_DS_indexes(0, al.Count - 1, nr_steps, ref indexes);

            List<double> ret = new List<double>();

            indexes.Sort();

            //if (!indexes.Contains(0))
            //  ret.Add(al[0]);

            for (int i = 0; i < indexes.Count; i++)
                ret.Add(al[indexes[i]]);

            //if (!indexes.Contains(al.Count - 1))
            //  ret.Add(al[al.Count - 1]);

            return ret;
        }

        private static void _get_DS_indexes(int min, int max, int nr_steps, ref List<int> indexes)
        {
            if (nr_steps > 0)
            {
                int diff = max - min;
                int point = min + diff / 2;

                indexes.Add(point);
                _get_DS_indexes(min, point - 1, nr_steps - 1, ref indexes);
                _get_DS_indexes(point + 1, max, nr_steps - 1, ref indexes);
            }
        }

        public static int _get_DS_nr_steps(int x)
        {
            double ret = Math.Log((double)x + 1.0, 2) - 1;

            return (int)Math.Round(ret);
        }

        public static List<int> _get_extrems(List<double> al)
        {
            List<int> ret = new List<int>();

            double max = 0;
            double min = 0;

            for (int i = 0; i < al.Count; i++)
            {
                if (max < al[i])
                    max = al[i];

                if (min > al[i])
                    min = al[i];
            }

            double dist = max - min;
            double interv1 = dist * 35 / 10.0;
            double interv2 = dist * 1 / 10.0;

            for (int i = 0; i < al.Count; i++)
            {
                if (al[i] > max - interv1 || al[i] < min + interv2)
                    ret.Add(i);

                //if (al[i] < min * 3 / 6.0 && al[i] > min * 3.5 / 6.0)
                //  ret.Add(i);
            }

            return ret;
        }

        public static List<int> _get_extrem_points(List<double> al)
        {
            List<int> ret = new List<int>();

            for (int i = 2; i < al.Count - 2; i++)
                if ((al[i - 2] < al[i] && al[i - 1] < al[i] && al[i + 1] < al[i] && al[i + 2] < al[i]) ||
                  (al[i - 2] > al[i] && al[i - 1] > al[i] && al[i + 1] > al[i] && al[i + 2] > al[i]))
                    ret.Add(i);

            return ret;
        }

        public static List<double> _eliminate_abberations(List<double> al)
        {
            if (al.Count == 1 || al.Count == 0)
                return al;
            else
            {
                List<double> ret = new List<double>();
                double mean = _mean(al);
                double stdev = _stdev(al);

                double min = mean - 1.5 * stdev;
                double max = mean + 1.5 * stdev;

                for (int i = 0; i < al.Count; i++)
                    if ((double)al[i] <= max && (double)al[i] >= min)
                        ret.Add((double)al[i]);

                return ret;
            }
        }
    }
}
