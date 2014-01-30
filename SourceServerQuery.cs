using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Checksums;

namespace SteamMasterServer.Lib
{
    public class SourceServerQuery
    {
        // this will hold our ip address as an object (required by Socket & UdpClient)
        private IPEndPoint remote;
        // used for single-packet responses (mainly A2S_INFO, A2S_PLAYER and Challenge)
        private Socket socket;
        // multi-packet responses (currently only A2S_RULES)
        private UdpClient client;

        // send & receive timeouts
        private int send_timeout = 2500;
        private int receive_timeout = 2500;

        // raw response returned from the server
        private byte[] raw_data;

        private int offset = 0;

        // constants
        private readonly byte[] A2S_HEADER = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        private readonly byte A2S_INFO = 0x54;
        private readonly byte A2S_PLAYER = 0x55;
        private readonly byte A2S_RULES = 0x56;
        private readonly byte[] A2S_INFO_STUB = new byte[] { 0x53, 0x6F, 0x75, 0x72, 0x63, 0x65, 0x20, 0x45, 0x6E, 0x67, 0x69, 0x6E, 0x65, 0x20, 0x51, 0x75, 0x65, 0x72, 0x79, 0x00 };
        private readonly byte[] CHALLENGE = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF};

        public SourceServerQuery(String ip, int port)
        {
            this.remote = new IPEndPoint(IPAddress.Parse(ip), port);
        }

        /// <summary>
        /// Retrieve general information from the Server via A2S_Info.
        /// See https://developer.valvesoftware.com/wiki/Server_queries#A2S_INFO for more Information
        /// </summary>
        /// <returns>A ServerInfoResponse Object containing the publically available data</returns>
        public ServerInfoResponse GetServerInformation()
        {
            // open socket if not already open
            this.GetSocket();
            // reset our pointer
            this.offset = 6;

            ServerInfoResponse sr = new ServerInfoResponse();

            // construct request byte-array
            byte[] request = new byte[A2S_HEADER.Length+A2S_INFO_STUB.Length+1];
            Array.Copy(this.A2S_HEADER, 0, request, 0, A2S_HEADER.Length);
            request[A2S_HEADER.Length] = this.A2S_INFO;
            Array.Copy(this.A2S_INFO_STUB, 0, request, A2S_HEADER.Length + 1, A2S_INFO_STUB.Length);

            this.socket.Send(request);

            this.raw_data = new byte[1024];

            try
            {
                this.socket.Receive(this.raw_data);

                // read data
                sr.name = this.ReadString();
                sr.map = this.ReadString();
                sr.directory = this.ReadString();
                sr.game = this.ReadString();
                sr.appid = this.ReadInt16();
                sr.players = this.ReadByte();
                sr.maxplayers = this.ReadByte();
                sr.bots = this.ReadByte();
                sr.dedicated = (this.ReadChar() == 'd') ? true : false;
                sr.os = (this.ReadChar() == 'l') ? "Linux" : "Windows";
                sr.password = (this.ReadByte() == 1) ? true : false;
                sr.secure = (this.ReadByte() == 1) ? true : false;
                sr.version = this.ReadString();
            }
            catch (SocketException e)
            {
                sr.name = "N/A (request timed out)";
                sr.map = "N/A";
                sr.directory = "N/A";
                sr.game = "N/A";
                sr.appid = -1;
                sr.players = 0;
                sr.maxplayers = 0;
                sr.bots = -1;
                sr.dedicated = false;
                sr.os = "N/A";
                sr.password = false;
                sr.secure = false;
                sr.version = "N/A";
            }

            return sr;
        }

