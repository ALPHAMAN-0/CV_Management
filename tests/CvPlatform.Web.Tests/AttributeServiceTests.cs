using CvPlatform.Domain;
using CvPlatform.Web.Data;
using CvPlatform.Web.Features.Account;
using CvPlatform.Web.Features.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Conflict = CvPlatform.Domain.SaveResult<CvPlatform.Web.Features.Attributes.AttributeDetails>.Conflict;
using Invalid = CvPlatform.Domain.SaveResult<CvPlatform.Web.Features.Attributes.AttributeDetails>.Invalid;
using Saved = CvPlatform.Domain.SaveResult<CvPlatform.Web.Features.Attributes.AttributeDetails>.Saved;

namespace CvPlatform.Web.Tests;

public sealed class AttributeServiceTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    [Fact]
    public async Task Create_starts_at_version_zero_with_trimmed_options_in_order()
    {
        var name = UniqueName();
        var fields = new AttributeFields($"  {name}  ", "  Favourite colour  ", AttributeCategory.Personal,
            AttributeType.Dropdown, [new(0, " Red "), new(999_999, "Green"), new(0, "Blue")]);

        var saved = Assert.IsType<Saved>(await Service.CreateAsync(fields, default));

        Assert.Equal(0, saved.NewVersion);
        Assert.Equal(0, saved.Value.Version);
        Assert.False(saved.Value.IsSystem);
        Assert.Equal(name, saved.Value.Fields.Name);
        Assert.Equal("Favourite colour", saved.Value.Fields.Description);
        Assert.Equal(["Red", "Green", "Blue"], saved.Value.Fields.Options.Select(o => o.Label));
        // Client ids are ignored on create: the database assigns every option's id.
        Assert.DoesNotContain(saved.Value.Fields.Options, o => o.Id == 999_999);
        Assert.Equivalent(saved.Value, await Service.GetAsync(saved.Value.Id, default), strict: true);
    }

    [Fact]
    public async Task Types_without_options_keep_none()
    {
        var created = await CreateAsync(Fields(UniqueName(), AttributeType.String, "Stray option"));

        Assert.Empty(created.Fields.Options);
        Assert.Empty((await Service.GetAsync(created.Id, default))!.Fields.Options);
    }

    [Fact]
    public async Task Blank_description_is_stored_as_null()
    {
        var created = await CreateAsync(Fields(UniqueName()) with { Description = "   " });

        Assert.Null((await Service.GetAsync(created.Id, default))!.Fields.Description);
    }

    [Fact]
    public async Task Create_with_a_duplicate_name_ignoring_case_and_spaces_is_invalid()
    {
        var name = UniqueName();
        await CreateAsync(Fields(name));

        var result = await Service.CreateAsync(Fields($"  {name.ToUpperInvariant()} "), default);

        AssertInvalid(result, nameof(AttributeFields.Name));
    }

    [Fact]
    public async Task Renaming_to_an_existing_name_is_invalid_and_saves_nothing()
    {
        var taken = UniqueName();
        await CreateAsync(Fields(taken));
        var created = await CreateAsync(Fields(UniqueName()));

        var result = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Name = taken.ToLowerInvariant(), Description = "changed" }, default);

        AssertInvalid(result, nameof(AttributeFields.Name));
        Assert.Equivalent(created, await Service.GetAsync(created.Id, default), strict: true);
    }

    [Fact]
    public async Task Invalid_input_is_reported_per_field()
    {
        var result = await Service.CreateAsync(Fields("   ", AttributeType.Dropdown), default);

        AssertInvalid(result, nameof(AttributeFields.Name), nameof(AttributeFields.Options));
    }

    [Fact]
    public async Task Undefined_enum_values_are_invalid()
    {
        AssertInvalid(await Service.CreateAsync(Fields(UniqueName(), (AttributeType)99), default),
            nameof(AttributeFields.Type));
        AssertInvalid(await Service.CreateAsync(Fields(UniqueName()) with { Category = (AttributeCategory)99 }, default),
            nameof(AttributeFields.Category));
    }

    [Fact]
    public async Task Save_with_the_current_version_increments_it()
    {
        var created = await CreateAsync(Fields(UniqueName()));
        var renamed = UniqueName();

        var saved = Assert.IsType<Saved>(await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Name = renamed, Category = AttributeCategory.Languages }, default));

        Assert.Equal(1, saved.NewVersion);
        var loaded = await Service.GetAsync(created.Id, default);
        Assert.Equivalent(saved.Value, loaded, strict: true);
        Assert.Equal((1, renamed, AttributeCategory.Languages), (loaded!.Version, loaded.Fields.Name, loaded.Fields.Category));
    }

    [Fact]
    public async Task Stale_save_returns_the_server_state_as_a_conflict()
    {
        var created = await CreateAsync(Fields(UniqueName()));
        var theirs = await SaveAsync(created, created.Fields with { Description = "theirs" });

        var result = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Description = "mine" }, default);

        var conflict = Assert.IsType<Conflict>(result);
        Assert.Equivalent(theirs, conflict.ServerState, strict: true);
        Assert.Equal("theirs", (await Service.GetAsync(created.Id, default))!.Fields.Description);
    }

    [Fact]
    public async Task Options_only_save_increments_the_version()
    {
        var created = await CreateAsync(Fields(UniqueName(), AttributeType.Dropdown, "A"));

        var saved = await SaveAsync(created, created.Fields with { Options = [.. created.Fields.Options, new(0, "B")] });
        var stale = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Description = "mine" }, default);

        Assert.Equal(1, saved.Version);
        var conflict = Assert.IsType<Conflict>(stale);
        Assert.Equal(["A", "B"], conflict.ServerState.Fields.Options.Select(o => o.Label));
    }

    [Fact]
    public async Task Stale_save_that_removes_options_leaves_them_intact()
    {
        var created = await CreateAsync(Fields(UniqueName(), AttributeType.Dropdown, "A", "B", "C"));
        await SaveAsync(created, created.Fields with { Description = "theirs" });

        var result = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Options = [created.Fields.Options[0]] }, default);

        Assert.IsType<Conflict>(result);
        Assert.Equal(["A", "B", "C"], (await Service.GetAsync(created.Id, default))!.Fields.Options.Select(o => o.Label));
    }

    // The stale editor still lists the option the other editor deleted; that is a conflict, not bad input.
    [Fact]
    public async Task Stale_save_listing_an_option_deleted_meanwhile_is_a_conflict()
    {
        var created = await CreateAsync(Fields(UniqueName(), AttributeType.Dropdown, "A", "B"));
        await SaveAsync(created, created.Fields with { Options = [created.Fields.Options[0]] });

        var result = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Description = "mine" }, default);

        var conflict = Assert.IsType<Conflict>(result);
        Assert.Equal(["A"], conflict.ServerState.Fields.Options.Select(o => o.Label));
    }

    [Fact]
    public async Task Option_diff_keeps_ids_of_renamed_options_deletes_removed_ones_and_keeps_the_order()
    {
        var created = await CreateAsync(Fields(UniqueName(), AttributeType.Dropdown, "A", "B", "C"));
        var (a, b, c) = (created.Fields.Options[0], created.Fields.Options[1], created.Fields.Options[2]);

        var saved = await SaveAsync(created, created.Fields with { Options = [c, a with { Label = " A2 " }, new(0, "D")] });

        Assert.Equal(["C", "A2", "D"], saved.Fields.Options.Select(o => o.Label));
        Assert.Equal([c.Id, a.Id], saved.Fields.Options.Take(2).Select(o => o.Id));
        Assert.Equivalent(saved, await Service.GetAsync(created.Id, default), strict: true);
        await using var db = await CreateDbContextAsync();
        Assert.False(await db.AttributeOptions.AnyAsync(o => o.Id == b.Id));
    }

    [Fact]
    public async Task Option_ids_that_are_not_this_attributes_own_are_invalid()
    {
        var other = await CreateAsync(Fields(UniqueName(), AttributeType.Dropdown, "Other"));
        var created = await CreateAsync(Fields(UniqueName(), AttributeType.Dropdown, "Mine"));
        var mine = created.Fields.Options[0];

        var foreign = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Options = [mine, other.Fields.Options[0] with { Label = "Hijacked" }] }, default);
        var repeated = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Options = [mine, mine with { Label = "Mine again" }] }, default);

        AssertInvalid(foreign, nameof(AttributeFields.Options));
        AssertInvalid(repeated, nameof(AttributeFields.Options));
        Assert.Equivalent(other, await Service.GetAsync(other.Id, default), strict: true);
    }

    [Fact]
    public async Task Type_cannot_change_on_ordinary_or_system_attributes()
    {
        var created = await CreateAsync(Fields(UniqueName()));
        var firstName = (await Service.GetAsync(await SystemAttributeIdAsync(SystemAttributeKey.FirstName), default))!;

        var ordinary = await Service.SaveAsync(created.Id, created.Version,
            created.Fields with { Type = AttributeType.Text }, default);
        var system = await Service.SaveAsync(firstName.Id, firstName.Version,
            firstName.Fields with { Type = AttributeType.Text }, default);

        AssertInvalid(ordinary, nameof(AttributeFields.Type));
        AssertInvalid(system, nameof(AttributeFields.Type));
    }

    [Fact]
    public async Task System_attributes_can_be_renamed()
    {
        var photo = (await Service.GetAsync(await SystemAttributeIdAsync(SystemAttributeKey.Photo), default))!;
        var renamed = UniqueName();

        var saved = await SaveAsync(photo, photo.Fields with { Name = renamed });

        Assert.True(saved.IsSystem);
        Assert.Equal((renamed, AttributeType.Image), (saved.Fields.Name, saved.Fields.Type));
    }

    [Fact]
    public async Task Saving_a_deleted_attribute_is_not_found()
    {
        var created = await CreateAsync(Fields(UniqueName()));
        await Service.DeleteAsync([created.Id], default);

        var result = await Service.SaveAsync(created.Id, created.Version, created.Fields, default);

        Assert.IsType<SaveResult<AttributeDetails>.NotFound>(result);
    }

    // Two editors saving the same version: whichever interleaving happens (the loser reads the new
    // version, or its UPDATE matches no row), exactly one save wins.
    [Fact]
    public async Task Concurrent_saves_of_the_same_version_let_exactly_one_win()
    {
        var created = await CreateAsync(Fields(UniqueName()));

        var results = await Task.WhenAll(
            Service.SaveAsync(created.Id, created.Version, created.Fields with { Description = "first" }, default),
            Service.SaveAsync(created.Id, created.Version, created.Fields with { Description = "second" }, default));

        Assert.Single(results, r => r is Saved);
        Assert.Single(results, r => r is Conflict);
    }

    [Fact]
    public async Task Delete_removes_ordinary_attributes_with_their_options_and_skips_system_ones()
    {
        var dropdown = await CreateAsync(Fields(UniqueName(), AttributeType.Dropdown, "A", "B"));
        var text = await CreateAsync(Fields(UniqueName(), AttributeType.Text));
        var firstNameId = await SystemAttributeIdAsync(SystemAttributeKey.FirstName);

        var deleted = await Service.DeleteAsync([dropdown.Id, text.Id, firstNameId], default);

        Assert.Equal(2, deleted);
        Assert.Null(await Service.GetAsync(dropdown.Id, default));
        Assert.Null(await Service.GetAsync(text.Id, default));
        Assert.NotNull(await Service.GetAsync(firstNameId, default));
        await using var db = await CreateDbContextAsync();
        Assert.False(await db.AttributeOptions.AnyAsync(o => o.AttributeDefinitionId == dropdown.Id));
    }

    [Fact]
    public async Task List_pages_and_sorts_by_name_ignoring_case()
    {
        var token = Guid.NewGuid().ToString("N");
        await CreateAsync(Fields($"{token} Bravo"));
        await CreateAsync(Fields($"{token} alpha"));
        await CreateAsync(Fields($"{token} Charlie"));

        var first = await Service.ListAsync(token, null, State(0, 2, nameof(AttributeRow.Name)), default);
        var second = await Service.ListAsync(token, null, State(1, 2, nameof(AttributeRow.Name)), default);
        var descending = await Service.ListAsync(token, null, State(0, 10, nameof(AttributeRow.Name), descending: true), default);

        Assert.Equal(3, first.TotalItems);
        Assert.Equal([$"{token} alpha", $"{token} Bravo"], first.Items.Select(r => r.Name));
        Assert.Equal([$"{token} Charlie"], second.Items.Select(r => r.Name));
        Assert.Equal([$"{token} Charlie", $"{token} Bravo", $"{token} alpha"], descending.Items.Select(r => r.Name));
    }

    [Fact]
    public async Task List_filters_by_category_and_sorts_enums_by_their_english_name()
    {
        var token = Guid.NewGuid().ToString("N");
        await CreateAsync(Fields($"{token} a", AttributeType.String) with { Category = AttributeCategory.Skills });
        await CreateAsync(Fields($"{token} b", AttributeType.Text) with { Category = AttributeCategory.Contact });
        await CreateAsync(Fields($"{token} c", AttributeType.Number) with { Category = AttributeCategory.Skills });

        var skills = await Service.ListAsync(token, AttributeCategory.Skills, State(0, 10), default);
        var byCategory = await Service.ListAsync(token, null, State(0, 10, nameof(AttributeRow.Category)), default);
        var byType = await Service.ListAsync(token, null, State(0, 10, nameof(AttributeRow.Type)), default);

        Assert.Equal(2, skills.TotalItems);
        Assert.Equal([$"{token} a", $"{token} c"], skills.Items.Select(r => r.Name));
        Assert.Equal([$"{token} b", $"{token} a", $"{token} c"], byCategory.Items.Select(r => r.Name));
        Assert.Equal([AttributeType.Number, AttributeType.String, AttributeType.Text], byType.Items.Select(r => r.Type));
    }

    [Fact]
    public async Task List_search_matches_part_of_the_name_and_marks_system_rows()
    {
        var page = await Service.ListAsync("irst nam", null, State(0, 10), default);

        var row = Assert.Single(page.Items);
        Assert.Equal(await SystemAttributeIdAsync(SystemAttributeKey.FirstName), row.Id);
        Assert.True(row.IsSystem);
    }

    // LIKE wildcards in the search box are matched literally, not as "anything".
    [Fact]
    public async Task List_search_treats_wildcards_literally()
    {
        await CreateAsync(Fields(UniqueName()));

        var page = await Service.ListAsync("%", null, State(0, 10), default);

        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task Candidates_are_refused()
    {
        var created = await CreateAsync(Fields(UniqueName()));
        var candidate = CreateService(AppRoles.Candidate);

        Assert.IsType<SaveResult<AttributeDetails>.Forbidden>(await candidate.CreateAsync(Fields(UniqueName()), default));
        Assert.IsType<SaveResult<AttributeDetails>.Forbidden>(
            await candidate.SaveAsync(created.Id, created.Version, created.Fields with { Description = "x" }, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => candidate.ListAsync(null, null, State(0, 10), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => candidate.GetAsync(created.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => candidate.DeleteAsync([created.Id], default));
        Assert.Equivalent(created, await Service.GetAsync(created.Id, default), strict: true);
    }

    [Theory]
    [InlineData(AppRoles.Recruiter)]
    [InlineData(AppRoles.Administrator)]
    public async Task Recruiters_and_administrators_manage_the_library(string role)
    {
        var service = CreateService(role);
        var name = UniqueName();

        var created = Assert.IsType<Saved>(await service.CreateAsync(Fields(name), default)).Value;
        var listed = await service.ListAsync(name, null, State(0, 10), default);
        var saved = Assert.IsType<Saved>(await service.SaveAsync(created.Id, created.Version,
            created.Fields with { Description = role }, default));
        var deleted = await service.DeleteAsync([created.Id], default);

        Assert.Equal(created.Id, Assert.Single(listed.Items).Id);
        Assert.Equal(1, saved.NewVersion);
        Assert.Equal(1, deleted);
    }

    // Recruiter is the least-privileged role allowed to manage the library.
    private AttributeService Service => CreateService(AppRoles.Recruiter);

    private AttributeService CreateService(params string[] roles) =>
        new(factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            new FixedAuthenticationStateProvider(TestAuth.Principal(roles)),
            factory.Services.GetRequiredService<IAuthorizationService>());

    private static string UniqueName() => $"Attribute {Guid.NewGuid():N}";

    private static AttributeFields Fields(string name, AttributeType type = AttributeType.String, params string[] options) =>
        new(name, null, AttributeCategory.Skills, type, [.. options.Select(label => new AttributeOptionInput(0, label))]);

    private static GridState<AttributeRow> State(int page, int pageSize, string? sortBy = null, bool descending = false) => new()
    {
        Page = page,
        PageSize = pageSize,
        SortDefinitions = sortBy is null ? [] : [new SortDefinition<AttributeRow>(sortBy, descending, 0, row => row.Name)],
    };

    // Helpers for steps that must succeed; the tests assert on the interesting call directly.
    private async Task<AttributeDetails> CreateAsync(AttributeFields fields) =>
        Assert.IsType<Saved>(await Service.CreateAsync(fields, default)).Value;

    private async Task<AttributeDetails> SaveAsync(AttributeDetails current, AttributeFields fields) =>
        Assert.IsType<Saved>(await Service.SaveAsync(current.Id, current.Version, fields, default)).Value;

    private static void AssertInvalid(SaveResult<AttributeDetails> result, params string[] fields) =>
        Assert.Equal(fields.Order(StringComparer.Ordinal),
            Assert.IsType<Invalid>(result).Errors.Keys.Order(StringComparer.Ordinal));

    private async Task<int> SystemAttributeIdAsync(SystemAttributeKey key)
    {
        await using var db = await CreateDbContextAsync();
        return await db.AttributeDefinitions.Where(a => a.SystemKey == key).Select(a => a.Id).SingleAsync();
    }

    private Task<AppDbContext> CreateDbContextAsync() =>
        factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
}
