using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SteamMasterServer.Lib
{
    public class PlayersResponse
    {
        public short player_count;
        public List<Player> players = new List<Player>();

        public class Player
        {
            public String name { get; set; }
            public int score { get; set; }
            public float playtime { get; set; }
        }
    }
}