        /// <summary>
        /// Get a list of currently in-game clients on the specified gameserver.
        /// <b>Please note:</b> the playtime is stored as a float in <i>seconds</i>, you might want to convert it.
        /// 
        /// See https://developer.valvesoftware.com/wiki/Server_queries#A2S_PLAYER for more Information
        /// </summary>
        /// <returns>A PLayersResponse Object containing the name, score and playtime of each player</returns>
        public PlayersResponse GetPlayerList()
        {
            // open socket if not already open
            this.GetSocket();
            // we don't need the header, so set pointer to where the payload begins
            this.offset = 5;

            try
            {
                PlayersResponse pr = new PlayersResponse();

                // since A2S_PLAYER requests require a valid challenge, get it first
                byte[] challenge = this.GetChallenge(A2S_PLAYER, true);

                byte[] request = new byte[challenge.Length+this.A2S_HEADER.Length+1];
                Array.Copy(this.A2S_HEADER, 0, request, 0, this.A2S_HEADER.Length);
                request[this.A2S_HEADER.Length] = A2S_PLAYER;
                Array.Copy(challenge, 0, request, this.A2S_HEADER.Length + 1, challenge.Length);

                this.socket.Send(request);

                this.raw_data = new byte[1024];
                this.socket.Receive(this.raw_data);

                byte player_count = this.ReadByte();

                // fill up the list of players
                for (int i = 0; i < player_count; i++)
                {
                    this.ReadByte();

                    PlayersResponse.Player p = new PlayersResponse.Player();

                    p.name = this.ReadString();
                    p.score = this.ReadInt32();
                    p.playtime = this.ReadFloat();

                    pr.players.Add(p);
                }

                pr.player_count = player_count;

                return pr;
            }
            catch (SocketException e)
            {
                return null;
            }           
        }

