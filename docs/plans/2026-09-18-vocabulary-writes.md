# vocabulary writes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `create` and `delete` to all five lookup vocabularies — ten admin-only commands behind [#43](https://github.com/thomaslazar/grimoire-cli/issues/43).

**Architecture:** Ten thin pass-throughs. Each of the five existing groups keeps its own command file and its own service; no shared builder and no table-driven command factory, which is the settled convention here. The generated `GenreCreate`, `SystemFamilyCreate`, `ParentSystemCreate`, `LicenseCreate` and `DiceMaterialCreate` models carry the bodies.

**Tech Stack:** C# / .NET, System.CommandLine, Kiota-generated client, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-18-vocabulary-writes-design.md](../specs/2026-09-18-vocabulary-writes-design.md)

## Global Constraints

- Branch: `feat/vocabulary-writes`, cut from `main`. Never commit to `main`.
- Conventional Commits, imperative, lowercase, no period, ~72 chars. No `Co-Authored-By:` and no "Generated with Claude Code" lines.
- The spec and this plan are committed **on the feature branch**, in Task 1's commit.
- Every one of the ten commands calls `command.AddRoleRequired("admin")` immediately after construction, and its service send passes `permissionHint: "the admin role"`. The two must agree.
- Run `dotnet format GrimoireCli.sln` after touching any C# file.
- Help text is terse: one-liners, no prose, nothing already visible from the flags or the rendered response shape.
- **No shared helper across the five groups.** Near-identical code in five files is the convention, not a smell to refactor.
- Never touch `CHANGELOG.md` (release-process owned) or `docs/roadmap.md` in Tasks 1-4; the roadmap item is removed in Task 4 only, as specified there.
- Writes go to the local Docker stack only, never any other instance.

---

### Task 1: The ten service sends

