using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class BookFolderDtoTests
{
    // An empty tags array is how a folder reads after being cleared, so it
    // must survive deserialization as an empty list, not null.
    [Fact]
    public void BookFolderListSurvivesAnEmptyTagList()
    {
        const string json = """
        {"folders": [{"path": "5/core/Curse of Strahd", "tags": ["Horror", "Ravenloft"]},
                     {"path": "5/adventure/One Shots", "tags": []}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookFolderList)!;
        Assert.Equal(2, result.Folders!.Count);
        Assert.Equal(["Horror", "Ravenloft"], result.Folders[0].Tags);
        Assert.Empty(result.Folders[1].Tags!);
    }

    [Fact]
    public void BookFolderUpdatedReadsPathAndTags()
    {
        const string json = """{"path": "5/core/Curse of Strahd", "tags": ["horror", "ravenloft"]}""";
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookFolderUpdated)!;
        Assert.Equal("5/core/Curse of Strahd", result.Path);
        Assert.Equal(["horror", "ravenloft"], result.Tags);
    }
}