        /// <summary>
        /// Get a list of all publically available CVars ("rules") from the server.
        /// <b>Note:</b> Due to a bug in the Source Engine, it might happen that some CVars/values are cut off.
        /// 
        /// Example: mp_idlemaxtime = [nothing]
        /// Only Valve can fix that.
        /// </summary>
        /// <returns>A RulesResponse Object containing a Name-Value pair of each CVar</returns>
        public RulesResponse GetRules()
        {
            // open udpclient if not already open
            this.GetClient();

            try
            {
                RulesResponse rr = new RulesResponse();

                // similar to A2S_PLAYER requests, A2S_RULES require a valid challenge
                byte[] challenge = this.GetChallenge(A2S_RULES, false);

                byte[] request = new byte[challenge.Length + this.A2S_HEADER.Length + 1];
                Array.Copy(this.A2S_HEADER, 0, request, 0, this.A2S_HEADER.Length);
                request[this.A2S_HEADER.Length] = A2S_RULES;
                Array.Copy(challenge, 0, request, this.A2S_HEADER.Length + 1, challenge.Length);

                this.client.Send(request, request.Length);

                //
                // Since A2S_RULES responses might be split up into several packages/compressed, we have to do a special handling of them
                //
                int bytesRead;

                // this will keep our assembled message
                byte[] buffer = new byte[4096];

                // send first request

                this.raw_data = this.client.Receive(ref this.remote);

                bytesRead = this.raw_data.Length;

                // reset pointer 
                this.offset = 0;

                int is_split = this.ReadInt32();
                int requestid = this.ReadInt32();

                this.offset = 4;

                // response is split up into several packets
                if (this.PacketIsSplit(is_split))
                {
                    bool isCompressed = false;
                    byte[] splitData;
                    int packetCount, packetNumber, requestId;
                    int packetsReceived = 1;
                    int packetChecksum = 0;
                    int packetSplit = 0;
                    short splitSize;
                    int uncompressedSize = 0;
                    List<byte[]> splitPackets = new List<byte[]>();

                    do
                    {
                        // unique request id 
                        requestId = this.ReverseBytes(this.ReadInt32());
                        isCompressed = this.PacketIsCompressed(requestId);

                        packetCount = this.ReadByte();
                        packetNumber = this.ReadByte() + 1;
                        // so we know how big our byte arrays have to be
                        splitSize = this.ReadInt16();
                        splitSize -= 4; // fix

                        if (packetsReceived == 1)
                        {
                            for (int i = 0; i < packetCount; i++)
                            {
                                splitPackets.Add(new byte[] { });
                            }
                        }

                        // if the packets are compressed, get some data to decompress them
                        if (isCompressed)
                        {
                            uncompressedSize = ReverseBytes(this.ReadInt32());
                            packetChecksum = ReverseBytes(this.ReadInt32());
                        }

                        // ommit header in first packet
                        if (packetNumber == 1) this.ReadInt32();

                        splitData = new byte[splitSize];
                        splitPackets[packetNumber - 1] = this.ReadBytes();

                        // fixes a case where the returned package might still contain a character after the last \0 terminator (truncated name => value)
                        // please note: this therefore also removes the value of said variable, but atleast the program won't crash
                        if (splitPackets[packetNumber-1].Length -1 > 0 && splitPackets[packetNumber - 1][splitPackets[packetNumber - 1].Length - 1] != 0x00)
                        {
                            splitPackets[packetNumber - 1][splitPackets[packetNumber - 1].Length - 1] = 0x00;
                        }

                        // reset pointer again, so we can copy over the contents
                        this.offset = 0;

                        if (packetsReceived < packetCount)
                        {

                            this.raw_data = this.client.Receive(ref this.remote);
                            bytesRead = this.raw_data.Length;

                            // continue with the next packets
                            packetSplit = this.ReadInt32();
                            packetsReceived++;
                        }
                        else
                        {
                            // all packets received
                            bytesRead = 0;
                        }
                    }
                    while (packetsReceived <= packetCount && bytesRead > 0 && packetSplit == -2);

                    // decompress
                    if (isCompressed)
                    {
                        buffer = ReassemblePacket(splitPackets, true, uncompressedSize, packetChecksum);
                    }
                    else
                    {
                        buffer = ReassemblePacket(splitPackets, false, 0, 0);
                    }
                }
                else
                {
                    buffer = this.raw_data;
                }

                // move our final result over to handle it
                this.raw_data = buffer;

                // omitting header
                this.offset = 1;
                rr.rule_count = this.ReadInt16();

                for (int i = 0; i < rr.rule_count; i++)
                {
                    RulesResponse.Rule rule = new RulesResponse.Rule();
                    rule.name = this.ReadString();
                    rule.value = this.ReadString();
                    rr.rules.Add(rule);
                }

                return rr;
            }
            catch (SocketException e)
            {
                return null;
            }
        }

        /// <summary>
        /// Close all currently open socket/UdpClient connections
        /// </summary>
        public void CleanUp()
        {
            if (this.socket != null) this.socket.Close();
            if (this.client != null) this.client.Close();
        }

        /// <summary>
        /// Set the IP and Port used in this Object.
        /// </summary>
        /// <param name="ip">The Server IP</param>
        /// <param name="port">The Server Port</param>
        public void SetAddress(String ip, int port)
        {
            this.remote = new IPEndPoint(IPAddress.Parse(ip), port);

            if (this.socket != null)
            {
                this.socket.Close();
                this.socket = null;
            }
            if (this.client != null)
            {
                this.client.Close();
                this.client = null;
            }
        }

        /// <summary>
        /// Sets the Send Timeout on both the Socket and the Client
        /// </summary>
        /// <param name="timeout"></param>
        public void SetSendTimeout(int timeout)
        {
            this.send_timeout = timeout;
        }

        /// <summary>
        /// Sets the Receive Timeout on both the Socket and the Client
        /// </summary>
        /// <param name="timeout"></param>
        public void SetReceiveTimeout(int timeout)
        {
            this.receive_timeout = timeout;
        }

