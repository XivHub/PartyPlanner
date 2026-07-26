using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using System;
using System.Linq;
using System.Threading.Tasks;
using PartyPlanner.Models;

namespace PartyPlanner.Tests;

/// <summary>
/// Integration tests against the real api.partake.gg endpoint.
/// Requires network access. Marked with [Trait("Category", "Integration")].
/// </summary>
public class ApiIntegrationTests : IDisposable
{
    private const string ApiUrl = "https://api.partake.gg/";

    private const string EventFields = @"
        id
        title
        location
        attendeeCount
        startsAt
        endsAt
        tags
        description(type: PLAIN_TEXT)
        attachments
        locationData {
          server { id name dataCenterId }
          dataCenter { id name }
        }";

    private readonly GraphQLHttpClient _client;

    public ApiIntegrationTests()
    {
        _client = new GraphQLHttpClient(ApiUrl, new NewtonsoftJsonSerializer());
        _client.HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PartyPlanner-Tests/1.0");
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEvents_ReturnsEvents()
    {
        var request = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 10, offset: 0) {" + EventFields + "} }"
        };

        var res = await _client.SendQueryAsync<EventsResponseType>(request);

        Assert.Null(res.Errors);
        Assert.NotNull(res.Data);
        Assert.NotEmpty(res.Data.Events);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEvents_EventsHaveRequiredFields()
    {
        var request = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 10, offset: 0) {" + EventFields + "} }"
        };

        var res = await _client.SendQueryAsync<EventsResponseType>(request);
        var events = res.Data.Events;

        Assert.All(events, ev =>
        {
            Assert.True(ev.Id > 0, $"Event id should be positive, got {ev.Id}");
            Assert.False(string.IsNullOrWhiteSpace(ev.Title), "Event title should not be empty");
            Assert.True(ev.StartsAt > DateTime.MinValue, "StartsAt should be set");
            Assert.True(ev.EndsAt > ev.StartsAt, "EndsAt should be after StartsAt");
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEvents_PaginationWorks()
    {
        var page0 = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 5, offset: 0) {" + EventFields + "} }"
        };
        var page1 = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 5, offset: 5) {" + EventFields + "} }"
        };

        var res0 = await _client.SendQueryAsync<EventsResponseType>(page0);
        var res1 = await _client.SendQueryAsync<EventsResponseType>(page1);

        var ids0 = res0.Data.Events.Select(e => e.Id).ToHashSet();
        var ids1 = res1.Data.Events.Select(e => e.Id).ToHashSet();

        Assert.Empty(ids0.Intersect(ids1));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetActiveEvents_OnlyReturnsCurrentlyActiveEvents()
    {
        var now = DateTime.UtcNow;
        var request = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 50, offset: 0,
                        startsBetween: { end: """ + now.ToString("o") + @""" },
                        endsBetween:   { start: """ + now.ToString("o") + @""" }
                      ) {" + EventFields + "} }"
        };

        var res = await _client.SendQueryAsync<EventsResponseType>(request);

        Assert.Null(res.Errors);
        Assert.NotNull(res.Data);

        Assert.All(res.Data.Events, ev =>
        {
            Assert.True(ev.StartsAt <= now, $"Active event {ev.Id} starts in the future");
            Assert.True(ev.EndsAt >= now, $"Active event {ev.Id} has already ended");
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEvents_NoGraphQLErrors()
    {
        var request = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 1, offset: 0) {" + EventFields + "} }"
        };

        var res = await _client.SendQueryAsync<EventsResponseType>(request);

        Assert.Null(res.Errors);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEvents_LocationDataPresentForInWorldEvents()
    {
        var request = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 50, offset: 0) {" + EventFields + "} }"
        };

        var res = await _client.SendQueryAsync<EventsResponseType>(request);
        var withLocation = res.Data.Events.Where(e => e.LocationData?.DataCenter != null).ToList();

        Assert.NotEmpty(withLocation);
        Assert.All(withLocation, ev =>
        {
            Assert.False(string.IsNullOrEmpty(ev.LocationData!.DataCenter!.Name));
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetActiveEvents_HaveStartedAndNotYetEnded()
    {
        var now = DateTime.UtcNow;

        var request = new GraphQLRequest
        {
            Query = @"{ events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 100, offset: 0,
                        startsBetween: { end: """ + now.ToString("o") + @""" },
                        endsBetween:   { start: """ + now.ToString("o") + @""" }
                      ) {" + EventFields + "} }"
        };

        var res = await _client.SendQueryAsync<EventsResponseType>(request);

        Assert.Null(res.Errors);
        Assert.NotNull(res.Data);

        // If no events are active right now that's fine — skip.
        if (res.Data.Events.Count == 0) return;

        Assert.All(res.Data.Events, ev =>
        {
            Assert.True(ev.StartsAt <= now, $"Active event {ev.Id} starts in the future ({ev.StartsAt:o})");
            Assert.True(ev.EndsAt >= now, $"Active event {ev.Id} has already ended ({ev.EndsAt:o})");
        });
    }
}
