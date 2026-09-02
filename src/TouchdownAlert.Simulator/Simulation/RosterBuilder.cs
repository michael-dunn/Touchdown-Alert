using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Simulator.Simulation;

/// <summary>
/// Deterministically builds the 10-team league: real team names/ids from fixtures/league-2026-preseason.json,
/// plausible player rosters (a few confirmed real ids, the rest invented but unique), and the week-1 matchup
/// pairings taken from that same fixture. Every call produces byte-for-byte the same league.
/// </summary>
internal static class RosterBuilder
{
    // Team ids/names/abbrevs straight from fixtures/league-2026-preseason.json.
    private static readonly (int Id, string Name, string Abbrev)[] TeamInfo =
    {
        (1, "Michael's Magnificent Team", "MMT"),
        (2, "Emma and Evie Extravaganza", "EEE"),
        (3, "Lauryn's Loud Team", "LLT"),
        (4, "Team 4", "TM4"),
        (5, "Hailey's Heated Team", "HHT"),
        (6, "Team 6", "TM6"),
        (7, "Team 7", "TM7"),
        (8, "Team 8", "TM8"),
        (9, "Team 9", "TM9"),
        (10, "KCP - Kimmie Cocoa Pop", "KCP"),
    };

    // Week-1 matchup pairings, also straight from the fixture's schedule (matchupPeriodId == 1).
    private static readonly (int Home, int Away)[] Week1Matchups =
    {
        (3, 9), (8, 4), (6, 5), (1, 10), (2, 7),
    };

    private static readonly string[] QbNames =
    {
        "Josh Allen", "Lamar Jackson", "Patrick Mahomes", "Jalen Hurts", "Joe Burrow",
        "Justin Herbert", "C.J. Stroud", "Dak Prescott", "Trevor Lawrence", "Kyler Murray",
    };

    private static readonly string[] RbNames =
    {
        "Saquon Barkley", "Christian McCaffrey", "Bijan Robinson", "Jahmyr Gibbs", "Breece Hall",
        "Derrick Henry", "Jonathan Taylor", "De'Von Achane", "Kenneth Walker III", "Isiah Pacheco",
        "James Cook", "Josh Jacobs", "Rachaad White", "Travis Etienne", "Aaron Jones",
        "Tony Pollard", "Alvin Kamara", "Najee Harris", "Kyren Williams", "Joe Mixon",
    };

    private static readonly string[] WrNames =
    {
        "Ja'Marr Chase", "Justin Jefferson", "CeeDee Lamb", "Tyreek Hill", "A.J. Brown",
        "Amon-Ra St. Brown", "Puka Nacua", "Garrett Wilson", "Chris Olave", "DK Metcalf",
        "Stefon Diggs", "Davante Adams", "DeVonta Smith", "Jaylen Waddle", "Nico Collins",
        "Drake London", "Terry McLaurin", "Calvin Ridley", "Mike Evans", "Brandon Aiyuk",
    };

    private static readonly string[] TeNames =
    {
        "Travis Kelce", "Sam LaPorta", "Mark Andrews", "T.J. Hockenson", "George Kittle",
        "Trey McBride", "Dallas Goedert", "Evan Engram", "Kyle Pitts", "David Njoku",
    };

    private static readonly string[] KNames =
    {
        "Justin Tucker", "Harrison Butker", "Brandon Aubrey", "Jake Moody", "Evan McPherson",
        "Younghoe Koo", "Daniel Carlson", "Tyler Bass", "Chris Boswell", "Jason Sanders",
    };

    // (proTeamId, city) used to build D/ST id -(16000+proTeamId) and name "{city} D/ST".
    // Ravens (33) and Dolphins (15) are confirmed-real ids/ids per the project brief; the rest use
    // plausible proTeamIds in the same numeric family and are not guaranteed to match ESPN's real table.
    private static readonly (int ProTeamId, string City)[] DstInfo =
    {
        (33, "Baltimore"), (15, "Miami"), (2, "Buffalo"), (12, "Kansas City"), (21, "Philadelphia"),
        (6, "Dallas"), (25, "San Francisco"), (9, "Green Bay"), (20, "New York"), (3, "Chicago"),
    };

    // proTeamId for Josh Allen (Bills) per the project brief.
    private const int BillsProTeamId = 2;