**Files:**
- Modify: `src/GrimoireCli/Services/GenresService.cs`, `LicensesService.cs`, `ParentSystemsService.cs`, `SystemFamiliesService.cs`, `DiceMaterialsService.cs`
- Test: `tests/GrimoireCli.Tests/Services/VocabularyServiceTests.cs`
- Commit alongside: `docs/specs/2026-09-18-vocabulary-writes-design.md`, `docs/plans/2026-09-18-vocabulary-writes.md`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, string? permissionHint, string? notFoundHint)`, and each service's existing `internal RequestInformation ListRequest()` pattern.
- Produces, on every one of the five services: `CreateAsync(...)`, `DeleteAsync(string id, bool force)`, `internal RequestInformation CreateRequest(...)`, `internal RequestInformation DeleteRequest(string id, bool force)`. `GenresService.CreateAsync` takes `(string name, string? parentId)`; `DiceMaterialsService.CreateAsync` takes `(string name, string? group)`; the other three take `(string name)`. Task 2 calls exactly these names.

- [ ] **Step 1: Cut the branch**

```bash
git checkout -b feat/vocabulary-writes
```

- [ ] **Step 2: Write the failing tests**

Append inside the existing `VocabularyServiceTests` class. It already has `Client()` and the `Vocabularies()` theory data; add these members below `NoQueryStringIsSent`:

```csharp
    private static RequestInformation CreateRequest(string vocabulary)
    {
        var client = Client();
        return vocabulary switch
        {
            "genres" => new GenresService(client).CreateRequest("Solo", parentId: null),
            "licenses" => new LicensesService(client).CreateRequest("OGL"),
            "parent-systems" => new ParentSystemsService(client).CreateRequest("D20"),
            "system-families" => new SystemFamiliesService(client).CreateRequest("DSA"),
            "dice-materials" => new DiceMaterialsService(client).CreateRequest("Oak", group: null),
            _ => throw new ArgumentException($"Unknown vocabulary '{vocabulary}'.", nameof(vocabulary)),
        };
    }

    private static RequestInformation DeleteRequest(string vocabulary, bool force)
    {
        var client = Client();
        return vocabulary switch
        {
            "genres" => new GenresService(client).DeleteRequest("v1", force),
            "licenses" => new LicensesService(client).DeleteRequest("v1", force),
            "parent-systems" => new ParentSystemsService(client).DeleteRequest("v1", force),
            "system-families" => new SystemFamiliesService(client).DeleteRequest("v1", force),
            "dice-materials" => new DiceMaterialsService(client).DeleteRequest("v1", force),
            _ => throw new ArgumentException($"Unknown vocabulary '{vocabulary}'.", nameof(vocabulary)),
        };
    }

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    // A service copied from its neighbour without swapping the builder would
    // write to the wrong vocabulary, which nothing else catches: every one of
    // these responses has the same shape.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachCreateResolvesToItsOwnPath(string vocabulary, string expectedPath)
    {
        Assert.Equal("http://example.test" + expectedPath, Uri(CreateRequest(vocabulary)));
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachDeleteResolvesToItsOwnPath(string vocabulary, string expectedPath)
    {
        Assert.Contains(expectedPath + "/v1", Uri(DeleteRequest(vocabulary, force: false)));
    }

    // force is the one query parameter these writes send; a regeneration that
    // renamed it would leave a flag the server ignores — a delete that 409s
    // when the caller asked it not to.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void DeleteSendsForceAsAQueryParameter(string vocabulary, string expectedPath)
    {
        _ = expectedPath;
        Assert.Contains("force=true", Uri(DeleteRequest(vocabulary, force: true)));
        Assert.Contains("force=false", Uri(DeleteRequest(vocabulary, force: false)));
    }

    // Both are composed-type wrappers whose constructors set nothing, so an
    // omitted flag must stay absent from the body: the server defaults group to
    // "Custom", and a null parent_id is what makes a top-level genre.
    [Fact]
    public void OmittedGenreParentLeavesTheBodyWithoutIt()
    {
        var body = GenresService.BuildCreateBody("Solo", parentId: null);
        Assert.Equal("Solo", body.Name);
        Assert.Null(body.ParentId);
    }

    [Fact]
    public void GivenGenreParentReachesTheBodyThroughTheComposedWrapper()
    {
        Assert.Equal("g1", GenresService.BuildCreateBody("Solo", "g1").ParentId!.String);
    }

    [Fact]
    public void OmittedDiceGroupLeavesTheBodyWithoutIt()
    {
        var body = DiceMaterialsService.BuildCreateBody("Oak", group: null);
        Assert.Equal("Oak", body.Name);
        Assert.Null(body.Group);
    }

    [Fact]
    public void GivenDiceGroupReachesTheBodyThroughTheComposedWrapper()
    {
        Assert.Equal("Wood", DiceMaterialsService.BuildCreateBody("Oak", "Wood").Group!.String);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter VocabularyServiceTests`
Expected: compile error — `CreateRequest`, `DeleteRequest` and `BuildCreateBody` do not exist.

- [ ] **Step 4: Add the sends to `GenresService.cs`**

Add the constants beside the existing field, and the methods below `ListRequest()`:

```csharp
    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No genre with that ID. List them with: grimoire-cli genres list";

    /// <summary>
    /// POST /api/genres. 409s on a case-insensitive duplicate name. Its only
    /// 404 names an unknown parent, so the hint can say so.
    /// </summary>
    public async Task<string> CreateAsync(string name, string? parentId)
        => await _client.SendAsync(
            CreateRequest(name, parentId),
            permissionHint: AdminHint,
            notFoundHint: "No genre with that --parent-id. List them with: grimoire-cli genres list");

    internal RequestInformation CreateRequest(string name, string? parentId)
        => _client.Api.Api.Genres.ToPostRequestInformation(BuildCreateBody(name, parentId));

    /// <summary>
    /// ParentId is a composed-type wrapper whose constructor sets nothing, so
    /// assigning through it only when the flag was given leaves an omitted one
    /// absent from the body — which is what makes a top-level genre. Internal so
    /// a test can pin that a client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.GenreCreate BuildCreateBody(string name, string? parentId)
    {
        var body = new Generated.Models.GenreCreate { Name = name };
        if (parentId is not null)
            body.ParentId = new Generated.Models.GenreCreate.GenreCreate_parent_id { String = parentId };
        return body;
    }

    /// <summary>
    /// DELETE /api/genres/{id}. 409s while the name is in use unless force; a
    /// forced delete removes the row only and cascades to child genres.
    /// </summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.Genres[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
```

- [ ] **Step 5: Add the sends to `DiceMaterialsService.cs`**

```csharp
    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No dice/material with that ID. List them with: grimoire-cli dice-materials list";

    /// <summary>POST /api/dice-materials. 409s on a case-insensitive duplicate name.</summary>
    public async Task<string> CreateAsync(string name, string? group)
        => await _client.SendAsync(CreateRequest(name, group), permissionHint: AdminHint);

    internal RequestInformation CreateRequest(string name, string? group)
        => _client.Api.Api.DiceMaterials.ToPostRequestInformation(BuildCreateBody(name, group));

    /// <summary>
    /// Group is a composed-type wrapper whose constructor sets nothing, so
    /// assigning through it only when the flag was given leaves an omitted one
    /// absent from the body and lets the server apply its own "Custom" default.
    /// Internal so a test can pin that a client regeneration cannot change it.
    /// </summary>
    internal static Generated.Models.DiceMaterialCreate BuildCreateBody(string name, string? group)
    {
        var body = new Generated.Models.DiceMaterialCreate { Name = name };
        if (group is not null)
            body.Group = new Generated.Models.DiceMaterialCreate.DiceMaterialCreate_group { String = group };
        return body;
    }

    /// <summary>DELETE /api/dice-materials/{id}. 409s while the name is in use unless force.</summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.DiceMaterials[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
```

- [ ] **Step 6: Add the sends to `LicensesService.cs`**

```csharp
    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No license with that ID. List them with: grimoire-cli licenses list";

    /// <summary>POST /api/licenses. 409s on a case-insensitive duplicate name.</summary>
    public async Task<string> CreateAsync(string name)
        => await _client.SendAsync(CreateRequest(name), permissionHint: AdminHint);

    internal RequestInformation CreateRequest(string name)
        => _client.Api.Api.Licenses.ToPostRequestInformation(
            new Generated.Models.LicenseCreate { Name = name });

    /// <summary>DELETE /api/licenses/{id}. 409s while the name is in use unless force.</summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.Licenses[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
```

- [ ] **Step 7: Add the sends to `ParentSystemsService.cs`**

```csharp
    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No parent system with that ID. List them with: grimoire-cli parent-systems list";

    /// <summary>POST /api/parent-systems. 409s on a case-insensitive duplicate name.</summary>
    public async Task<string> CreateAsync(string name)
        => await _client.SendAsync(CreateRequest(name), permissionHint: AdminHint);

    internal RequestInformation CreateRequest(string name)
        => _client.Api.Api.ParentSystems.ToPostRequestInformation(
            new Generated.Models.ParentSystemCreate { Name = name });

    /// <summary>DELETE /api/parent-systems/{id}. 409s while the name is in use unless force.</summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.ParentSystems[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
```

- [ ] **Step 8: Add the sends to `SystemFamiliesService.cs`**

```csharp
    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No system family with that ID. List them with: grimoire-cli system-families list";

    /// <summary>POST /api/system-families. 409s on a case-insensitive duplicate name.</summary>
    public async Task<string> CreateAsync(string name)
        => await _client.SendAsync(CreateRequest(name), permissionHint: AdminHint);

    internal RequestInformation CreateRequest(string name)
        => _client.Api.Api.SystemFamilies.ToPostRequestInformation(
            new Generated.Models.SystemFamilyCreate { Name = name });

    /// <summary>DELETE /api/system-families/{id}. 409s while the name is in use unless force.</summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.SystemFamilies[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
```

Each of the five files needs `using Microsoft.Kiota.Abstractions;` for `RequestInformation`; `ParentSystemsService.cs` already has it, the others may not. Update each class's doc comment so it describes the writes as well as the read — the existing comments say the service has one parameterless read that names no hints, which stops being true here.

- [ ] **Step 9: Format, build, run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter VocabularyServiceTests
```

Expected: build clean, all `VocabularyServiceTests` pass.

- [ ] **Step 10: Commit**

```bash
git add docs/specs/2026-09-18-vocabulary-writes-design.md docs/plans/2026-09-18-vocabulary-writes.md \
        src/GrimoireCli/Services/GenresService.cs src/GrimoireCli/Services/LicensesService.cs \
        src/GrimoireCli/Services/ParentSystemsService.cs src/GrimoireCli/Services/SystemFamiliesService.cs \
        src/GrimoireCli/Services/DiceMaterialsService.cs \
        tests/GrimoireCli.Tests/Services/VocabularyServiceTests.cs
git commit -m "feat: add the vocabulary create and delete sends"
```

---

### Task 2: The ten subcommands

**Files:**
- Modify: `src/GrimoireCli/Commands/GenresCommand.cs`, `LicensesCommand.cs`, `ParentSystemsCommand.cs`, `SystemFamiliesCommand.cs`, `DiceMaterialsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/VocabularyCommandTests.cs`

**Interfaces:**
- Consumes: from Task 1 — `GenresService.CreateAsync(string name, string? parentId)`, `DiceMaterialsService.CreateAsync(string name, string? group)`, `CreateAsync(string name)` on the other three, and `DeleteAsync(string id, bool force)` on all five.
- Produces: `create` and `delete` subcommands on each of the five groups, registered after `list` in that order.

**The shared Notes text.** All five `delete` commands carry the same three paragraphs, with one extra line on `genres`. Write it out in each file; do not extract a helper.

- [ ] **Step 1: Update the two existing tests that this task breaks**

In `tests/GrimoireCli.Tests/Commands/VocabularyCommandTests.cs`, replace `EachGroupHasExactlyOneListSubcommand` with:

```csharp
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachGroupHostsTheReadThenTheWrites(string name)
    {
        Assert.Equal(["list", "create", "delete"], Group(name).Subcommands.Select(c => c.Name).ToArray());
    }
```

and replace `AnUnknownSubcommandErrors`'s body — `create --name x` is a real subcommand now:

```csharp
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void AnUnknownSubcommandErrors(string name)
    {
        Assert.NotEmpty(Group(name).Parse(["rename", "--name", "x"]).Errors);
    }
```

- [ ] **Step 2: Write the new failing tests**

Append inside the same class:

```csharp
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void BothWritesDeclareTheAdminRole(string name)
    {
        foreach (var leaf in new[] { "create", "delete" })
        {
            var help = HelpRenderer.Render(Group(name), [name, leaf], full: false);
            Assert.Contains("Role required:", help);
            Assert.Contains("admin", help);
        }
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void CreateRequiresAName(string name)
    {
        Assert.NotEmpty(Group(name).Parse(["create"]).Errors);
        Assert.Empty(Group(name).Parse(["create", "--name", "Solo"]).Errors);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void DeleteRequiresAnIdAndTakesForce(string name)
    {
        Assert.NotEmpty(Group(name).Parse(["delete"]).Errors);
        Assert.Empty(Group(name).Parse(["delete", "--id", "v1"]).Errors);
        Assert.Empty(Group(name).Parse(["delete", "--id", "v1", "--force"]).Errors);
    }

    // removed_usage reads as though a forced delete cleaned the value off every
    // system and book. It does the opposite, and that is the whole reason these
    // Notes exist.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void DeleteWarnsThatForceStripsNothing(string name)
    {
        var help = HelpRenderer.Render(Group(name), [name, "delete"], full: false);
        Assert.Contains("removed_usage", help);
        Assert.Contains("keep the value", help);
    }

    // No delete handler checks is_default, and the defaults are seeded by a
    // one-time migration, so this is unrecoverable.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void DeleteWarnsThatBuiltInsAreDeletable(string name)
    {
        var help = HelpRenderer.Render(Group(name), [name, "delete"], full: false);
        Assert.Contains("Built-in entries", help);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void BothWritesCarryAResponseShape(string name)
    {
        foreach (var leaf in new[] { "create", "delete" })
            Assert.Contains("Response shape:", HelpRenderer.Render(Group(name), [name, leaf], full: true));
    }

    [Fact]
    public void OnlyGenresTakesAParent()
    {
        Assert.Empty(GenresCommand.Create().Parse(["create", "--name", "Solo", "--parent-id", "g1"]).Errors);
        Assert.NotEmpty(LicensesCommand.Create().Parse(["create", "--name", "OGL", "--parent-id", "g1"]).Errors);
    }

    [Fact]
    public void OnlyDiceMaterialsTakesAGroup()
    {
        Assert.Empty(DiceMaterialsCommand.Create().Parse(["create", "--name", "Oak", "--group", "Wood"]).Errors);
        Assert.NotEmpty(LicensesCommand.Create().Parse(["create", "--name", "OGL", "--group", "Wood"]).Errors);
    }

    [Fact]
    public void OnlyGenresDeleteMentionsChildren()
    {
        Assert.Contains("child genres", HelpRenderer.Render(GenresCommand.Create(), ["genres", "delete"], full: false));
        Assert.DoesNotContain("child", HelpRenderer.Render(LicensesCommand.Create(), ["licenses", "delete"], full: false));
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter VocabularyCommandTests`
Expected: FAIL — the subcommand-list assertion fails and every write help render is empty.

- [ ] **Step 4: Add `create` and `delete` to `GenresCommand.cs`**

Register them in `Create()`, after the existing `CreateListCommand()` line:

```csharp
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
```

Then add the two builders:

```csharp
    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The genre's name", Required = true };
        var parentOption = new Option<string?>("--parent-id") { Description = "Nest under this genre, by id from genres list" };
        var command = new Command("create", "Create a genre")
        {
            nameOption, parentOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a genre does not make it enforced: nothing validates a",
            "system's or book's genres against this list.");
        command.AddExamples(
            "grimoire-cli genres create --name \"Solo\"",
            "grimoire-cli genres create --name \"Solo\" --parent-id <genre-id>");
        command.AddResponseExample<Generated.Models.GenreOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new GenresService(client);
            var result = await service.CreateAsync(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(parentOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The genre's id, from genres list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a genre")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems and books",
            "that keep the value, stored as a plain string rather than a",
            "reference. Child genres are deleted with the parent.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli genres delete --id <genre-id>",
            "grimoire-cli genres delete --id <genre-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new GenresService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 5: Add `create` and `delete` to `DiceMaterialsCommand.cs`**

Register both in `Create()` as in Step 4, then:

```csharp
    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The dice/material's name", Required = true };
        var groupOption = new Option<string?>("--group") { Description = "Picker grouping label; the server uses Custom when omitted" };
        var command = new Command("create", "Create a dice/material")
        {
            nameOption, groupOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's dice_materials against this list.");
        command.AddExamples(
            "grimoire-cli dice-materials create --name \"Oak\"",
            "grimoire-cli dice-materials create --name \"Oak\" --group \"Wood\"");
        command.AddResponseExample<Generated.Models.DiceMaterialOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new DiceMaterialsService(client);
            var result = await service.CreateAsync(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(groupOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The dice/material's id, from dice-materials list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a dice/material")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems that keep",
            "the value, stored as a plain string rather than a reference.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli dice-materials delete --id <material-id>",
            "grimoire-cli dice-materials delete --id <material-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new DiceMaterialsService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 6: Add `create` and `delete` to `LicensesCommand.cs`**

```csharp
    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The license's name", Required = true };
        var command = new Command("create", "Create a license")
        {
            nameOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's or book's license against this list.");
        command.AddExamples("grimoire-cli licenses create --name \"OGL 1.0a\"");
        command.AddResponseExample<Generated.Models.LookupOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new LicensesService(client);
            var result = await service.CreateAsync(parseResult.GetValue(nameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The license's id, from licenses list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a license")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems and books",
            "that keep the value, stored as a plain string rather than a",
            "reference.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli licenses delete --id <license-id>",
            "grimoire-cli licenses delete --id <license-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new LicensesService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 7: Add `create` and `delete` to `ParentSystemsCommand.cs`**

```csharp
    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The parent system's name", Required = true };
        var command = new Command("create", "Create a parent system")
        {
            nameOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's parent_system against this list.");
        command.AddExamples("grimoire-cli parent-systems create --name \"D20\"");
        command.AddResponseExample<Generated.Models.LookupOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ParentSystemsService(client);
            var result = await service.CreateAsync(parseResult.GetValue(nameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The parent system's id, from parent-systems list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a parent system")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems that keep",
            "the value, stored as a plain string rather than a reference.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli parent-systems delete --id <parent-id>",
            "grimoire-cli parent-systems delete --id <parent-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ParentSystemsService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 8: Add `create` and `delete` to `SystemFamiliesCommand.cs`**

```csharp
    private static Command CreateCreateCommand()
    {
        var nameOption = new Option<string>("--name") { Description = "The system family's name", Required = true };
        var command = new Command("create", "Create a system family")
        {
            nameOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 when the name already exists, matched case-insensitively.",
            "",
            "Creating a value does not make it enforced: nothing validates a",
            "system's system_family against this list.");
        command.AddExamples("grimoire-cli system-families create --name \"DSA\"");
        command.AddResponseExample<Generated.Models.LookupOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemFamiliesService(client);
            var result = await service.CreateAsync(parseResult.GetValue(nameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "The system family's id, from system-families list", Required = true };
        var forceOption = new Option<bool>("--force") { Description = "Delete even while the name is in use" };
        var command = new Command("delete", "Delete a system family")
        {
            idOption, forceOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "409 while the name is in use; the body carries usage_count and name.",
            "",
            "--force strips nothing: removed_usage counts the systems that keep",
            "the value, stored as a plain string rather than a reference.",
            "",
            "Built-in entries are deletable and cannot be restored: the defaults",
            "are seeded once by migration, and create returns a new id with",
            "is_default false.");
        command.AddExamples(
            "grimoire-cli system-families delete --id <family-id>",
            "grimoire-cli system-families delete --id <family-id> --force");
        command.AddResponseExample<Generated.Models.LookupDeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemFamiliesService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(forceOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

Every one of the five `create`/`delete` pairs is written out above rather than
cross-referenced. Keep it that way: near-identical code in five files is the
convention here, and a substitution instruction is how the five quietly drift
apart.

- [ ] **Step 9: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green.

- [ ] **Step 10: Read the rendered help, not the source**

```bash
for g in genres licenses parent-systems system-families dice-materials; do
  dotnet run --project src/GrimoireCli -- $g create --help
  dotnet run --project src/GrimoireCli -- $g delete --help
done
```

Check each: the Role required section renders "admin"; no Notes line restates a flag's own description or a field already visible in the response sample. Trim any line that fails that, except a line an assertion in Step 2 depends on.

- [ ] **Step 11: Commit**

```bash
git add src/GrimoireCli/Commands/GenresCommand.cs src/GrimoireCli/Commands/LicensesCommand.cs \
        src/GrimoireCli/Commands/ParentSystemsCommand.cs src/GrimoireCli/Commands/SystemFamiliesCommand.cs \
        src/GrimoireCli/Commands/DiceMaterialsCommand.cs \
        tests/GrimoireCli.Tests/Commands/VocabularyCommandTests.cs
git commit -m "feat: add create and delete on all five vocabularies"
```

---

### Task 3: Smoke coverage and the two live checks

**Files:**
- Modify: `docker/smoke-test.sh` (append a `--- vocabulary writes ---` block before the final `echo "smoke: all checks passed"`)

**Interfaces:**
- Consumes: the ten commands from Task 2, and the script's existing `$CLI`, `$WORK`, `ok`, `fail` helpers.
- Produces: the two live findings Task 4 records in the docs.

**Constraint:** the block must converge on a re-run — fixed names it invents itself, deleted before the block ends. It must never delete or force-delete a built-in entry, and never write vocabulary values onto fixture metadata it did not set itself.

- [ ] **Step 1: Bring up and seed the stack, if it is not already up**

```bash
docker compose -f docker/docker-compose.yml ps
# only if it is down:
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

- [ ] **Step 2: Run the two live checks by hand and record the answers**

```bash
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli   # build first; smoke-test.sh logs you in

# (a) is_default is not protected. Create a throwaway, confirm it reads
# is_default false, and check the server never refuses a delete on that ground.
$CLI licenses create --name "Smoke Default Probe"
$CLI licenses list | jq '.licenses[] | select(.name == "Smoke Default Probe")'
PROBE=$($CLI licenses list | jq -r '.licenses[] | select(.name == "Smoke Default Probe") | .id')
$CLI licenses delete --id "$PROBE"; echo "exit=$?"

# (b) a forced delete leaves the string behind. Apply a throwaway license to
# Shadowrun 4 DE -- the one system the smoke test already owns writes to -- then
# force-delete the vocabulary entry and read the system back.
SR4=$($CLI systems list | jq -r '.[] | select(.name == "Shadowrun 4 DE") | .id')
BEFORE=$($CLI systems get --id "$SR4" | jq -r '.license // ""')
echo "restore license to: [$BEFORE]"          # note this down before going on
$CLI licenses create --name "Smoke Force Probe"
FORCED=$($CLI licenses list | jq -r '.licenses[] | select(.name == "Smoke Force Probe") | .id')
printf '{"license":"Smoke Force Probe"}' | $CLI systems update --id "$SR4" --stdin
$CLI licenses delete --id "$FORCED"; echo "unforced exit=$? (expect 409, body naming usage_count)"
$CLI licenses delete --id "$FORCED" --force; echo "forced exit=$? (note removed_usage)"
$CLI systems get --id "$SR4" | jq -r .license   # expect "Smoke Force Probe" still there
printf '{"license":"%s"}' "$BEFORE" | $CLI systems update --id "$SR4" --stdin
$CLI systems get --id "$SR4" | jq -r '.license // ""'   # confirm it is back to $BEFORE
```

The restore is part of the check, not cleanup to do if you remember: the final
`systems get` above must show `$BEFORE` before you move on. Record every exit code and body in the scratchpad; Task 4 turns them into `docs/grimoire-api-notes.md` lines. Report what you actually observed, including any case where the server disagrees with the prediction above.

- [ ] **Step 3: Append the smoke block**

Insert before the final `echo "smoke: all checks passed" >&2`:

```bash
# --- vocabulary writes -------------------------------------------------------
# Fixed names, invented here and deleted before the block ends, so a re-run
# converges. Nothing here touches a built-in entry or a fixture's own metadata.
for VOCAB in genres licenses parent-systems system-families; do
  case "$VOCAB" in
    genres) LISTKEY=genres ;;
    licenses) LISTKEY=licenses ;;
    parent-systems) LISTKEY=parent_systems ;;
    system-families) LISTKEY=families ;;
  esac
  CREATED=$("$CLI" "$VOCAB" create --name "Smoke Vocab" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "$VOCAB create exited non-zero"; }
  [ "$(echo "$CREATED" | jq -r .name)" = "Smoke Vocab" ] \
    || fail "$VOCAB create should echo the name: $CREATED"
  [ "$(echo "$CREATED" | jq -r .is_default)" = "false" ] \
    || fail "$VOCAB create should mark the entry custom: $CREATED"
  VID=$(echo "$CREATED" | jq -r .id)

  "$CLI" "$VOCAB" create --name "smoke vocab" >/dev/null 2>&1 \
    && fail "$VOCAB create should refuse a case-insensitive duplicate"

  DELETED=$("$CLI" "$VOCAB" delete --id "$VID" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "$VOCAB delete exited non-zero"; }
  [ "$(echo "$DELETED" | jq -r .removed_usage)" = "0" ] \
    || fail "$VOCAB delete of an unused entry should report no usage: $DELETED"
  "$CLI" "$VOCAB" list 2>"$WORK/cli.err" \
    | jq -e --arg k "$LISTKEY" '[.[$k][].name] | index("Smoke Vocab") == null' >/dev/null \
    || fail "$VOCAB should no longer list the deleted entry"
  ok "$VOCAB create and delete round-trip"
done

# dice-materials carries an extra field, so it is checked on its own rather than
# in the loop above.
DICE=$("$CLI" dice-materials create --name "Smoke Vocab" --group "Smoke" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "dice-materials create exited non-zero"; }
[ "$(echo "$DICE" | jq -r .group)" = "Smoke" ] \
  || fail "dice-materials create should keep the group it was given: $DICE"
DICE_ID=$(echo "$DICE" | jq -r .id)
"$CLI" dice-materials delete --id "$DICE_ID" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "dice-materials delete exited non-zero"; }
ok "dice-materials create keeps its group and deletes cleanly"

DICE=$("$CLI" dice-materials create --name "Smoke Vocab" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "dice-materials create exited non-zero"; }
[ "$(echo "$DICE" | jq -r .group)" = "Custom" ] \
  || fail "an omitted --group should leave the server's Custom default: $DICE"
"$CLI" dice-materials delete --id "$(echo "$DICE" | jq -r .id)" >/dev/null 2>&1 \
  || fail "dice-materials delete exited non-zero"
ok "an omitted --group leaves the server's default"

# The genre cascade: a child goes with its parent, and no other vocabulary has
# this behaviour to check.
PARENT=$("$CLI" genres create --name "Smoke Parent" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "genres create exited non-zero"; }
PARENT_ID=$(echo "$PARENT" | jq -r .id)
CHILD=$("$CLI" genres create --name "Smoke Child" --parent-id "$PARENT_ID" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "genres create --parent-id exited non-zero"; }
[ "$(echo "$CHILD" | jq -r .parent_id)" = "$PARENT_ID" ] \
  || fail "the child should carry its parent's id: $CHILD"
"$CLI" genres delete --id "$PARENT_ID" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "genres delete exited non-zero"; }
"$CLI" genres list 2>"$WORK/cli.err" \
  | jq -e '[.genres[].name] | index("Smoke Child") == null' >/dev/null \
  || fail "deleting a parent genre should take its children with it"
ok "deleting a parent genre cascades to its children"

"$CLI" genres create --name "Smoke Orphan" --parent-id "no-such-genre" >/dev/null 2>&1 \
  && fail "genres create should refuse an unknown --parent-id"
ok "genres create refuses an unknown --parent-id"

"$CLI" licenses delete --id "no-such-license" >/dev/null 2>&1 \
  && fail "deleting an entry that does not exist should fail"
ok "vocabulary delete refuses an unknown id"
```

If a list response's top-level key differs from the `LISTKEY` mapping above, fix the mapping to match what the server actually returns — read one `list` response rather than assuming.

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed` both times. A second run that fails means the block does not converge — fix the block, never an assertion.

- [ ] **Step 5: Confirm nothing was left behind**

```bash
for g in genres licenses parent-systems system-families dice-materials; do
  src/GrimoireCli/bin/Debug/net10.0/grimoire-cli $g list | grep -i smoke && echo "LEFTOVER in $g"
done
```

Expected: no output. Any leftover is a bug in the block.

- [ ] **Step 6: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the vocabulary writes in the smoke test"
```

---

### Task 4: Docs

**Files:**
- Modify: `README.md` (Commands table, the five vocabulary rows)
- Modify: `tools/generate-api-coverage.py` (the `IMPLEMENTED` dict)
- Modify: `docs/grimoire-api-coverage.md` (regenerated, never hand-edited)
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docs/roadmap.md`

**Interfaces:**
- Consumes: the command names from Task 2 and the live findings from Task 3.

- [ ] **Step 1: Add the README rows**

Beside each vocabulary's existing `list` row:

```markdown
| `genres create --name <name> [--parent-id <id>]` | Create a genre (admin) |
| `genres delete --id <id> [--force]` | Delete a genre and its children; no undo (admin) |
| `licenses create --name <name>` | Create a license (admin) |
| `licenses delete --id <id> [--force]` | Delete a license; no undo (admin) |
| `parent-systems create --name <name>` | Create a parent system (admin) |
| `parent-systems delete --id <id> [--force]` | Delete a parent system; no undo (admin) |
| `system-families create --name <name>` | Create a system family (admin) |
| `system-families delete --id <id> [--force]` | Delete a system family; no undo (admin) |
| `dice-materials create --name <name> [--group <group>]` | Create a dice/material (admin) |
| `dice-materials delete --id <id> [--force]` | Delete a dice/material; no undo (admin) |
```

Place each pair directly after its group's `list` row, matching how the table already orders a group's commands.

- [ ] **Step 2: Add the coverage entries**

In `tools/generate-api-coverage.py`, beside the five existing lookup lines:

```python
    "POST /api/genres": "`genres create` ✅",
    "DELETE /api/genres/{genre_id}": "`genres delete` ✅",
    "POST /api/licenses": "`licenses create` ✅",
    "DELETE /api/licenses/{license_id}": "`licenses delete` ✅",
    "POST /api/parent-systems": "`parent-systems create` ✅",
    "DELETE /api/parent-systems/{parent_id}": "`parent-systems delete` ✅",
    "POST /api/system-families": "`system-families create` ✅",
    "DELETE /api/system-families/{family_id}": "`system-families delete` ✅",
    "POST /api/dice-materials": "`dice-materials create` ✅",
    "DELETE /api/dice-materials/{material_id}": "`dice-materials delete` ✅",
```

The path-parameter names must match the spec exactly; check them against `docs/grimoire-api-coverage.md`'s existing rows for those routes rather than guessing.

- [ ] **Step 3: Regenerate the coverage table**

With the stack up:

```bash
tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly the ten rows change, plus the derived `lookups` and `**Total**` summary lines. Any other row changing means the pin or the clone moved — stop and report it rather than committing the drift.

- [ ] **Step 4: Record the verified behaviour**

Add to `docs/grimoire-api-notes.md`, matching the file's existing heading style and placement:

```markdown
### Vocabulary writes

- A forced delete strips nothing. `removed_usage` in the success body counts the
  systems and books that still carry the value — it is stored as a plain string,
  not a reference, so a forced delete removes the vocabulary row alone
  (`routers/lookups/core.py:71-93` and the four siblings, v1.7.1). Genre
  children are the exception: they are cascaded away with the parent.
- Built-in entries are deletable and not restorable. No delete handler checks
  `is_default`, and the defaults are seeded by one-time migrations
  (`migrations/versions/0004_expand_metadata.py`,
  `0006_parent_system_licenses.py`) rather than re-seeded on boot, so `create`
  gives the name back only as a new id with `is_default: false`.
- `create` matches an existing name case-insensitively (`ilike`) and 409s;
  `genres create` 404s on an unknown `parent_id`; `dice-materials create`
  coalesces a blank or omitted `group` to `"Custom"` (`core.py:278`).
- Usage is counted by name, case-insensitively, over the systems and books
  carrying it (`routers/lookups/_helpers.py:71-127`).
```

Replace any line with what Task 3 actually observed if the stack disagrees, and say so in the PR body.

- [ ] **Step 5: Remove the shipped roadmap item**

`docs/roadmap.md`'s `Next` list now contains only this item. Remove it, leaving the `Small completions` paragraph and the `Later` section in place, and update the lead-in paragraph above `## Next`, which describes vocabulary curation as the thing still missing. Read `git show 872ba5c -- docs/roadmap.md` for the shape the last one took. The file records intended work only — add no note that this shipped. If removing the item empties `## Next`, say so in your report rather than inventing a replacement item.

- [ ] **Step 6: Commit**

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docs/grimoire-api-notes.md docs/roadmap.md
git commit -m "docs: record the vocabulary write commands"
```

---

### Task 5: Pre-PR verification and the PR

- [ ] **Step 1: Run all four checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. Report the actual output; do not claim a pass that was not run.

- [ ] **Step 2: Open the PR**

```bash
git push -u origin feat/vocabulary-writes
gh pr create --title "feat: vocabulary writes" --body "…"
```

The body states what the ten commands are, the two traps the help text now carries (a forced delete strips nothing; built-in entries are deletable and unrecoverable), the verification that was run, and that the README table and coverage doc are updated in the same change. End it with the attribution line this session's instructions require.

- [ ] **Step 3: Watch CI to a terminal state**

```bash
gh pr checks --watch
```

Report the result without being asked, and present the PR URL as a clickable link.
