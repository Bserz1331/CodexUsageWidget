using System;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class UsageReaderTests
{
    [Fact]
    public void ParseLine_ReadsWeeklyPlusLimit()
    {
        const string json = """
        {
          "timestamp": "2026-07-29T14:00:00Z",
          "type": "event_msg",
          "payload": {
            "type": "token_count",
            "rate_limits": {
              "limit_id": "codex",
              "plan_type": "plus",
              "primary": {
                "used_percent": 45,
                "window_minutes": 10080,
                "resets_at": 1785903328
              },
              "secondary": null
            }
          }
        }
        """;

        UsageInfo result = UsageReader.ParseLine(json);

        Assert.NotNull(result);
        Assert.Equal("plus", result.Plan);
        Assert.Equal(45, result.UsedPercent);
        Assert.Equal(10080, result.WindowMinutes);
        Assert.NotEqual(DateTime.MinValue, result.ResetAt);
    }

    [Fact]
    public void ParseLine_ReadsShortWindowAlongsideWeeklyWindow()
    {
        const string json = """
        {
          "timestamp": "2026-07-29T14:00:00Z",
          "payload": {
            "type": "token_count",
            "rate_limits": {
              "plan_type": "plus",
              "primary": { "used_percent": 18, "window_minutes": 300, "resets_at": 1785900000 },
              "secondary": { "used_percent": 55, "window_minutes": 10080, "resets_at": 1785903328 }
            }
          }
        }
        """;

        UsageInfo result = UsageReader.ParseLine(json);

        Assert.NotNull(result);
        Assert.Equal(55, result.UsedPercent);
        Assert.Equal(18, result.ShortUsedPercent);
        Assert.Equal(300, result.ShortWindowMinutes);
    }

    [Fact]
    public void ParseLine_RejectsNonWeeklyOnlyWindow()
    {
        const string json = """
        {
          "timestamp": "2026-07-29T14:00:00Z",
          "payload": {
            "type": "token_count",
            "rate_limits": {
              "plan_type": "plus",
              "primary": { "used_percent": 18, "window_minutes": 300, "resets_at": 1785900000 },
              "secondary": null
            }
          }
        }
        """;

        Assert.Null(UsageReader.ParseLine(json));
    }

    [Fact]
    public void ParseLine_IgnoresUnrelatedEvents()
    {
        const string json = """
        {
          "timestamp": "2026-07-29T14:00:00Z",
          "payload": { "type": "user_message", "message": "hello" }
        }
        """;

        Assert.Null(UsageReader.ParseLine(json));
    }

    [Fact]
    public void ParseLine_RejectsUnknownLongWindow()
    {
        const string json = """
        {
          "timestamp": "2026-07-29T14:00:00Z",
          "payload": {
            "type": "token_count",
            "rate_limits": {
              "plan_type": "plus",
              "primary": { "used_percent": 45, "window_minutes": 43200, "resets_at": 1785900000 }
            }
          }
        }
        """;

        Assert.Null(UsageReader.ParseLine(json));
    }

    [Fact]
    public void ParseLine_RejectsInvalidPercentage()
    {
        const string json = """
        {
          "timestamp": "2026-07-29T14:00:00Z",
          "payload": {
            "type": "token_count",
            "rate_limits": {
              "plan_type": "plus",
              "primary": { "used_percent": 145, "window_minutes": 10080, "resets_at": 1785900000 }
            }
          }
        }
        """;

        Assert.Null(UsageReader.ParseLine(json));
    }

    [Fact]
    public void ParseLine_RejectsMissingPercentage()
    {
        const string json = """
        { "timestamp":"2026-07-29T14:00:00Z", "payload": { "type":"token_count",
          "rate_limits": { "primary": { "window_minutes":10080, "resets_at":1785900000 } } } }
        """;

        Assert.Null(UsageReader.ParseLine(json));
    }

    [Fact]
    public void ParseLine_RejectsInvalidTimestamp()
    {
        const string json = """
        { "timestamp":"not-a-time", "payload": { "type":"token_count",
          "rate_limits": { "primary": { "used_percent":45, "window_minutes":10080, "resets_at":1785900000 } } } }
        """;

        Assert.Null(UsageReader.ParseLine(json));
    }
}
