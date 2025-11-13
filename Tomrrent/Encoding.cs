using System;
using System.Text;
using System.Linq;
using System.Collections;
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
            
            return BitConverter.EndianBitConverter.BigEndian.ToInt64(bytes.ToArray<byte>(),0);
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
            var sortedKeys = dictionary.Keys.ToList().OrderBy(static x => Encoding.UTF8.GetBytes(x));

            foreach (var key in sortedKeys)
            {
                EncodeByteArray(buffer, Encoding.UTF8.GetBytes(key));
                EncodeNext(buffer, dictionary[key]);
            }
            buffer.Append(DictionaryEnd);
    
        }
        private static Dictionary<string, object> DecodeDictionary(IEnumerator<byte> enumerator)
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
            var sortedKeys = keys.OrderBy(static x => Encoding.UTF8.GetBytes(x));
            if (!keys.SequenceEqual(sortedKeys))
                throw new Exception("error loading dictionary: keys not sorted");

            return dictionary;
        }
        // ----peer functions----

        public static byte[] EncodeHandshake(byte[] hash, string id)
        {
            byte[] message = new byte[68];
            message[0] = 19;
            Buffer.BlockCopy(Encoding.UTF8.GetBytes("BitTorrent protocol"), 0, message, 1, 19);
            Buffer.BlockCopy(hash, 0, message, 28, 20);
            Buffer.BlockCopy(Encoding.UTF8.GetBytes(id), 0, message, 48, 20);

            return message;
        }
        public static bool DecodeHandshake(byte[] bytes, out byte[] hash, out string id)
        {
            hash = new byte[20];
            id = "";

            if (bytes.Length != 68 || bytes[0] != 19)
            {
                Console.WriteLine("invalid handshake, must be of length 68 and first byte must equal 19");
                return false;
            }

            if (Encoding.UTF8.GetString(bytes.Skip(1).Take(19).ToArray()) != "BitTorrent protocol")
            {
                Console.WriteLine("invalid handshake, protocol must equal \"BitTorrent protocol\"");
                return false;
            }

            // flags
            //byte[] flags = bytes.Skip(20).Take(8).ToArray();

            hash = bytes.Skip(28).Take(20).ToArray();

            id = Encoding.UTF8.GetString(bytes.Skip(48).Take(20).ToArray());

            return true;
        }

        public static byte[] EncodeKeepAlive()
        {
            return BitConverter.EndianBitConverter.BigEndian.GetBytes(0);
        }
        public static bool DecodeKeepAlive(byte[] bytes)
        {            
            if (bytes.Length != 4 || BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes,0) != 0 )
            {
                Console.WriteLine("invalid keep alive");
                return false;
            }
            return true;
        }

        //four state encoders/decoders
        public static byte[] EncodeState(MessageType messageType)
        {
            byte[] message = new byte[5];
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(1), 0, message, 0, 4);
            message[4] = (byte)messageType;
            return message;
        }

        public static bool DecodeState(byte[] bytes, MessageType type)
        {            
            if (bytes.Length != 5 || BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 0) != 1 || bytes[4] != (byte)type)
            {
                Console.WriteLine("invalid " + Enum.GetName(typeof(MessageType), type));
                return false;
            }
            return true;
        }

        public static byte[] EncodeChoke()
        {
            return EncodeState(MessageType.Choke);
        }
        public static bool DecodeChoke(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.Choke);
        }

        public static byte[] EncodeUnchoke()
        {
            return EncodeState(MessageType.Unchoke);
        }
        public static bool DecodeUnchoke(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.Unchoke);
        }

        public static byte[] EncodeInterested()
        {
            return EncodeState(MessageType.Interested);
        }
        public static bool DecodeInterested(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.Interested);
        }

        public static byte[] EncodeNotInterested()
        {
            return EncodeState(MessageType.NotInterested);
        }
        public static bool DecodeNotInterested(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.NotInterested);
        }

        public static byte[] EncodeHave(int index)
        {
            byte[] message = new byte[9];
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(5), 0, message, 0, 4);
            message[4] = (byte)MessageType.Have;
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(index), 0, message, 5, 4);

            return message;
        }
        public static bool DecodeHave(byte[] bytes, out int index)
        {
            index = -1;

            if (bytes.Length != 9 || BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 0) != 5)
            {
                Console.WriteLine("invalid have, first byte must equal 0x2");
                return false;
            }

            index = BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 5);

            return true;
        }

        public static byte[] EncodeBitfield(bool[] isPieceDownloaded)
        {
            int numPieces = isPieceDownloaded.Length;
            int numBytes = Convert.ToInt32(Math.Ceiling(numPieces / 8.0));
            int numBits = numBytes * 8;

            int length = numBytes + 1;

            byte[] message = new byte[length + 4];
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(length), 0, message, 0, 4);
            message[4] = (byte)MessageType.Bitfield;

            bool[] downloaded = new bool[numBits];
            for (int i = 0; i < numPieces; i++)
                downloaded[i] = isPieceDownloaded[i];

            BitArray bitfield = new BitArray(downloaded);
            BitArray reversed = new BitArray(numBits);
            for (int i = 0; i < numBits; i++)
                reversed[i] = bitfield[numBits - i - 1];

            reversed.CopyTo(message, 5);

            return message;
        }
        public static bool DecodeBitfield(byte[] bytes, int pieces, out bool[] isPieceDownloaded)
        {
            isPieceDownloaded = new bool[pieces];

            int expectedLength = Convert.ToInt32(Math.Ceiling(pieces / 8.0)) + 1;

            if (bytes.Length != expectedLength + 4 || BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 0) != expectedLength)
            {
                Console.WriteLine("invalid bitfield, first byte must equal " + expectedLength);
                return false;
            }

            BitArray bitfield = new BitArray(bytes.Skip(5).ToArray());

            for (int i = 0; i < pieces; i++)
                isPieceDownloaded[i] = bitfield[bitfield.Length - 1 - i];

            return true;
        }

        public static byte[] EncodeRequest(int index, int begin, int length) 
        {
            byte[] message = new byte[17];
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(13), 0, message, 0, 4);
            message[4] = (byte)MessageType.Request;
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(index), 0, message, 5, 4);
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(begin), 0, message, 9, 4);
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(length), 0, message, 13, 4);

            return message;
        }
        public static bool DecodeRequest(byte[] bytes, out int index, out int begin, out int length)
        {
            index = -1;
            begin = -1;
            length = -1;

            if (bytes.Length != 17 ||  BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes,0) != 13)
            {
                Console.WriteLine("invalid request message, must be of length 17");
                return false;
            }

            index =  BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 5);
            begin =  BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 9);
            length =  BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 13);

            return true;
        }
        public static byte[] EncodePiece(int index, int begin, byte[] data)
        {
            int length = data.Length + 9;

            byte[] message = new byte[length + 4];
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(length), 0, message, 0, 4);
            message[4] = (byte)MessageType.Piece;
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(index), 0, message, 5, 4);
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(begin), 0, message, 9, 4);
            Buffer.BlockCopy(data, 0, message, 13, data.Length);

            return message;
        }  
        public static bool DecodePiece(byte[] bytes, out int index, out int begin, out byte[] data)
        {
            index = -1;
            begin = -1;
            data = new byte[0];

            if (bytes.Length < 13)
            {
                Console.WriteLine("invalid piece message");
                return false;
            }

            int length = BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 0) - 9;
            index = BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 5);
            begin = BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 9);

            data = new byte[length];
            Buffer.BlockCopy(bytes, 13, data, 0, length);

            return true;
        }

        public static byte[] EncodeCancel(int index, int begin, int length)
        {
            byte[] message = new byte[17];
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(13), 0, message, 0, 4);
            message[4] = (byte)MessageType.Cancel;
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(index), 0, message, 5, 4);
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(begin), 0, message, 9, 4);
            Buffer.BlockCopy(BitConverter.EndianBitConverter.BigEndian.GetBytes(length), 0, message, 13, 4);

            return message;
        }
        public static bool DecodeCancel(byte[] bytes, out int index, out int begin, out int length)
        {
            index = -1;
            begin = -1;
            length = -1;

            if (bytes.Length != 17 || BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 0) != 13)
            {
                Console.WriteLine("invalid cancel message, must be of length 17");
                return false;
            }

            index = BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 5);
            begin = BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 9);
            length = BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 13);

            return true;
        }
        

        
        public static long DateTimeToUnixTimestamp(DateTime time)
        {
            return Convert.ToInt64((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds);
        }
        

        public static DateTime UnixTimeStampToDateTime( double unixTimeStamp )
        {
            System.DateTime dtDateTime = new DateTime(1970,1,1,0,0,0,0,System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds( unixTimeStamp ).ToLocalTime();
            return dtDateTime;
        }

        private static string DecodeUTF8String( object obj )
        {
            byte[]? bytes = obj as byte[];
        
            if (bytes == null)
                throw new Exception("unable to decode utf-8 string, object is not a byte array");
        
            return Encoding.UTF8.GetString(bytes);
        }
        public static object TorrentToEncodingObject(Torrent torrent)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            if (torrent.Trackers.Count == 1)
                dict["announce"] = Encoding.UTF8.GetBytes(torrent.Trackers[0].Address);
            else
                dict["announce"] = torrent.Trackers.Select(x => (object)Encoding.UTF8.GetBytes(x.Address)).ToList();
            dict["comment"] = Encoding.UTF8.GetBytes(torrent.Comment);
            dict["created by"] = Encoding.UTF8.GetBytes(torrent.CreatedBy);
            dict["creation date"] = DateTimeToUnixTimestamp(torrent.CreationDate);
            dict["encoding"] = Encoding.UTF8.GetBytes(Encoding.UTF8.WebName.ToUpper());
            dict["info"] = TorrentInfoToEncodingObject(torrent);

            return dict;

        }
        private static object TorrentInfoToEncodingObject(Torrent torrent)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();
            dict["piece length"] = (long)torrent.PieceSize;
            byte[] pieces = new byte[20 * torrent.PieceHashes.Length];
            for (int i = 0; i < torrent.PieceHashes.Length; i++)
                Buffer.BlockCopy(torrent.PieceHashes[i], 0, pieces, i * 20, 20);
            dict["pieces"] = pieces;

            if (torrent.IsPrivate.HasValue)
                dict["private"] = torrent.IsPrivate.Value ? 1L : 0L;

            if (torrent.Files.Count == 1)
            {                
                dict["name"] = Encoding.UTF8.GetBytes(torrent.Files[0].Path);
                dict["length"] = torrent.Files[0].Size;
            }
            else
            {
                List<object> files = new List<object>();

                foreach (var f in torrent.Files)
                {
                    Dictionary<string,object> fileDict = new Dictionary<string, object>();
                    fileDict["path"] = f.Path.Split(Path.DirectorySeparatorChar).Select(x => (object)Encoding.UTF8.GetBytes(x)).ToList();
                    fileDict["length"] = f.Size;
                    files.Add(fileDict);
                }

                dict["files"] = files;
                dict["name"] = Encoding.UTF8.GetBytes(torrent.FileDirectory.Substring(0, torrent.FileDirectory.Length - 1));
            }

            return dict;
        }
        public static Torrent EncodingObjectToTorrent(object encodingObject, string name, string downloadPath)
        {
            Dictionary<string, object> obj = (Dictionary<string, object>)encodingObject;
            if (obj == null)
            {
                throw new Exception("Not a Torrent file");
            }
            if(!obj.ContainsKey("info"))
            {
                throw new Exception("Torrent doesnt have info section");
            }
            List<string> trackers = new List<string>();
            if (obj.ContainsKey("announce"))
            {
                trackers.Add(DecodeUTF8String(obj["announce"]));
            }
            Dictionary<string,object> info = (Dictionary<string,object>)obj["info"];

            if (info == null)
            {
                throw new Exception("error");
            }
            List<FileItem> files = new List<FileItem>();

            if (info.ContainsKey("name") && info.ContainsKey("length"))
            {
                files.Add(new FileItem() {
                    Path = DecodeUTF8String(info["name"]),
                    Size = (long)info["length"]
                });
            }
            else if (info.ContainsKey("files"))
            {
                long running = 0;
        
                foreach (object item in (List<object>)info["files"])
                {
                    var dict = item as Dictionary<string,object>;
        
                    if (dict == null || !dict.ContainsKey("path") || !dict.ContainsKey("length") )
                        throw new Exception("error: incorrect file specification");
        
                    string path = String.Join(Path.DirectorySeparatorChar.ToString(), ((List<object>)dict["path"]).Select(x => DecodeUTF8String(x)));
        
                    long size = (long)dict["length"];
        
                    files.Add(new FileItem() {
                        Path = path,
                        Size = size,
                        Offset = running
                    });
        
                    running += size;
                }
            }
            else
            {
                throw new Exception("error: no files specified in torrent");
            }
                
            if (!info.ContainsKey("piece length"))
                throw new Exception("error");
            int pieceSize = Convert.ToInt32(info["piece length"]);
        
            if (!info.ContainsKey("pieces"))
                throw new Exception("error");            
            byte[] pieceHashes = (byte[])info["pieces"];
        
            bool? isPrivate = null;
            if (info.ContainsKey("private"))
                isPrivate = ((long)info["private"]) == 1L;            
            
            Torrent torrent = new Torrent(name, downloadPath, files, trackers, pieceSize, pieceHashes, 16384, isPrivate );
        
            if (obj.ContainsKey("comment"))
                torrent.Comment = DecodeUTF8String(obj["comment"]);
        
            if (obj.ContainsKey("created by"))
                torrent.CreatedBy = DecodeUTF8String(obj["created by"]);
        
            if (obj.ContainsKey("creation date"))
                torrent.CreationDate = UnixTimeStampToDateTime(Convert.ToDouble(obj["creation date"]));
        
            if (obj.ContainsKey("encoding"))
                torrent.Encoding = Encoding.GetEncoding(DecodeUTF8String(obj["encoding"]));
            
            return torrent;
        }
    }
}