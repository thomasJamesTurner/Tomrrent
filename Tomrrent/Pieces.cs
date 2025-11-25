using System.Security.Cryptography;

namespace Tomrrent
{
    public class Piece
    {
        public int Index { get; private set; }
        public int Size { get; private set; }
        public int BlockSize { get; private set; }
        public byte[] Hash { get; private set; }
        public bool[] IsBlockAcquired { get; private set; }
        public bool IsVerified { get; private set; }
        public event EventHandler<int> PieceVerified;
        private Torrent ParentTorrent;
        private static SHA1 sha1 = SHA1.Create();

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
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
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

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

            if (Hash != null && Hash.SequenceEqual(ParentTorrent.PieceHashes[piece]))
            {
                IsVerified = true;
                for (int j = 0; j < IsBlockAcquired.Length; j++)
                    IsBlockAcquired[j] = true;
                if (PieceVerified != null)
                {
                    PieceVerified(this, piece);
                }
                return;
            }
            IsVerified = false;
            if (IsBlockAcquired.All(x => x))
            {
                for (int j = 0; j < IsBlockAcquired.Length; j++)
                    IsBlockAcquired[j] = false;
            }
        }

        
    }
}