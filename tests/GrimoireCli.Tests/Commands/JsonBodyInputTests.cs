using GrimoireCli.Commands;
using GrimoireCli.Generated.Models;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// The allowed field names come from the generated models, so these tests are also
/// what proves the generator's own field lists are usable for validation — a
/// regeneration that dropped a model's properties (microsoft/kiota#2338) would
/// surface here as a valid body being refused.
/// </summary>
public class JsonBodyInputTests
{
    private const string IdHint = "pass it with --id";

    private static void Validate(string json)
        => JsonBodyInput.Validate(json, GameSystemUpdate.CreateFromDiscriminatorValue, IdHint);

    private static void ValidateBatch(string json)
        => JsonBodyInput.Validate(json, GameSystemBulkUpdate.CreateFromDiscriminatorValue, "put it in each item");

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
    public void RejectsAMalformedPathWithoutACrash()
    {
        // File.ReadAllText("") throws ArgumentException, not IOException — a
        // widened catch is what turns this into a readable refusal instead of an
        // unhandled exception and a stack trace on stderr.
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read("", useStdin: false));
        Assert.Contains("Could not read", ex.Message);
    }

    [Fact]
    public void RejectsEmptyStdin()
    {
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read(null, useStdin: true, stdin: new StringReader("   ")));
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void ReadsNonAsciiStdinIntact()
    {
        const string body = """{"description":"Über Straße — café"}""";
        Assert.Equal(body, JsonBodyInput.Read(null, useStdin: true, stdin: new StringReader(body)));
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

    // The nested entry types describe their own fields, so the suggestion is drawn
    // from the model that actually rejected the key rather than from the root.
    [Fact]
    public void ReportsTheJsonPathAndSuggestionForANestedUnknownField()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"urls":[{"lable":"x"}]}"""));
        Assert.Contains("$.urls[0].lable", ex.Message);
        Assert.Contains("label", ex.Message);
    }

    [Fact]
    public void RejectsAnUnknownFieldInsideAPublisher()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"publishers":[{"nmae":"Pegasus"}]}"""));
        Assert.Contains("$.publishers[0].nmae", ex.Message);
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void AcceptsValidNestedEntries()
    {
        Validate("""{"publishers":[{"name":"Pegasus Spiele","url":""}],"urls":[{"label":"Site","url":"https://s"}]}""");
    }

    [Fact]
    public void RejectsAnUnknownFieldInsideABatchItem()
    {
        var ex = Assert.Throws<BodyInputException>(() => ValidateBatch("""{"items":[{"id":"a","yaer":1}]}"""));
        Assert.Contains("$.items[0].yaer", ex.Message);
        Assert.Contains("year", ex.Message);
    }

    [Fact]
    public void AcceptsAValidBatchBody()
    {
        ValidateBatch("""{"items":[{"id":"a","year":2009},{"id":"b","genres":["Fantasy"]}]}""");
    }

    // An id is legitimate inside a batch item, so the advice about --id must not
    // fire there — only where the endpoint really takes the id elsewhere.
    [Fact]
    public void DoesNotMisreadAnIdInsideABatchItem()
    {
        ValidateBatch("""{"items":[{"id":"a"}]}""");
    }

    // A value of the wrong type is a 422 from the server, which reports it — unlike
    // an unknown key, which the server drops silently. Nothing is refused here.
    [Fact]
    public void LeavesAWrongTypeToTheServer()
    {
        Validate("""{"year":"soon"}""");
    }

    // Clearing a field is "", and an explicit null is a no-op server-side: both are
    // legal bodies and neither may be refused.
    [Fact]
    public void AcceptsEmptyStringsAndExplicitNulls()
    {
        Validate("""{"system_family":"","description":null}""");
    }

    // The spec normalization that makes the nested models generate at all drops the
    // "or null" branch of every array property, so an explicit null for one must be
    // proven still legal rather than assumed.
    [Fact]
    public void AcceptsNullAndEmptyArrays()
    {
        Validate("""{"publishers":null,"urls":null}""");
        Validate("""{"publishers":[]}""");
    }

    [Fact]
    public void ReportsEveryUnknownFieldNotJustTheFirst()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"nmae":"x","yaer":1}"""));
        Assert.Contains("1 more", ex.Message);
        Assert.Contains("yaer", ex.Message);
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
