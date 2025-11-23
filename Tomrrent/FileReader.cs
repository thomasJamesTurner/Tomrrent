using System.Data.SqlTypes;

namespace Tomrrent
{
    public class FileHandler
    {
        public byte[]? Read(Torrent parentTorrent, long start, int length)
        {
            long end = start + length;
            byte[] buffer = new byte[length];
            List<FileItem> files = parentTorrent.Files;
            for (int i = 0; i < files.Count; i++)
            {
                if ((start < files[i].Offset && end < files[i].Offset) ||
                    (start > files[i].Offset + files[i].Size && end > files[i].Offset + files[i].Size))
                    continue;
                string filePath = parentTorrent.DownloadDirectory +
                                    Path.DirectorySeparatorChar +
                                    parentTorrent.FileDirectory +
                                    parentTorrent.Files[i].Path;

                if (!File.Exists(filePath))
                {
                    return null;
                }
                long fstart = Math.Max(0, start - parentTorrent.Files[i].Offset);
                long fend = Math.Min(end - parentTorrent.Files[i].Offset, parentTorrent.Files[i].Size);
                int flength = Convert.ToInt32(fend - fstart);
                int bstart = Math.Max(0, Convert.ToInt32(parentTorrent.Files[i].Offset - start));

                using (Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(fstart, SeekOrigin.Begin);
                    stream.ReadExactly(buffer, bstart, flength);
                }
            }
            return buffer;
        }
        public void Write(Torrent parentTorrent, long start, byte[] bytes)
        {
            long end = start + bytes.Length;
            List<FileItem> files = parentTorrent.Files;
            for (int i = 0; i < files.Count; i++)
            {
                if ((start < files[i].Offset && end < files[i].Offset) ||
                    (start > files[i].Offset + files[i].Size && end > files[i].Offset + files[i].Size))
                    continue;
                string filePath = parentTorrent.DownloadDirectory +
                                    Path.DirectorySeparatorChar +
                                    parentTorrent.FileDirectory +
                                    parentTorrent.Files[i].Path;

                if (!File.Exists(filePath))
                {
                    return;
                }
                long fstart = Math.Max(0, start - parentTorrent.Files[i].Offset);
                long fend = Math.Min(end - parentTorrent.Files[i].Offset, parentTorrent.Files[i].Size);
                int flength = Convert.ToInt32(fend - fstart);
                int bstart = Math.Max(0, Convert.ToInt32(parentTorrent.Files[i].Offset - start));

                using (Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(fstart, SeekOrigin.Begin);
                    stream.Write(bytes, bstart, flength);
                }
            }
        } 
#pragma warning disable CS8603 // Possible null reference return.
        public byte[] ReadPiece(Torrent parentTorrent,int piece)
        {

            return Read(parentTorrent, piece * parentTorrent.PieceSize, parentTorrent.Pieces[piece].GetPieceSize(piece));
        }

        public byte[] ReadBlock(Torrent parentTorrent,int piece, int offset, int length)
        {

            return Read(parentTorrent, (piece * parentTorrent.PieceSize) + offset, length);

        }
#pragma warning restore CS8603 // Possible null reference return.
        public void WriteBlock(Torrent parentTorrent, int piece, int block, byte[] bytes)
        {
            Write(parentTorrent, piece * parentTorrent.PieceSize + block * parentTorrent.BlockSize, bytes);
            parentTorrent.Pieces[piece].IsBlockAcquired[block] = true;
            parentTorrent.Pieces[piece].Verify(piece);
        }

        public static Torrent LoadFromFile(string filePath, string downloadPath)
        {
            object obj = Encoder.DecodeFile(filePath);
            string name = Path.GetFileNameWithoutExtension(filePath);

            return Encoder.EncodingObjectToTorrent(obj, name, downloadPath);
        }
        
        public static void SaveToFile(Torrent torrent)
        {
            object obj = Encoder.TorrentToEncodingObject(torrent);
        
            Encoder.EncodeToFile(obj, torrent.Name + ".torrent");
        }
    }
}