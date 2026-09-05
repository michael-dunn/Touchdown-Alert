using System.Xml.Linq;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Yahoo;

namespace TouchdownAlert.Core.Tests;

public class YahooXmlParserTests
{
    private const string Ns = "http://fantasysports.yahooapis.com/fantasy/v2/base.rng";

    [Fact]
    public void ParseLeague_ReadsFields()
    {
        var xml = $"""
        <fantasy_content xmlns="{Ns}">
          <league>
            <league_key>414.l.123456</league_key>
            <league_id>123456</league_id>
            <name>My League</name>
            <season>2026</season>
            <current_week>3</current_week>
            <start_week>1</start_week>
            <end_week>17</end_week>
            <num_teams>10</num_teams>
            <is_finished>0</is_finished>
          </league>
        </fantasy_content>
        """;

        var info = YahooXmlParser.ParseLeague(XDocument.Parse(xml));

        Assert.Equal("414.l.123456", info.LeagueKey);
        Assert.Equal("123456", info.LeagueId);
        Assert.Equal("My League", info.Name);
        Assert.Equal(2026, info.Season);
        Assert.Equal(3, info.CurrentWeek);
        Assert.Equal(1, info.StartWeek);
        Assert.Equal(17, info.EndWeek);
        Assert.Equal(10, info.NumTeams);
        Assert.False(info.IsFinished);
    }

    [Fact]
    public void ParseLeague_WithoutNamespace_StillWorks()
    {
        var xml = """
        <fantasy_content>
          <league>
            <league_key>414.l.1</league_key>
            <name>No Namespace League</name>
            <current_week>1</current_week>
          </league>
        </fantasy_content>
        """;

        var info = YahooXmlParser.ParseLeague(XDocument.Parse(xml));

        Assert.Equal("No Namespace League", info.Name);
        Assert.Equal(1, info.CurrentWeek);
    }

    [Fact]
    public void ParseStatCategories_ReadsAllFields()
    {
        var xml = $"""
        <fantasy_content xmlns="{Ns}">
          <league>
            <settings>
              <stat_categories>
                <stats>
                  <stat>
                    <stat_id>5</stat_id>
                    <name>Passing Touchdowns</name>
                    <display_name>Pass TD</display_name>
                    <position_type>O</position_type>
                    <enabled>1</enabled>
                  </stat>
                  <stat>
                    <stat_id>16</stat_id>
                    <name>2-Point Conversions</name>
                    <display_name>2-PT</display_name>
                    <position_type>O</position_type>
                    <enabled>1</enabled>
                  </stat>
                  <stat>
                    <stat_id>35</stat_id>
                    <name>Touchdown</name>
                    <display_name>TD</display_name>
                    <position_type>DT</position_type>
                    <enabled>1</enabled>
                  </stat>
                </stats>
              </stat_categories>
            </settings>
          </league>
        </fantasy_content>
        """;

        var categories = YahooXmlParser.ParseStatCategories(XDocument.Parse(xml));

        Assert.Equal(3, categories.Count);
        var passing = categories.Single(c => c.StatId == 5);
        Assert.Equal("Passing Touchdowns", passing.Name);
        Assert.Equal("Pass TD", passing.DisplayName);
        Assert.Equal("O", passing.PositionType);
        Assert.True(passing.Enabled);

        var defTd = categories.Single(c => c.StatId == 35);
        Assert.Equal("DT", defTd.PositionType);
    }

    [Fact]
    public void ParseScoreboard_ReadsBothTeamsPerMatchup()
    {
        var xml = $"""
        <fantasy_content xmlns="{Ns}">
          <league>
            <scoreboard>
              <matchups>
                <matchup>
                  <teams>
                    <team>
                      <team_key>414.l.123456.t.1</team_key>
                      <team_id>1</team_id>
                      <name>Team A</name>
                      <team_points><total>100.5</total></team_points>
                      <team_projected_points><total>110.2</total></team_projected_points>
                    </team>
                    <team>
                      <team_key>414.l.123456.t.2</team_key>
                      <team_id>2</team_id>
                      <name>Team B</name>
                      <team_points><total>95.0</total></team_points>
                      <team_projected_points><total>90.0</total></team_projected_points>
                    </team>
                  </teams>
                </matchup>
              </matchups>
            </scoreboard>
          </league>
        </fantasy_content>
        """;

        var matchups = YahooXmlParser.ParseScoreboard(XDocument.Parse(xml));

        Assert.Single(matchups);
        Assert.Equal(1, matchups[0].Team1.TeamId);
        Assert.Equal("Team A", matchups[0].Team1.Name);
        Assert.Equal(100.5, matchups[0].Team1.Points);
        Assert.Equal(110.2, matchups[0].Team1.ProjectedPoints);
        Assert.Equal(2, matchups[0].Team2.TeamId);
        Assert.Equal(95.0, matchups[0].Team2.Points);
    }

