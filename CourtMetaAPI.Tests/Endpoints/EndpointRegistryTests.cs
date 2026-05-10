using System.Linq;
using CourtMetaAPI.Services.Endpoints;
using Xunit;

namespace CourtMetaAPI.Tests.Endpoints;

public class EndpointRegistryTests
{
    [Fact]
    public void ParseableScopesAreDistinct()
    {
        var scopes = EndpointRegistry.ParseableScopes().ToArray();
        Assert.Equal(scopes.Length, scopes.Distinct().Count());
    }

    [Fact]
    public void EveryEndpointKeyIsUnique()
    {
        var keys = EndpointRegistry.All.Select(e => e.Key).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ParseableEndpointsCarryNonNullSchemaVersion()
    {
        foreach (var def in EndpointRegistry.All.Where(e => e.IsParseable))
        {
            Assert.False(string.IsNullOrEmpty(def.SchemaVersion),
                $"Parseable endpoint '{def.Key}' must declare a schemaVersion.");
            Assert.False(string.IsNullOrEmpty(def.MappingConfig));
        }
    }

    [Fact]
    public void RawOnlyEndpointsHaveNoSchemaVersion()
    {
        foreach (var def in EndpointRegistry.All.Where(e => !e.IsParseable))
        {
            Assert.Null(def.SchemaVersion);
            Assert.Null(def.MappingConfig);
        }
    }

    [Fact]
    public void CnrIsParseable()
    {
        var cnr = EndpointRegistry.ByKey("cnr");
        Assert.NotNull(cnr);
        Assert.True(cnr!.IsParseable);
        Assert.Equal("parse:cnr", cnr.ScopeName);
    }
}
