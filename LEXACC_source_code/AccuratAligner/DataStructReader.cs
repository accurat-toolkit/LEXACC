using System;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace DataStructUtils
{
    public static class DataStructReader
    {
        public delegate string keyDelegate(string key);
        public delegate string valDelegate(string value);

        public static Dictionary<string, string> readDictionary(string fileName, Encoding encoding, int keyIndex, int valueIndex, char separator, bool toLower, keyDelegate keyDelegate, valDelegate valDelegate)
        {
            Dictionary<string, string> ret = new Dictionary<string, string>();
            StreamReader rdr = new StreamReader(fileName, encoding);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                if (toLower)
                {
                    line = line.ToLower();
                }
                string[] parts = line.Trim().Split(separator);

                if (keyIndex < parts.Length && valueIndex < parts.Length)
                {
                    string key = parts[keyIndex];
                    string value = parts[valueIndex];

                    if (keyDelegate != null)
                    {
                        key = keyDelegate(key);
                    }
                    if (valDelegate != null)
                    {
                        value = valDelegate(value);
                    }


                    if (!ret.ContainsKey(key))
                    {
                        ret.Add(key, value);
                    }
                }
            }
            rdr.Close();
            return ret;
        }

        public static Dictionary<string, List<string>> readDictionary(string fileName, Encoding encoding, char separator_1, char separator_2, bool toLower, bool uniqueValues, keyDelegate keyDelegate, valDelegate valDelegate)
        {
            Dictionary<string, List<string>> ret = new Dictionary<string, List<string>>();

            StreamReader rdr = new StreamReader(fileName, encoding);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                line = line.Trim();
                if (toLower)
                {
                    line = line.ToLower();
                }

                string[] parts = line.Split(new char[] { separator_1 }, 2);

                if (parts.Length == 2)
                {
                    string key = parts[0];
                    if (keyDelegate != null)
                    {
                        key = keyDelegate(key);
                    }

                    string[] vParts = parts[1].Split(separator_2);
                    List<string> values = new List<string>();
                    for (int i = 0; i < vParts.Length; i++)
                    {
                        string value = vParts[i];
                        if (valDelegate != null)
                        {
                            value = valDelegate(value);
                        }

                        if (!uniqueValues || !values.Contains(value))
                        {
                            values.Add(value);
                        }
                    }

                    ret.Add(key, values);
                }
            }
            return ret;
        }

        public static HashSet<string> readHashSet(string fileName, Encoding encoding, int keyIndex, char separator, bool toLower, keyDelegate keyDelegate)
        {
            HashSet<string> ret = new HashSet<string>();
            StreamReader rdr = new StreamReader(fileName, encoding);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                if (toLower)
                {
                    line = line.ToLower();
                }
                string[] parts = line.Trim().Split(separator);

                if (keyIndex < parts.Length)
                {
                    string key = parts[keyIndex];

                    if (keyDelegate != null)
                    {
                        key = keyDelegate(key);
                    }
                    ret.Add(key);
                }
            }
            rdr.Close();
            return ret;
        }

        public static string readWholeTextFile(string fileName, Encoding encoding)
        {
            StringBuilder sb = new StringBuilder();
            StreamReader rdr = new StreamReader(fileName, encoding);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                sb.AppendLine(line);
            }
            rdr.Close();

            return sb.ToString().Trim();
        }
    }
}
