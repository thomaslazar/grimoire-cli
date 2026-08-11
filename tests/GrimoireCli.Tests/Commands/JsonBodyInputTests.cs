using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Commands;

public class JsonBodyInputTests
{
    private const string IdHint = "pass it with --id";

    private static void Validate(string json)
        => JsonBodyInput.Validate(json, AppJsonContext.Default.GameSystemUpdateRequest, IdHint);

    [Fact]
    public void ReadsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"body-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"year":2009}""");
        try
        {
            Assert.Equal("""{"year":2009}""", JsonBodyInput.Read(path, useStdin: false));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsBothSources()
    {
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read("f.json", useStdin: true));
        Assert.Contains("not both", ex.Message);
    }

    [Fact]
    public void RejectsNeitherSource()
    {
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read(null, useStdin: false));
        Assert.Contains("--input", ex.Message);
        Assert.Contains("--stdin", ex.Message);
    }

    [Fact]
    public void RejectsAMissingFile()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json");
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read(missing, useStdin: false));
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void RejectsAnEmptyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "   \n");
        try
        {
            var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read(path, useStdin: false));
            Assert.Contains("empty", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AcceptsAValidBody()
    {
        Validate("""{"system_family":"Shadowrun","year":2009}""");
    }

    [Fact]
    public void NamesTheUnknownFieldAndSuggestsTheNearestMatch()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"system_familly":"x"}"""));
        Assert.Contains("system_familly", ex.Message);
        Assert.Contains("system_family", ex.Message);
        Assert.Contains("Did you mean", ex.Message);
    }

    [Fact]
    public void ListsTheAllowedFieldsWhenNothingIsClose()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"zzzzzz":"x"}"""));
        Assert.Contains("zzzzzz", ex.Message);
        Assert.Contains("cover_book_id", ex.Message);
    }

    [Fact]
    public void GivesIdItsOwnAdvice()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"id":"abc"}"""));
        Assert.Contains("'id'", ex.Message);
        Assert.Contains(IdHint, ex.Message);
    }

    [Fact]
    public void ReportsTheJsonPathForANestedUnknownField()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"urls":[{"lable":"x"}]}"""));
        Assert.Contains("$.urls[0].lable", ex.Message);
        Assert.DoesNotContain("Did you mean", ex.Message);
    }

    [Fact]
    public void ReportsAWrongTypeWithoutASuggestion()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"year":"soon"}"""));
        Assert.Contains("year", ex.Message);
        Assert.DoesNotContain("Did you mean", ex.Message);
    }

    [Fact]
    public void ReportsMalformedJson()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("{not json"));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void ReportsAWrongRootShape()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("[]"));
        Assert.Contains("must be a JSON object", ex.Message);
    }
}
