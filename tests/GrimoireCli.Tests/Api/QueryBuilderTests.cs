using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class QueryBuilderTests
{
    [Fact]
    public void ReturnsEmptyStringWhenNothingIsSet()
    {
        Assert.Equal("", QueryBuilder.Build(("sort", null), ("genre", null)));
    }

    [Fact]
    public void SkipsNullAndEmptyValues()
    {
        Assert.Equal("?sort=name", QueryBuilder.Build(("sort", "name"), ("genre", null), ("edition", "")));
    }

    [Fact]
    public void JoinsMultipleParametersWithAmpersands()
    {
        Assert.Equal("?sort=name&order=desc", QueryBuilder.Build(("sort", "name"), ("order", "desc")));
    }

    // Filter values are real system names: "Dungeons & Dragons" would otherwise
    // terminate the parameter early and silently change the query.
    [Fact]
    public void EncodesAmpersandsInValues()
    {
        Assert.Equal("?parent_system=Dungeons%20%26%20Dragons",
            QueryBuilder.Build(("parent_system", "Dungeons & Dragons")));
    }

    [Fact]
    public void EncodesSpacesAndNonAscii()
    {
        Assert.Equal("?family=Das%20Schwarze%20Auge", QueryBuilder.Build(("family", "Das Schwarze Auge")));
        Assert.Equal("?genre=Stra%C3%9Fe", QueryBuilder.Build(("genre", "Straße")));
    }

    [Fact]
    public void EncodesTheParameterNameToo()
    {
        Assert.Equal("?odd%20name=v", QueryBuilder.Build(("odd name", "v")));
    }
}
