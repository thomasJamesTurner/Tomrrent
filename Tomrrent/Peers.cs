using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.IO;

namespace Tomrrent
{
    public enum MessageType : int
    {
        Unknown = -3,
        Handshake = -2,
        KeepAlive = -1,
        Choke = 0,
        Unchoke = 1,
        Interested = 2,
        NotInterested = 3,
        Have = 4,
        Bitfield = 5,
        Request = 6,
        Piece = 7,
        Cancel = 8,
        Port = 9,
    }
    public class Peer
    {
        public event EventHandler Disconnected;
        public event EventHandler StateChanged;
        public event EventHandler<DataRequest> BlockRequested;
        public event EventHandler<DataRequest> BlockCancelled;
        public event EventHandler<DataPackage> BlockReceived;

        public string LocalId { get; set; }
        public string Id { get; set; }

        public Torrent torrent { get; private set; }

        public IPEndPoint IPEndPoint { get; private set; }
        public string Key { get { return IPEndPoint.ToString(); } }

        private TcpClient TcpClient { get; set; }
        private NetworkStream stream { get; set; }
        private const int bufferSize = 256;
        private byte[] streamBuffer = new byte[bufferSize];
        private List<byte> data = new List<byte>();

        public bool[] IsPieceDownloaded = new bool[0];
        public string PiecesDownloaded { get { return String.Join("", IsPieceDownloaded.Select(x => Convert.ToInt32(x))); } }
        public int PiecesRequiredAvailable { get { return IsPieceDownloaded.Select((x, i) => x && !torrent.Pieces[i].IsVerified).Count(x => x); } }
        public int PiecesDownloadedCount { get { return IsPieceDownloaded.Count(x => x); } }
        public bool IsCompleted { get { return PiecesDownloadedCount == torrent.PieceHashes.Length; } }

        public bool IsDisconnected;

        public bool IsHandshakeSent;
        public bool IsPositionSent;
        public bool IsChokeSent = true;
        public bool IsInterestedSent = false;

        public bool IsHandshakeReceived;
        public bool IsChokeReceived = true;
        public bool IsInterestedReceived = false;

        public bool[][] IsBlockRequested = new bool[0][];
        public int BlocksRequested { get { return IsBlockRequested.Sum(x => x.Count(y => y)); } }

        public DateTime LastActive;
        public DateTime LastKeepAlive = DateTime.MinValue;

        public long Uploaded;
        public long Downloaded;


        public void Connect()
        {
            if (TcpClient == null)
            {
                TcpClient = new TcpClient();
                try
                {
                    TcpClient.Connect(IPEndPoint);
                }
                catch (Exception e)
                {
                    Disconnect();
                    return;
                }
            }

            Console.WriteLine(this, "connected");

            stream = TcpClient.GetStream();
            stream.BeginRead(streamBuffer, 0, Peer.bufferSize, new AsyncCallback(HandleRead), null);

            SendHandshake();
            if (IsHandshakeReceived)
                SendBitfield(torrent.IsPieceVerified);
        }
        public void Disconnect()
        {
            if (!IsDisconnected)
            {
                IsDisconnected = true;
                Console.WriteLine(this, "disconnected, down " + Downloaded + ", up " + Uploaded);
            }

            if (TcpClient != null)
                TcpClient.Close();

            if (Disconnected != null)
                Disconnected(this, new EventArgs());
        }

        private MessageType GetMessageType(byte[] bytes)
        {
            if (!IsHandshakeReceived)
                return MessageType.Handshake;

            if (bytes.Length == 4 && BitConverter.EndianBitConverter.BigEndian.ToInt32(bytes, 0) == 0)
                return MessageType.KeepAlive;

            if (bytes.Length > 4 && Enum.IsDefined(typeof(MessageType), (int)bytes[4]))
                return (MessageType)bytes[4];

            return MessageType.Unknown;
        }

        private void SendBytes(byte[] bytes)
        {
            try
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception e)
            {
                Disconnect();
            }
        }
        private void HandleRead( IAsyncResult ar )
        {            
            int bytes = 0;
            try
            {
                bytes = stream.EndRead(ar);
            }
            catch (Exception e)
            {
                Disconnect();
                return;
            }

            data.AddRange(streamBuffer.Take(bytes));

            int messageLength = GetMessageLength(data);
            while (data.Count >= messageLength)
            {
                HandleMessage(data.Take(messageLength).ToArray());
                data = data.Skip(messageLength).ToList();

                messageLength = GetMessageLength(data);
            }

            try
            {
                stream.BeginRead(streamBuffer, 0, Peer.bufferSize, new AsyncCallback(HandleRead), null);
            }
            catch (Exception e)
            {
                Disconnect();
            }
        }

        private int GetMessageLength(List<byte> data)
        {
            if (!IsHandshakeReceived)
                return 68;

            if (data.Count < 4)
                return int.MaxValue;

            return BitConverter.EndianBitConverter.BigEndian.ToInt32(data.ToArray(), 0) + 4;
        }
        private void SendHandshake()
        {
            if (IsHandshakeSent)
                return;

            Console.WriteLine(this, "-> handshake" );
            SendBytes(EncodeHandshake(Torrent.Infohash, LocalId));
            IsHandshakeSent = true;
        }

        public void SendKeepAlive()
        {
            if( LastKeepAlive > DateTime.UtcNow.AddSeconds(-30) )
                return;

            Console.WriteLine(this, "-> keep alive" );
            SendBytes(EncodeKeepAlive());
            LastKeepAlive = DateTime.UtcNow;
        }

        public void SendChoke() 
        {
            if (IsChokeSent)
                return;

            Console.WriteLine(this, "-> choke" );
            SendBytes(EncodeChoke());
            IsChokeSent = true;
        }

        public void SendUnchoke() 
        {
            if (!IsChokeSent)
                return;

            Console.WriteLine(this, "-> unchoke" );
            SendBytes(EncodeUnchoke());
            IsChokeSent = false;
        }

        public void SendInterested()
        {
            if (IsInterestedSent)
                return;

            Console.WriteLine(this, "-> interested");
            SendBytes(EncodeInterested());
            IsInterestedSent = true;
        }

        public void SendNotInterested() 
        {
            if (!IsInterestedSent)
                return;

            Console.WriteLine(this, "-> not interested");
            SendBytes(EncodeNotInterested());
            IsInterestedSent = false;
        }

        public void SendHave(int index) 
        {
            Console.WriteLine(this, "-> have " + index);
            SendBytes(EncodeHave(index));
        }

        public void SendBitfield(bool[] isPieceDownloaded) 
        {
            Console.WriteLine(this, "-> bitfield " + String.Join("", isPieceDownloaded.Select(x => x ? 1 : 0)));
            SendBytes(EncodeBitfield(isPieceDownloaded));
        }

        public void SendRequest(int index, int begin, int length) 
        {
            Console.WriteLine(this, "-> request " + index + ", " + begin + ", " + length);
            SendBytes(EncodeRequest(index, begin, length));
        }

        public void SendPiece(int index, int begin, byte[] data) 
        {
            Console.WriteLine(this, "-> piece " + index + ", " + begin + ", " + data.Length);
            SendBytes(EncodePiece(index, begin, data));
            Uploaded += data.Length;
        }

        public void SendCancel(int index, int begin, int length) 
        {
            Console.WriteLine(this, "-> cancel");
            SendBytes(EncodeCancel(index, begin, length));
        }
    }
}