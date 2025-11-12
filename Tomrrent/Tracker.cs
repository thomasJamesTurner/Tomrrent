using System.Net;
using Microsoft.AspNetCore.SignalR;

namespace Tomrrent
{

    public enum TrackerEvent
    {
        Started,
        Paused,
        Stopped
    }
    public class Tracker
    {
        public DateTime LastPeerRequest { get; private set; } = DateTime.MinValue;
        public TimeSpan RequestInterval { get; private set; } = TimeSpan.FromMinutes(10);
        public event EventHandler<List<IPEndPoint>> PeerListUpdated;
        public string Address { get; private set; }
        private HttpWebRequest httpWebRequest;
        public Tracker(string address)
        {
            Address = address;
        }

        public void Update(Torrent torrent, TrackerEvent trackerEvent, string id, int port)
        {
            // wait until after request interval has elapsed before asking for new peers
            if (trackerEvent == TrackerEvent.Started && DateTime.UtcNow < LastPeerRequest.Add(RequestInterval))
                return;

            LastPeerRequest = DateTime.UtcNow;

            string url = String.Format("{0}?info_hash={1}&peer_id={2}&port={3}&uploaded={4}&downloaded={5}&left={6}&event={7}&compact=1",
                             Address, torrent.UrlSafeStringInfohash,
                             id, port,
                             torrent.Uploaded, torrent.Downloaded, torrent.Left,
                             Enum.GetName(typeof(TrackerEvent), trackerEvent).ToLower());

            Request(url);
        }

        public void ResetLastRequest()
        {
            LastPeerRequest = DateTime.MinValue;
        }
        private void Request(string url)
        {
            httpWebRequest = (HttpWebRequest)HttpWebRequest.Create(url);
            httpWebRequest.BeginGetResponse(HandleResponse, null);
        }
        private void HandleResponse(IAsyncResult result)
        {
            byte[] data;

            using (HttpWebResponse response = (HttpWebResponse)httpWebRequest.EndGetResponse(result))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine("error reaching tracker " + this + ": " + response.StatusCode + " " + response.StatusDescription);
                    return;
                }

                using (Stream stream = response.GetResponseStream())
                {
                    data = new byte[response.ContentLength];
                    stream.Read(data, 0, Convert.ToInt32(response.ContentLength));
                }
            }

            Dictionary<string,object> info = Encoder.Decode(data) as Dictionary<string,object>;

            if (info == null)
            {
                Console.WriteLine("unable to decode tracker announce response");
                return;
            }

            RequestInterval = TimeSpan.FromSeconds((long)info["interval"]);
            byte[] peerInfo = (byte[])info["peers"];

            List<IPEndPoint> peers = new List<IPEndPoint>();
            for (int i = 0; i < peerInfo.Length/6; i++)
            {
                int offset = i * 6;
                string address = peerInfo[offset] + "." + peerInfo[offset+1] + "." + peerInfo[offset+2] + "." + peerInfo[offset+3];
                int port = BitConverter.EndianBitConverter.BigEndian.ToChar(peerInfo, offset + 4);

                peers.Add(new IPEndPoint(IPAddress.Parse(address), port));
            }

            var handler = PeerListUpdated;
            if (handler != null)
                handler(this, peers);
        }

    }
}