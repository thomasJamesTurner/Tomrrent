using System.IO;
using Microsoft.AspNetCore.Http.Connections;

namespace Tomrrent
{
    class Piece
    {
        //initialise with file to be pieced
        public Torrent torrent { get; set; }
        public int PieceSize { get; set; }
        public int BlockSize { get; set; }
        public long TotalSize { get{ return torrent.Files.Sum(x => x.Size);} }


        public int PieceCount { get { return PieceHashes.Length; } }

        public byte[][] PieceHashes { get; private set; }
        public bool[] IsPieceVerified { get; private set; }
        public bool[][] IsBlockAcquired { get; private set; }
        public int VerifiedPieceCount { get { return IsPieceVerified.Count(x => x); } }
        public double VerifiedRatio { get { return VerifiedPieceCount / (double)PieceCount; } }
        public bool IsCompleted { get { return VerifiedPieceCount == PieceCount; } }
        public bool IsStarted { get { return VerifiedPieceCount > 0; } }

        public long Uploaded { get; set; } = 0;
        public long Downloaded { get { return PieceSize * VerifiedPieceCount; } } // !! incorrect
        public long Left { get { return TotalSize - Downloaded; } }

        public int GetPieceSize(int piece)
        {
            if (piece != PieceCount - 1)
            {
                return PieceSize;
            }
            int remains =Convert.ToInt32(TotalSize % PieceSize);
            return (remains == 0) ? PieceSize : remains;
        }
        public int GetBlockSize(int piece, int block)
        {
            if (block != GetBlockCount(piece) - 1)
            {
                return BlockSize;
            }
            int remains = Convert.ToInt32(GetPieceSize(piece) % PieceSize);
            return (remains == 0) ? 0 : remains;
        }
        public int GetBlockCount(int piece)
        {
            return Convert.ToInt32(Math.Ceiling(GetPieceSize(piece) / (double)BlockSize));
        }
    }
}