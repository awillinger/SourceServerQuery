SourceServerQuery
=================

A free-to-use library which can be used for querying SRCDS-based Gameservers, written in C#.

############
Requirements
############

* .NET Framework 4.5
* SharpZipLib (available via NuGet)
* A Source-based Gameserver

#######
Methods
#######

-------------------
General Information
-------------------

Retrieve general Information from the Server. such as Name, Map, Game, Players, etc.

.. code:: c#

    ServerInfoResponse resp = obj.GetServerInformation();

-----------
Player list
-----------

Get a list of currently connected clients on the Server, including their name, playtime and score.

.. code:: c#

    PlayersResponse resp = obj.GetPlayerList();


---------
CVar List
---------

This will request a list of publically available CVars (known as rules) from the server.

.. code:: c#

    RulesResponse resp = obj.GetRules();


#####
Usage
#####

#. Initialize a SourceServerQuery object, with the server's ip & port passed to the constructor.
#. Call one (or multiple) methods specified above (e.g. obj.GetRules())
#. Cleanup with obj.Cleanup() (to free up Ressources)

#######
Example
#######

.. code:: c#

    SourceServerQuery lib = new SourceServerQuery("127.0.0.1", 27045);
    
    ServerInfoResponse sr = lib.GetServerInformation();
    PlayersResponse pr = lib.GetPlayerList();
    RulesResponse rr = lib.GetRules();
    
    Console.WriteLine(sr.name); // e.g. "test server"
    Console.WriteLine(pr.player_count); // e.g. 23
    
    foreach (var player in pr.players)
    {
        Console.WriteLine(player.name + "=>" + player.score + "=>" + player.playtime); // "player" => 123 => 700 SECONDS!
    }

    Console.WriteLine(rr.rule_count);
    
    for (int i = 0; i < rr.rule_count; i++)
    {
        Console.WriteLine(rr.rules[i].name + "=>" + rr.rules[i].value);
    }
    
    lib.CleanUp();


#####
Notes
#####