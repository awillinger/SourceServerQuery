using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SteamMasterServer.Lib
{
    public class ServerInfoResponse
    {
        public String ip { get; set; }
        public int port { get; set; }

        public String name { get; set; }
        public String map { get; set; }
        public String directory { get; set; }
        public String game { get; set; }
        public short appid { get; set; }
        public int players { get; set; }
        public int maxplayers { get; set; }
        public int bots { get; set; }
        public Boolean dedicated  { get; set; }
        public String os  { get; set; }
        public Boolean password { get; set; }
        public Boolean secure { get; set; }
        public String version { get; set; }
    }
}