        /// <summary>
        /// Open up a new Socket-based connection to a server, if not already open.
        /// </summary>
        private void GetSocket()
        {
            if (this.socket == null)
            {
                this.socket = new Socket(
                            AddressFamily.InterNetwork,
                            SocketType.Dgram,
                            ProtocolType.Udp);

                this.socket.SendTimeout = this.send_timeout;
                this.socket.ReceiveTimeout = this.receive_timeout;

                this.socket.Connect(this.remote);
            }
        }

        /// <summary>
        /// Create a new UdpClient connection to a server (mostly used for multi-packet answers)
        /// </summary>
        private void GetClient()
        {
            if (this.client == null)
            {
                this.client = new UdpClient();
                this.client.Connect(this.remote);
                this.client.DontFragment = true;

                this.client.Client.SendTimeout = this.send_timeout;
                this.client.Client.ReceiveTimeout = this.receive_timeout;
            }
        }

        /// <summary>
        /// Reassmble a multi-packet response.
        /// </summary>
        /// <param name="splitPackets">The packets to assemble</param>
        /// <param name="isCompressed">true: packets are compressed; false: not</param>
        /// <param name="uncompressedSize">The size of the message after decompression (for comparison)</param>
        /// <param name="packetChecksum">Validation of the result</param>
        /// <returns>A byte-array containing all packets assembled together/decompressed.</returns>
        private byte[] ReassemblePacket(List<byte[]> splitPackets, bool isCompressed, int uncompressedSize, int packetChecksum)
        {
            byte[] packetData, tmpData;
            packetData = new byte[0];

            foreach (byte[] splitPacket in splitPackets)
            {
                if (splitPacket == null)
                {
                    throw new Exception();
                }

                tmpData = packetData;
                packetData = new byte[tmpData.Length + splitPacket.Length];

                MemoryStream memStream = new MemoryStream(packetData);
                memStream.Write(tmpData, 0, tmpData.Length);
                memStream.Write(splitPacket, 0, splitPacket.Length);
            }

            if (isCompressed)
            {
                BZip2InputStream bzip2 = new BZip2InputStream(new MemoryStream(packetData));
                bzip2.Read(packetData, 0, uncompressedSize);

                Crc32 crc32 = new Crc32();
                crc32.Update(packetData);

                if (crc32.Value != packetChecksum)
                {
                    throw new Exception("CRC32 checksum mismatch of uncompressed packet data.");
                }
            }

            return packetData;
        }

