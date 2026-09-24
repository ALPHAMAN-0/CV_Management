using CvPlatform.Domain;
using CvPlatform.Web.Data;
using CvPlatform.Web.Features.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using Npgsql;

namespace CvPlatform.Web.Features.Attributes;

public sealed class AttributeService(
    IDbContextFactory<AppDbContext> dbFactory,
    AuthenticationStateProvider authenticationState,
    IAuthorizationService authorization)
{
    // The unique index is the real guard against duplicate names; a check-then-insert would race.
    private const string UniqueNameIndex = "IX_AttributeDefinitions_NormalizedName";

    // Paged, sorted and filtered in the database: one COUNT and one page query, whatever the page size.
    public async Task<GridData<AttributeRow>> ListAsync(
        string? search, AttributeCategory? category, GridState<AttributeRow> state, CancellationToken ct)
    {
        await EnsureAllowedAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attributes = db.AttributeDefinitions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = AttributeDefinition.NormalizeName(search);
            attributes = attributes.Where(a => a.NormalizedName.Contains(term));
        }
        if (category is not null)
        {
            attributes = attributes.Where(a => a.Category == category);
        }

        var total = await attributes.CountAsync(ct);

        // Only whitelisted columns can be sorted; anything else falls back to name. Names sort by their
        // normalized form so case never splits the list, whatever the database collation. Category and
        // Type are stored by member name, so they sort alphabetically in English.
        var sort = state.SortDefinitions.FirstOrDefault();
        var ordered = (sort?.SortBy, sort?.Descending) switch
        {
            (nameof(AttributeRow.Name), true) => attributes.OrderByDescending(a => a.NormalizedName),
            (nameof(AttributeRow.Category), true) => attributes.OrderByDescending(a => a.Category),
            (nameof(AttributeRow.Category), _) => attributes.OrderBy(a => a.Category),
            (nameof(AttributeRow.Type), true) => attributes.OrderByDescending(a => a.Type),
            (nameof(AttributeRow.Type), _) => attributes.OrderBy(a => a.Type),
            _ => attributes.OrderBy(a => a.NormalizedName),
        };

        var items = await ordered
            .ThenBy(a => a.Id) // many rows share a category or type; ties would make paging unstable
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .Select(a => new AttributeRow(a.Id, a.Name, a.Category, a.Type, a.SystemKey != null, a.Description))
            .ToListAsync(ct);

        return new GridData<AttributeRow> { Items = items, TotalItems = total };
    }

    public async Task<AttributeDetails?> GetAsync(int id, CancellationToken ct)
    {
        await EnsureAllowedAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ReadDetailsAsync(db, id, ct);
    }

    public async Task<SaveResult<AttributeDetails>> CreateAsync(AttributeFields fields, CancellationToken ct)
    {
        if (!await IsAllowedAsync())
        {
            return new SaveResult<AttributeDetails>.Forbidden();
        }

        var errors = Validate(fields);
        if (errors.Count > 0)
        {
            return new SaveResult<AttributeDetails>.Invalid(errors);
        }

        var attribute = new AttributeDefinition { Type = fields.Type };
        ApplyFields(attribute, fields);
        // Every option of a new attribute is new, so ids sent by the client are ignored.
        attribute.Options.AddRange(OptionsOf(fields).Select((option, index) =>
            new AttributeOption { Label = option.Label.Trim(), SortOrder = index }));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AttributeDefinitions.Add(attribute);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsDuplicateName(e))
        {
            return DuplicateName();
        }

        return new SaveResult<AttributeDetails>.Saved(attribute.Version, ToDetails(attribute));
    }

    // One SaveChanges, so the attribute and all its option changes commit or roll back together.
    public async Task<SaveResult<AttributeDetails>> SaveAsync(
        int id, int expectedVersion, AttributeFields fields, CancellationToken ct)
    {
        if (!await IsAllowedAsync())
        {
            return new SaveResult<AttributeDetails>.Forbidden();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attribute = await db.AttributeDefinitions.Include(a => a.Options).SingleOrDefaultAsync(a => a.Id == id, ct);
        if (attribute is null)
        {
            return new SaveResult<AttributeDetails>.NotFound();
        }

        // A stale save is a conflict whatever it contains. Checking before the option diff also keeps
        // options the other editor deleted from being reported as invalid input.
        if (attribute.Version != expectedVersion)
        {
            return new SaveResult<AttributeDetails>.Conflict(ToDetails(attribute));
        }

        // Stored values live in the slot of the type, so it is fixed at creation (delete and recreate).
        if (fields.Type != attribute.Type)
        {
            return Invalid(nameof(AttributeFields.Type), "The type can't be changed after the attribute is created.");
        }

        var errors = Validate(fields);
        if (errors.Count > 0)
        {
            return new SaveResult<AttributeDetails>.Invalid(errors);
        }

        var inputs = OptionsOf(fields);
        var keptIds = inputs.Where(o => o.Id != 0).Select(o => o.Id).ToList();
        var existing = attribute.Options.ToDictionary(o => o.Id);
        // Diffed against this attribute's own options only, so an id can never reach another attribute's rows.
        if (keptIds.Distinct().Count() != keptIds.Count || !keptIds.All(existing.ContainsKey))
        {
            return Invalid(nameof(AttributeFields.Options), "Unknown option.");
        }

        ApplyFields(attribute, fields);
        // Removing from the collection deletes the row: an option can't exist without its attribute.
        attribute.Options.RemoveAll(o => !keptIds.Contains(o.Id));
        for (var index = 0; index < inputs.Count; index++)
        {
            var label = inputs[index].Label.Trim();
            if (existing.TryGetValue(inputs[index].Id, out var option))
            {
                option.Label = label;
                option.SortOrder = index;
            }
            else
            {
                attribute.Options.Add(new AttributeOption { Label = label, SortOrder = index });
            }
        }

        // The UPDATE's WHERE carries the version the editor loaded, so a save committed between our read
        // and this write matches no row and throws. Bumped even when only options changed: EF checks the
        // token only on rows it updates.
        db.Entry(attribute).Property(a => a.Version).OriginalValue = expectedVersion;
        attribute.Version = expectedVersion + 1;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // No-tracking read: the database's current state, not what this context tracks.
            return await ReadDetailsAsync(db, id, ct) is { } serverState
                ? new SaveResult<AttributeDetails>.Conflict(serverState)
                : new SaveResult<AttributeDetails>.NotFound();
        }
        catch (DbUpdateException e) when (IsDuplicateName(e))
        {
            return DuplicateName();
        }

        return new SaveResult<AttributeDetails>.Saved(attribute.Version, ToDetails(attribute));
    }

    // System attributes are never deleted: the WHERE is the guard, whatever ids the caller sends.
    // Set-based, so options go through the foreign key's ON DELETE CASCADE.
    public async Task<int> DeleteAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        await EnsureAllowedAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AttributeDefinitions
            .Where(a => ids.Contains(a.Id) && a.SystemKey == null)
            .ExecuteDeleteAsync(ct);
    }

    private static Task<AttributeDetails?> ReadDetailsAsync(AppDbContext db, int id, CancellationToken ct) =>
        db.AttributeDefinitions
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AttributeDetails(
                a.Id,
                a.Version,
                a.SystemKey != null,
                new AttributeFields(
                    a.Name,
                    a.Description,
                    a.Category,
                    a.Type,
                    a.Options.OrderBy(o => o.SortOrder).Select(o => new AttributeOptionInput(o.Id, o.Label)).ToList())))
            .SingleOrDefaultAsync(ct);

    // Maps the entity just saved, so the result needs no second round trip.
    private static AttributeDetails ToDetails(AttributeDefinition attribute) =>
        new(attribute.Id,
            attribute.Version,
            attribute.SystemKey is not null,
            new AttributeFields(
                attribute.Name,
                attribute.Description,
                attribute.Category,
                attribute.Type,
                [.. attribute.Options.OrderBy(o => o.SortOrder).Select(o => new AttributeOptionInput(o.Id, o.Label))]));

    private static void ApplyFields(AttributeDefinition attribute, AttributeFields fields)
    {
        attribute.Rename(fields.Name);
        attribute.Description = string.IsNullOrWhiteSpace(fields.Description) ? null : fields.Description.Trim();
        attribute.Category = fields.Category;
    }

    private static IReadOnlyDictionary<string, string> Validate(AttributeFields fields)
    {
        // Enums are integers underneath, so a cast can carry any value; the catalog throws on unknown types.
        if (!Enum.IsDefined(fields.Type))
        {
            return new Dictionary<string, string> { [nameof(AttributeFields.Type)] = "Unknown type." };
        }
        if (!Enum.IsDefined(fields.Category))
        {
            return new Dictionary<string, string> { [nameof(AttributeFields.Category)] = "Unknown category." };
        }

        return AttributeValidation.Validate(
            fields.Name, fields.Description, fields.Type, [.. OptionsOf(fields).Select(o => o.Label)]);
    }

    // Types without options keep none, whatever the form still holds from an earlier type choice.
    private static IReadOnlyList<AttributeOptionInput> OptionsOf(AttributeFields fields) =>
        AttributeTypeCatalog.For(fields.Type).HasOptions ? fields.Options : [];

    private static bool IsDuplicateName(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueNameIndex };

    private static SaveResult<AttributeDetails> DuplicateName() =>
        Invalid(nameof(AttributeFields.Name), "An attribute with this name already exists.");

    private static SaveResult<AttributeDetails>.Invalid Invalid(string field, string message) =>
        new(new Dictionary<string, string> { [field] = message });

    // Enforced here and not only on the pages: hiding UI is cosmetic. Create and Save follow the
    // SaveResult contract and return Forbidden; List, Get and Delete have no result type to carry it,
    // so they throw, as UserAdminService does.
    private async Task<bool> IsAllowedAsync()
    {
        var user = (await authenticationState.GetAuthenticationStateAsync()).User;
        return (await authorization.AuthorizeAsync(user, AppPolicies.RecruiterOrAdmin)).Succeeded;
    }

    private async Task EnsureAllowedAsync()
    {
        if (!await IsAllowedAsync())
        {
            throw new UnauthorizedAccessException("Only recruiters and administrators can manage attributes.");
        }
    }
}
