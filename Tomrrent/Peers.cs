using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.WebSockets;

namespace Tomrrent
{
    public class DataRequest
    {
        public Peer Peer;
        public int Piece;
        public int Begin;
        public int Length;
        public bool IsCancelled;
    }

    public class DataPackage
    {
        public Peer Peer;
        public int Piece;
        public int Block;
        public byte[] Data;
    }
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
                    Console.WriteLine("Could not establish connection: " + e);
                    Disconnect();
                    return;
                }
            }

            Console.WriteLine(Id + "connected");

            stream = TcpClient.GetStream();
            stream.BeginRead(streamBuffer, 0, Peer.bufferSize, new AsyncCallback(HandleRead), null);

            SendHandshake();
            if (IsHandshakeReceived)
            {
                int pieceCount = torrent.PieceHashes.Length;
                bool[] verified = new bool[pieceCount];
                for(int i = 0; i<pieceCount;i++)
                {
                    verified[i] = torrent.Pieces[i].IsVerified;
                }
                SendBitfield(verified);
            }
                
        }
        public void Disconnect()
        {
            if (!IsDisconnected)
            {
                IsDisconnected = true;
                Console.WriteLine(Id, "disconnected, down " + Downloaded + ", up " + Uploaded);
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
                Console.WriteLine("Could not send Bytes: " + e);
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
                Console.WriteLine("Could not read handel: " + e);
                Disconnect();
                return;
            }

            data.AddRange(streamBuffer.Take(bytes));

            int messageLength = GetMessageLength(data);
            while (data.Count >= messageLength)
            {
                //temporarily disabling Handle message until
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
                Console.WriteLine("Could not read handel: " + e);
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

            Console.WriteLine(Id, "-> handshake" );
            SendBytes(Encoder.EncodeHandshake(torrent.Infohash, LocalId));
            IsHandshakeSent = true;
        }

        public void SendKeepAlive()
        {
            if( LastKeepAlive > DateTime.UtcNow.AddSeconds(-30) )
                return;

            Console.WriteLine(Id, "-> keep alive" );
            SendBytes(Encoder.EncodeKeepAlive());
            LastKeepAlive = DateTime.UtcNow;
        }

        public void SendChoke() 
        {
            if (IsChokeSent)
                return;

            Console.WriteLine(Id, "-> choke" );
            SendBytes(Encoder.EncodeChoke());
            IsChokeSent = true;
        }

        public void SendUnchoke() 
        {
            if (!IsChokeSent)
                return;

            Console.WriteLine(Id, "-> unchoke" );
            SendBytes(Encoder.EncodeUnchoke());
            IsChokeSent = false;
        }

        public void SendInterested()
        {
            if (IsInterestedSent)
                return;

            Console.WriteLine(Id, "-> interested");
            SendBytes(Encoder.EncodeInterested());
            IsInterestedSent = true;
        }

        public void SendNotInterested() 
        {
            if (!IsInterestedSent)
                return;

            Console.WriteLine(Id, "-> not interested");
            SendBytes(Encoder.EncodeNotInterested());
            IsInterestedSent = false;
        }

        public void SendHave(int index) 
        {
            Console.WriteLine(Id, "-> have " + index);
            SendBytes(Encoder.EncodeHave(index));
        }

        public void SendBitfield(bool[] isPieceDownloaded) 
        {
            Console.WriteLine(Id, "-> bitfield " + String.Join("", isPieceDownloaded.Select(x => x ? 1 : 0)));
            SendBytes(Encoder.EncodeBitfield(isPieceDownloaded));
        }

        public void SendRequest(int index, int begin, int length) 
        {
            Console.WriteLine(Id, "-> request " + index + ", " + begin + ", " + length);
            SendBytes(Encoder.EncodeRequest(index, begin, length));
        }

        public void SendPiece(int index, int begin, byte[] data) 
        {
            Console.WriteLine(Id, "-> piece " + index + ", " + begin + ", " + data.Length);
            SendBytes(Encoder.EncodePiece(index, begin, data));
            Uploaded += data.Length;
        }

        public void SendCancel(int index, int begin, int length)
        {
            Console.WriteLine(Id, "-> cancel");
            SendBytes(Encoder.EncodeCancel(index, begin, length));
        }

        private void HandleMessage(byte[] message)
        {
            LastActive = DateTime.Now;
            MessageType type = GetMessageType(message);

            switch(type)
            {
                case(MessageType.Unknown):
                    return;
                case(MessageType.Handshake):
                    byte[] hash;
                    string id;
                    if(Encoder.DecodeHandshake(message,out hash,out id))
                    {
                        HandleHadshake(hash,id);
                    }
                    break;
                case(MessageType.KeepAlive):
                    if(Encoder.DecodeKeepAlive(message))
                    {
                        HandleKeepAlive();
                    }
                    break;
                case(MessageType.Choke):
                    if(Encoder.DecodeChoke(message))
                    {
                        HandleChoke();
                    }
                    break;
                case(MessageType.Unchoke):
                    if(Encoder.DecodeUnchoke(message))
                    {
                        HandleUnchoke();
                    }
                    break;
                case(MessageType.Interested):
                    if(Encoder.DecodeInterested(message))
                    {
                        HandleInterested();
                    }
                    break;
                case(MessageType.NotInterested):
                    if(Encoder.DecodeNotInterested(message))
                    {
                        HandleNotInterested();
                    }
                    break;
                case(MessageType.Have):
                    int haveIndex;
                    if(Encoder.DecodeHave(message,out haveIndex))
                    {
                        HandleHave(haveIndex);
                    }
                    break;
                case(MessageType.Request):
                    int requestIndex;
                    int requestBegin;
                    int requestLength;
                    if(Encoder.DecodeRequest(message,out requestIndex,out requestBegin,out requestLength))
                    {
                        HandleRequest(requestIndex,requestBegin,requestLength);
                    }
                    break;
                case(MessageType.Piece):
                    int pieceIndex;
                    int pieceBegin;
                    byte[] pieceData;
                    if(Encoder.DecodePiece(message,out pieceIndex,out pieceBegin,out pieceData))
                    {
                        HandlePiece(pieceIndex,pieceBegin,pieceData);
                    }
                    break;
                case(MessageType.Cancel):
                    int cancelIndex;
                    int cancelBegin;
                    int cancelLength;
                    if(Encoder.DecodeCancel(message,out cancelIndex,out cancelBegin,out cancelLength))
                    {
                        HandleCancel(cancelIndex,cancelBegin,cancelLength);
                    }
                    break;
                case(MessageType.Port):
                    Console.WriteLine("port: " + string.Join("",message.Select(x=>x.ToString("x2"))));
                    break;
            }
            Console.WriteLine("Unhandled Incomming Message: "+ string.Join("",message.Select(x=>x.ToString("x2"))));
            Disconnect();
        }
        private void HandleHadshake(byte[] hash,string id)
        {
            if(!torrent.Infohash.SequenceEqual(hash))
            {
                Console.WriteLine("Hash does not match torrent"
                                    +"\nExpected: "
                                    +string.Join("",torrent.Infohash.Select(x=>x.ToString("x2")))
                                    +"\nRecieved: "
                                    +string.Join("",hash.Select(x=>x.ToString("x2")))
                                    +"\nterminating connection");
                Disconnect();
                return;
            }
            Id = id;
            IsHandshakeReceived = true;
            bool[] verified = new bool[torrent.Infohash.Length];
            for(int i=0;i<torrent.Infohash.Length;i++)
            {
                verified[i] = torrent.Pieces[i].IsVerified;
            }
            SendBitfield(verified);
        }

        private void HandleKeepAlive()
        {
            Console.WriteLine("Keep connection alive");
        }
        private void HandleChoke()
        {
            IsChokeReceived = true;
            var handler = StateChanged;
            if (handler != null)
            {
                Console.WriteLine("Choke connection");
                handler(this, new EventArgs());
            }
        }
        private void HandleUnchoke()
        {
            IsChokeReceived = false;
            var handler = StateChanged;
            if (handler != null)
            {
                Console.WriteLine("Unchoke connection");
                handler(this, new EventArgs());
            }
            
        }
        private void HandleInterested()
        {
            IsInterestedReceived = true;
            var handler = StateChanged;
            if (handler != null)
            {
                Console.WriteLine("Interested");
                handler(this, new EventArgs());
            }
        }
        private void HandleNotInterested()
        {
            IsInterestedReceived = false;
            var handler = StateChanged;
            if (handler != null)
            {
                Console.WriteLine("Not interested");
                handler(this, new EventArgs());
            }
        }

        private void HandleHave(int index)
        {
            IsPieceDownloaded[index] = true;
            var handler = StateChanged;
            if (handler != null)
            {
                Console.WriteLine("Have " + index + " - " + PiecesDownloadedCount + " available (" + PiecesDownloaded + ")");
                handler(this, new EventArgs());
            }
        }
        private void HandleBitfield(bool[] isDownloaded)
        {
            for(int i = 0;i<torrent.Infohash.Length;i++)
            {
                IsPieceDownloaded[i] = IsPieceDownloaded[i] || isDownloaded[i]; 
            }
            var handler = StateChanged;
            handler(this,new EventArgs());
        }

        private void HandleRequest(int index, int begin, int length)
        {
            var handler = BlockRequested;
            if(handler != null)
            {
                handler(this,new DataRequest()
                {
                    Peer = this,
                    Piece = index,
                    Begin = begin,
                    Length = length
                });
            }
        }

        private void HandlePiece(int index, int begin, byte[] data)
        {
            var handler = BlockReceived;
            if(handler != null)
            {
                handler(this,new DataPackage()
                {
                    Peer = this,
                    Piece = index,
                    Block = begin/torrent.BlockSize,
                    Data = data
                });
            }
        }

        private void HandleCancel(int index, int begin, int length)
        {
            var handler = BlockCancelled;
            if(handler != null)
            {
                handler(this,new DataRequest()
                {
                   Peer = this,
                   Piece = index,
                   Begin = begin,
                   Length = length 
                });
            }
        }
    }
}