        /// <summary>
        /// Invert the Byte-order Mark of an value, used for compatibility between Little <-> Large BOM
        /// </summary>
        /// <param name="value">The value to invert</param>
        /// <returns>BOM-inversed value (if needed), otherwise the original value</returns>
        private int ReverseBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToInt32(bytes, 0);
        }

        /// <summary>
        /// <see cref="ReverseBytes(int value)">See</see> for more details.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private short ReverseBytes(short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToInt16(bytes, 0);
        }

        /// <summary>
        /// Determine whetever or not a message is compressed.
        /// Simply detects if the most significant bit is 1.
        /// </summary>
        /// <param name="value">The value to check</param>
        /// <returns>true, if message is compressed, false otherwise</returns>
        private bool PacketIsCompressed(int value)
        {
            return (value & 0x8000) != 0;
        }

        /// <summary>
        /// Determine whetever or not a message is split up.
        /// </summary>
        /// <param name="paket">The value to check</param>
        /// <returns>true, if message is split up, false otherwise</returns>
        private bool PacketIsSplit(int paket)
        {
            return (paket == -2);
        }

        /// <summary>
        /// Request the 4-byte challenge id from the server, required for A2S_RULES and A2S_PLAYER.
        /// </summary>
        /// <param name="type">The type of message to request the challenge for (see constants)</param>
        /// <param name="socket">Request method to use (performance reasons)</param>
        /// <returns>A Byte Array (4-bytes) containing the challenge</returns>
        private Byte[] GetChallenge(byte type, bool socket = true)
        {
            byte[] request = new byte[this.A2S_HEADER.Length+this.CHALLENGE.Length+1];
            Array.Copy(this.A2S_HEADER, 0, request, 0, this.A2S_HEADER.Length);
            request[A2S_HEADER.Length] = type;
            Array.Copy(this.CHALLENGE, 0, request, this.A2S_HEADER.Length + 1, this.CHALLENGE.Length);

            byte[] raw_response = new byte[24];
            byte[] challenge = new byte[4];

            // using sockets
            if (socket)
            {
                this.socket.Send(request);
                this.socket.Receive(raw_response);
            }
            else
            {
                this.client.Send(request, request.Length);
                raw_response = this.client.Receive(ref this.remote);
            }

            Array.Copy(raw_response, 5, challenge, 0, 4); // change this valve modifies the protocol!

            return challenge;
        }

        /// <summary>
        /// Read a single byte value from our raw data.
        /// </summary>
        /// <returns>A single Byte at the next Offset Address</returns>
        private Byte ReadByte()
        {
            byte[] b = new byte[1];
            Array.Copy(this.raw_data, this.offset, b, 0, 1);

            this.offset++;
            return b[0];
        }

        /// <summary>
        /// Read all remaining Bytes from our raw data.
        /// Used for multi-packet responses.
        /// </summary>
        /// <returns>All remaining data</returns>
        private Byte[] ReadBytes()
        {
            int size = (this.raw_data.Length - this.offset - 4);
            if (size < 1) return new Byte[] { };

            byte[] b = new byte[size];
            Array.Copy(this.raw_data, this.offset, b, 0, this.raw_data.Length - this.offset - 4);

            this.offset += (this.raw_data.Length - this.offset - 4);
            return b;
        }

        /// <summary>
        /// Read a 32-Bit Integer value from the next offset address.
        /// </summary>
        /// <returns>The Int32 Value found at the offset address</returns>
        private Int32 ReadInt32()
        {
            byte[] b = new byte[4];
            Array.Copy(this.raw_data, this.offset, b, 0, 4);

            this.offset += 4;
            return BitConverter.ToInt32(b, 0);
        }

        /// <summary>
        /// Read a 16-Bit Integer (also called "short") value from the next offset address.
        /// </summary>
        /// <returns>The Int16 Value found at the offset address</returns>
        private Int16 ReadInt16()
        {
            byte[] b = new byte[2];
            Array.Copy(this.raw_data, this.offset, b, 0, 2);

            this.offset += 2;
            return BitConverter.ToInt16(b, 0);
        }

        /// <summary>
        /// Read a Float value from the next offset address.
        /// </summary>
        /// <returns>The Float Value found at the offset address</returns>
        private float ReadFloat()
        {
            byte[] b = new byte[4];
            Array.Copy(this.raw_data, this.offset, b, 0, 4);

            this.offset += 4;
            return BitConverter.ToSingle(b, 0);
        }

        /// <summary>
        /// Read a single char value from the next offset address.
        /// </summary>
        /// <returns>The Char found at the offset address</returns>
        private Char ReadChar()
        {
            byte[] b = new byte[1];
            Array.Copy(this.raw_data, this.offset, b, 0, 1);

            this.offset++;
            return (char)b[0];
        }

        /// <summary>
        /// Read a String until its end starting from the next offset address.
        /// Reading stops once the method detects a 0x00 Character at the next position (\0 terminator)
        /// </summary>
        /// <returns>The String read</returns>
        private String ReadString()
        {
            byte[] cache = new byte[1]{0x01};
            String output = "";

            while(cache[0] != 0x00)
            {
                if (this.offset == this.raw_data.Length) break; // fixes Valve's inability to code a proper query protocol
                Array.Copy(this.raw_data, this.offset, cache, 0, 1);
                this.offset++;
                output += Encoding.UTF8.GetString(cache);
            }

            return output;
        }
    }
}
