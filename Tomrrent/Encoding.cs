using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace Tomrrent
{
    public class Encoder
    {
        private const byte DictionaryStart = (byte)'d'; // 100
        private const byte DictionaryEnd = (byte)'e'; // 101
        private const byte ListStart = (byte)'l'; // 108
        private const byte ListEnd = (byte)'e'; // 101
        private const byte NumberStart = (byte)'i'; // 105
        private const byte NumberEnd = (byte)'e'; // 101
        private const byte ByteArrayDivider = (byte)':'; //  58


        public static object Decode(byte[] bytes)
        {
            IEnumerator<byte> enumerator = ((IEnumerable<byte>)bytes).GetEnumerator();
            enumerator.MoveNext();
            return DecodeNext(enumerator);
        }

        private static object DecodeNext(IEnumerator<byte> enumerator)
        {
            switch (enumerator.Current)
            {
                case DictionaryStart:
                    return DecodeDictionary(enumerator);
                case ListStart:
                    return DecodeList(enumerator);
                case NumberStart:
                    return DecodeNumber(enumerator);
                default:
                    return DecodeByteArray(enumerator);
            }
        }
        public static object DecodeFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("unable to find file: " + path);

            byte[] bytes = File.ReadAllBytes(path);

            return Decode(bytes);
        }
        public static byte[] Encode(object obj)
        {
            MemoryStream buffer = new MemoryStream();

            EncodeNext(buffer, obj);

            return buffer.ToArray();
        }
        private static void EncodeNext(MemoryStream buffer, object obj)
        {
            if (obj is byte[])
                EncodeByteArray(buffer, (byte[])obj);
            else if (obj is string)
                EncodeByteArray(buffer, Encoding.UTF8.GetBytes((string)obj));
            else if (obj is long)
                EncodeNumber(buffer, (long)obj);
            else if (obj.GetType() == typeof(List<object>))
                EncodeList(buffer, (List<object>)obj);
            else if (obj.GetType() == typeof(Dictionary<string, object>))
                EncodeDictionary(buffer, (Dictionary<string, object>)obj);
            else
                throw new Exception("unable to encode type " + obj.GetType());
        }


        public static void EncodeToFile(object obj, string path)
        {
            File.WriteAllBytes(path, Encode(obj));
        }

        // Numbers
        private static void EncodeNumber(MemoryStream buffer,long number)
        {
            buffer.Append(NumberStart);
            buffer.Append(Encoding.UTF8.GetBytes(Convert.ToString(number)));
            buffer.Append(NumberEnd);
        }
        private static long DecodeNumber(IEnumerator<byte> enumerator)
        {
            List<byte> bytes = new List<byte>();

            while (enumerator.MoveNext())
            {
                if (enumerator.Current == NumberEnd)
                { break; }
                bytes.Add(enumerator.Current);
            }
            return BitConverter.ToInt64(bytes.ToArray<byte>());
        }

        // Byte arrays
        private static void EncodeByteArray(MemoryStream buffer, byte[] bytes)
        {
            buffer.Append(Encoding.UTF8.GetBytes(Convert.ToString(bytes.Length)));
            buffer.Append(ByteArrayDivider);
            buffer.Append(bytes);
        }
        private static byte[] DecodeByteArray(IEnumerator<byte> enumerator)
        {
            List<byte> lengthBytes = new List<byte>();

            // first character will never be ByteArrayDivider
            do
            {
                if (enumerator.Current == ByteArrayDivider)
                    break;

                lengthBytes.Add(enumerator.Current);
            }
            while (enumerator.MoveNext());

            string lengthString = System.Text.Encoding.UTF8.GetString(lengthBytes.ToArray());

            int length;

            if (!Int32.TryParse(lengthString, out length))
                throw new Exception("unable to parse length of byte array");

            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                enumerator.MoveNext();
                bytes[i] = enumerator.Current;
            }
            return bytes;
        }

        // Lists
        private static void EncodeList(MemoryStream buffer,List<object> list)
        {
            buffer.Append(ListStart);
            foreach (var item in list)
            { EncodeNext(buffer, item); }
            buffer.Append(ListEnd);
        }
        private static List<object> DecodeList(IEnumerator<byte> enumerator)
        {
            List<object> list = new List<object>();

            while (enumerator.MoveNext())
            {
                if (enumerator.Current == ListEnd)
                { break; }
                list.Add(enumerator.Current);
            }
            return list;
        }

        private static void EncodeDictionary(MemoryStream buffer,Dictionary<string,object> dictionary)
        {
            buffer.Append(DictionaryStart);
            var sortedKeys = dictionary.Keys.ToList().OrderBy(static x => BitConverter.ToString(Encoding.UTF8.GetBytes(x)));

            foreach (var key in sortedKeys)
            {
                EncodeByteArray(buffer, Encoding.UTF8.GetBytes(key));
                EncodeNext(buffer, dictionary[key]);
            }
            buffer.Append(DictionaryEnd);
    
        }
        private static Dictionary<string,object> DecodeDictionary(IEnumerator<byte> enumerator)
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object>();
            List<string> keys = new List<string>();

            while (enumerator.MoveNext())
            {
                if (enumerator.Current == DictionaryEnd)
                { break; }
                string key = Encoding.UTF8.GetString(DecodeByteArray(enumerator));
                enumerator.MoveNext();
                object val = DecodeNext(enumerator);

                keys.Add(key);
                dictionary.Add(key, val);
            }
            var sortedKeys = keys.OrderBy(static x => BitConverter.ToString(Encoding.UTF8.GetBytes(x)));
            if (!keys.SequenceEqual(sortedKeys))
                throw new Exception("error loading dictionary: keys not sorted");
    
            return dictionary;
        }
    }
}