using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Http.Connections;

namespace Tomrrent
{
    class Torrent
    {
        public string Name { get; private set; }
        public bool? IsPrivate { get; private set; }
        public List<FileItem> Files { get; private set; } = new List<FileItem>();
        public string FileDirectory { get{ return   (Files.Count > 1 ?
                                                        Name + Path.DirectorySeparatorChar :
                                                        ""
                                                    );} }
        public string DownloadDirectory { get; private set; }

        public List<Tracker> Trackers {get; } = new List<Tracker>();
        public string Comment;
        public string CreatedBy;
        public DateTime CreationDate;
        public Encoding Encoding;

        public int BlockSize { get; private set; }
        public int PieceSize { get; private set; }
        public byte[][] PieceHashes { get; private set; }
        public List<Piece> Pieces { get; } = new();
        public byte[] Infohash { get; private set; } = new byte[20];

        public long TotalSize { get { return Files.Sum(static x => x.Size); } }
        public long Downloaded => Pieces.Count(static p => p.IsVerified) * PieceSize;
        public long Left => TotalSize - Downloaded;
        public string HexStringInfohash => string.Join( "",
                                                        Infohash.Select(
                                                            static x => x.ToString("x2")
                                                            )
                                                        );
        public string UrlSafeStringInfohash => Encoding.UTF8.GetString(WebUtility.UrlEncodeToBytes(Infohash, 0, 20));
        public event EventHandler<List<IPEndPoint>> PeerList;

        public Torrent(string name, string address, List<FileItem> files, List<string> trackers, int pieceSize,byte[] pieceHashes = null, int blockSize = 16384, bool? isPrivate = false)
        {
            Name = name;
            DownloadDirectory = address;
            Files = files;
            PieceSize = pieceSize;
            BlockSize = blockSize;
            IsPrivate = isPrivate;

            if (trackers != null)
            {
                foreach (string url in trackers)
                {
                    var tracker = new Tracker(url);
                    Trackers.Add(tracker);
                    tracker.PeerListUpdated += PeerList;
                }
            }

            // Split into pieces
            int pieceCount = (int)Math.Ceiling(TotalSize / (double)PieceSize);
            PieceHashes = new byte[pieceCount][];
            if (pieceHashes == null)
            {
                //new torrent
                for (int i = 0; i < pieceCount; i++)
                {
                    Piece piece = new Piece(i, PieceSize, BlockSize, this);
                    PieceHashes[i] = piece.GetHash();
                    Pieces.Add(piece);
                }
                 

            }
            else
            {
                for (int i = 0; i < pieceCount; i++)
                {

                    PieceHashes[i] = new byte[20];
                    Piece piece = new Piece(i, PieceSize, BlockSize, this);
                    Buffer.BlockCopy(pieceHashes, i * 20, PieceHashes[i], 0, 20);
                    Pieces.Add(piece);
                    
                }
            }
            

            object info = TorrentInfoToEncodingObject(this);
            byte[] bytes = Encoder.Encode(info);
            Infohash = SHA1.Create().ComputeHash(bytes);
        }
        private static Dictionary<string,object> TorrentToEncodingObject(Torrent torrent)
        {
            Dictionary<string,object> dict = new Dictionary<string, object>();

            if( torrent.Trackers.Count == 1 )
                dict["announce"] = Encoding.UTF8.GetBytes(torrent.Trackers[0].Address);
            else
                dict["announce"] = torrent.Trackers.Select(static x => (object)Encoding.UTF8.GetBytes(x.Address)).ToList();
                dict["comment"] = Encoding.UTF8.GetBytes(torrent.Comment);
                dict["created by"] = Encoding.UTF8.GetBytes(torrent.CreatedBy);
                dict["creation date"] = DateTimeToUnixTimestamp(torrent.CreationDate);
                dict["encoding"] = Encoding.UTF8.GetBytes(Encoding.UTF8.WebName.ToUpper());
                dict["info"] = TorrentInfoToEncodingObject(torrent);

            return dict;
        }

        private static Dictionary<string, object> TorrentInfoToEncodingObject(Torrent torrent)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            dict["piece length"] = (long)torrent.PieceSize;
            byte[] pieces = new byte[20 * torrent.Pieces.Count];
            for (int i = 0; i < pieces.Length; i++)
                Buffer.BlockCopy(torrent.Pieces.Select(p => p.Hash[i]).ToArray(), 0, pieces, i * 20, 20);
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
                    Dictionary<string, object> fileDict = new Dictionary<string, object>();
                    fileDict["path"] = f.Path.Split(Path.DirectorySeparatorChar).Select(static x => (object)Encoding.UTF8.GetBytes(x)).ToList();
                    fileDict["length"] = f.Size;
                    files.Add(fileDict);
                }

                dict["files"] = files;
                dict["name"] = Encoding.UTF8.GetBytes(torrent.FileDirectory.Substring(0, torrent.FileDirectory.Length - 1));
            }

            return dict;
        }
        public static long DateTimeToUnixTimestamp( DateTime time )
        {
            return (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }
}