    public static void Build(Dictionary<long, SimPlayer> players, List<SimTeam> teams, List<SimMatchup> matchups)
    {
        long nextInventedId = 6_000_001;
        long NextId() => nextInventedId++;

        SimPlayer AddPlayer(long id, string name, string position, int proTeamId)
        {
            if (!players.TryGetValue(id, out var existing))
            {
                existing = new SimPlayer { Id = id, FullName = name, Position = position, ProTeamId = proTeamId };
                players[id] = existing;
            }

            return existing;
        }

        for (var i = 0; i < TeamInfo.Length; i++)
        {
            var (id, name, abbrev) = TeamInfo[i];
            var team = new SimTeam { Id = id, Name = name, Abbrev = abbrev };

            // --- Starters ---
            SimPlayer qb;
            if (id == 1)
            {
                // Confirmed real id from the project brief: Josh Allen, QB, Bills.
                qb = AddPlayer(3918298, "Josh Allen", "QB", BillsProTeamId);
            }
            else
            {
                qb = AddPlayer(NextId(), QbNames[i % QbNames.Length], "QB", 100 + i);
            }
            team.Roster.Add(new SimRosterSlot(qb.Id, EspnLineupSlots.Qb));

            var rb1 = id == 2
                ? AddPlayer(3929630, "Saquon Barkley", "RB", 100 + i) // confirmed real id, per the project brief
                : AddPlayer(NextId(), RbNames[(2 * i) % RbNames.Length], "RB", 100 + i);
            var rb2 = AddPlayer(NextId(), RbNames[(2 * i + 1) % RbNames.Length], "RB", 100 + i);
            team.Roster.Add(new SimRosterSlot(rb1.Id, EspnLineupSlots.Rb));
            team.Roster.Add(new SimRosterSlot(rb2.Id, EspnLineupSlots.Rb));

            var wr1 = id == 3
                ? AddPlayer(4362628, "Ja'Marr Chase", "WR", 100 + i) // confirmed real id, per the project brief
                : AddPlayer(NextId(), WrNames[(2 * i) % WrNames.Length], "WR", 100 + i);
            var wr2 = AddPlayer(NextId(), WrNames[(2 * i + 1) % WrNames.Length], "WR", 100 + i);
            team.Roster.Add(new SimRosterSlot(wr1.Id, EspnLineupSlots.Wr));
            team.Roster.Add(new SimRosterSlot(wr2.Id, EspnLineupSlots.Wr));

            var te = AddPlayer(NextId(), TeNames[i % TeNames.Length], "TE", 100 + i);
            team.Roster.Add(new SimRosterSlot(te.Id, EspnLineupSlots.Te));

            // FLEX: an extra RB/WR/TE-eligible player.
            var flex = AddPlayer(NextId(), RbNames[(2 * i + 5) % RbNames.Length], "RB", 100 + i);
            team.Roster.Add(new SimRosterSlot(flex.Id, EspnLineupSlots.Flex));

            var (dstProTeamId, dstCity) = DstInfo[i];
            var dst = dstProTeamId == 33
                ? AddPlayer(-16033, "Baltimore D/ST", "D/ST", 33) // confirmed real id, per the project brief
                : dstProTeamId == 15
                    ? AddPlayer(-16015, "Miami D/ST", "D/ST", 15) // confirmed real id, per the project brief
                    : AddPlayer(-(16000 + dstProTeamId), $"{dstCity} D/ST", "D/ST", dstProTeamId);
            team.Roster.Add(new SimRosterSlot(dst.Id, EspnLineupSlots.Dst));

            var k = AddPlayer(NextId(), KNames[i % KNames.Length], "K", 100 + i);
            team.Roster.Add(new SimRosterSlot(k.Id, EspnLineupSlots.K));

            // --- Bench (4 players) ---
            for (var b = 0; b < 4; b++)
            {
                // Team 5's 2nd bench slot is a DELIBERATE duplicate rostering of Ja'Marr Chase (see
                // SimPlayer class docs) to exercise "started on one team, benched on another".
                if (id == 5 && b == 1)
                {
                    team.Roster.Add(new SimRosterSlot(4362628, EspnLineupSlots.Bench));
                    continue;
                }

                var benchPlayer = b switch
                {
                    0 => AddPlayer(NextId(), RbNames[(2 * i + 7) % RbNames.Length], "RB", 100 + i),
                    1 => AddPlayer(NextId(), WrNames[(2 * i + 7) % WrNames.Length], "WR", 100 + i),
                    2 => AddPlayer(NextId(), TeNames[(i + 3) % TeNames.Length], "TE", 100 + i),
                    _ => AddPlayer(NextId(), QbNames[(i + 3) % QbNames.Length], "QB", 100 + i),
                };
                team.Roster.Add(new SimRosterSlot(benchPlayer.Id, EspnLineupSlots.Bench));
            }

            teams.Add(team);
        }

        for (var i = 0; i < Week1Matchups.Length; i++)
        {
            var (home, away) = Week1Matchups[i];
            matchups.Add(new SimMatchup(i + 1, home, away));
        }
    }
}
