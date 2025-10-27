using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;

namespace Tomrrent
{
    class Torrent
    {
        public string Name { get; private set; }
        public bool? IsPrivate { get; private set; }
        public List<FileItem> Files { get; private set; } = new List<FileItem>();
        public string FileDirectory { get { return (Files.Count > 1 ? Name + Path.DirectorySeparatorChar : ""); } }
        public string DownloadDirectory { get; private set; }

        public List<Tracker> Trackers {get; } = new List<Tracker>();
        public string Comment { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public Encoding Encoding { get; set; }

        public int BlockSize { get; private set; }
        public int PieceSize { get; private set; }

        public byte[][] PieceHashes { get; private set; }
        public byte[] Infohash { get; private set; } = new byte[20];
        public string HexStringInfohash { get { return String.Join("", this.Infohash.Select(x => x.ToString("x2"))); } }
        public string UrlSafeStringInfohash { get { return Encoding.UTF8.GetString(WebUtility.UrlEncodeToBytes(this.Infohash, 0, 20)); } }

    }
}