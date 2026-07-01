using DataGateOpenVpnManager.Services.PiHole;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleQueryParserTests
{
    [Fact]
    public void ParseQueriesResponse_ReadsV6Shape()
    {
        const string json = """
            {
              "queries": [
                {
                  "id": 42,
                  "time": 1719043200,
                  "domain": "example.com",
                  "client": { "ip": "10.51.30.5", "name": "laptop" },
                  "type": "A",
                  "status": "FORWARDED"
                }
              ]
            }
            """;

        var records = PiHoleQueryParser.ParseQueriesResponse(json);

        Assert.Single(records);
        Assert.Equal(42, records[0].PiHoleQueryId);
        Assert.Equal("10.51.30.5", records[0].ClientIp);
        Assert.Equal("example.com", records[0].Domain);
        Assert.Equal("A", records[0].QueryType);
        Assert.Equal("FORWARDED", records[0].Status);
    }

    [Fact]
    public void ParseQueriesResponse_ReadsStringClient()
    {
        const string json = """
            {
              "queries": [
                {
                  "id": 7,
                  "timestamp": 1719043300,
                  "domain": "tracker.example",
                  "client": "10.51.30.9",
                  "status": "GRAVITY"
                }
              ]
            }
            """;

        var records = PiHoleQueryParser.ParseQueriesResponse(json);

        Assert.Single(records);
        Assert.Equal("10.51.30.9", records[0].ClientIp);
        Assert.Equal("GRAVITY", records[0].Status);
    }

    [Fact]
    public void ReadSessionId_ParsesNestedSession()
    {
        const string json = """{"session":{"sid":"abc+123=","validity":1800}}""";

        Assert.Equal("abc+123=", PiHoleQueryParser.ReadSessionId(json));
    }

    [Fact]
    public void ReadRecordsTotal_ParsesPaginationField()
    {
        const string json = """{"queries":[],"recordsTotal":1234}""";

        Assert.Equal(1234, PiHoleQueryParser.ReadRecordsTotal(json));
    }
}
