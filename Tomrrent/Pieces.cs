using System.Security.Cryptography;

namespace Tomrrent
{
    class Piece
    {
        public int Index { get; private set; }
        public int Size { get; private set; }
        public int BlockSize { get; private set; }
        public byte[] Hash { get; private set; }
        public bool[] IsBlockAcquired { get; private set; }
        public bool IsVerified { get; private set; }

        private Torrent ParentTorrent;
        private static SHA1 sha1 = SHA1.Create();

        public Piece(int index, int size, int blockSize, Torrent parent, byte[] hash = null)
        {
            Index = index;
            Size = size;
            BlockSize = blockSize;
            ParentTorrent = parent;

            int blockCount = GetBlockCount();
            IsBlockAcquired = new bool[blockCount];

            if (hash == null)
            {
                Hash = GetHash();
            }
            else
            {
                Hash = hash;
            }
            Verify(index);
        }
        

        public int GetPieceSize(int piece)
        {
            return Size;
        }
        public int GetBlockCount()
        {
            return (int)Math.Ceiling(Size / (double)BlockSize);
        }

        public byte[] GetHash()
        {
            byte[] data = new byte[Size];
            return sha1.ComputeHash(data);
        }

        public void Verify(int piece)
        {
            byte[] hash = GetHash();
            if(hash != null && hash.SequenceEqual(ParentTorrent.PieceHashes[piece]))
            IsVerified = true;
        }

        
    }
}