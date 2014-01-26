using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SteamMasterServer.Lib
{
    public class RulesResponse
    {
        public int rule_count = 0;
        public List<Rule> rules = new List<Rule>();

        public class Rule
        {
            public String name { get; set; }
            public String value { get; set; }
        }
    }
}