    [Fact]
    public void ParseRoster_ClassifiesStartersVsBenchAndReadsStats()
    {
        var xml = $"""
        <fantasy_content xmlns="{Ns}">
          <team>
            <team_key>414.l.123456.t.1</team_key>
            <roster>
              <players>
                <player>
                  <player_key>414.p.1</player_key>
                  <player_id>3918298</player_id>
                  <name><full>Josh Allen</full></name>
                  <display_position>QB</display_position>
                  <editorial_team_abbr>BUF</editorial_team_abbr>
                  <selected_position><position>QB</position></selected_position>
                  <player_points><total>24.5</total></player_points>
                  <player_stats>
                    <stats>
                      <stat><stat_id>5</stat_id><value>2</value></stat>
                      <stat><stat_id>10</stat_id><value>0</value></stat>
                    </stats>
                  </player_stats>
                </player>
                <player>
                  <player_key>414.p.2</player_key>
                  <player_id>999</player_id>
                  <name><full>Bench Guy</full></name>
                  <display_position>WR</display_position>
                  <editorial_team_abbr>NYJ</editorial_team_abbr>
                  <selected_position><position>BN</position></selected_position>
                  <player_points><total>0</total></player_points>
                  <player_stats><stats></stats></player_stats>
                </player>
                <player>
                  <player_key>414.p.3</player_key>
                  <player_id>1000</player_id>
                  <name><full>Hurt Guy</full></name>
                  <display_position>RB</display_position>
                  <editorial_team_abbr>MIA</editorial_team_abbr>
                  <selected_position><position>IR</position></selected_position>
                  <player_points><total>0</total></player_points>
                  <player_stats><stats></stats></player_stats>
                </player>
              </players>
            </roster>
          </team>
        </fantasy_content>
        """;

        var players = YahooXmlParser.ParseRoster(XDocument.Parse(xml));

        Assert.Equal(3, players.Count);
        var allen = players.Single(p => p.PlayerId == 3918298);
        Assert.Equal("Josh Allen", allen.FullName);
        Assert.Equal("QB", allen.DisplayPosition);
        Assert.Equal("BUF", allen.EditorialTeamAbbr);
        Assert.Equal("QB", allen.SelectedPosition);
        Assert.Equal(24.5, allen.Points);
        Assert.Equal(2, allen.Stats[5]);
        Assert.Equal(0, allen.Stats[10]);
        Assert.True(YahooXmlParser.IsStarterSlot(allen.SelectedPosition));

        var bench = players.Single(p => p.PlayerId == 999);
        Assert.False(YahooXmlParser.IsStarterSlot(bench.SelectedPosition));

        var ir = players.Single(p => p.PlayerId == 1000);
        Assert.False(YahooXmlParser.IsStarterSlot(ir.SelectedPosition));
    }

    [Theory]
    [InlineData("QB", true)]
    [InlineData("WR", true)]
    [InlineData("RB", true)]
    [InlineData("TE", true)]
    [InlineData("W/R/T", true)]
    [InlineData("W/R", true)]
    [InlineData("Q/W/R/T", true)]
    [InlineData("K", true)]
    [InlineData("DEF", true)]
    [InlineData("D", true)]
    [InlineData("BN", false)]
    [InlineData("IR", false)]
    [InlineData("IR+", false)]
    [InlineData("IR-R", false)]
    [InlineData("IL", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsStarterSlot_ClassifiesEverySlot(string? slot, bool expectedStarter)
    {
        Assert.Equal(expectedStarter, YahooXmlParser.IsStarterSlot(slot));
    }

    [Theory]
    [InlineData("Passing Touchdowns", "O", TouchdownType.Passing)]
    [InlineData("Rushing Touchdowns", "O", TouchdownType.Rushing)]
    [InlineData("Reception Touchdowns", "O", TouchdownType.Receiving)]
    [InlineData("Receiving Touchdowns", "O", TouchdownType.Receiving)]
    [InlineData("Return Touchdowns", "O", TouchdownType.Return)]
    [InlineData("Kickoff and Punt Return Touchdowns", "DT", TouchdownType.Return)]
    [InlineData("Touchdown", "DT", TouchdownType.Defensive)]
    public void Classify_MapsKnownCategories(string name, string positionType, TouchdownType expected)
    {
        Assert.Equal(expected, YahooStatMap.Classify(name, positionType));
    }

    [Theory]
    [InlineData("2-Point Conversions", "O")]
    [InlineData("Field Goals Made", "K")]
    [InlineData("Touchdown", "O")] // "Touchdown" alone with non-DT position_type isn't classifiable
    [InlineData(null, "O")]
    [InlineData("", "O")]
    public void Classify_ReturnsNullForNonTouchdownOrUnclassifiable(string? name, string positionType)
    {
        Assert.Null(YahooStatMap.Classify(name, positionType));
    }
}
