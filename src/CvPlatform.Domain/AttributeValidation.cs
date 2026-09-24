namespace CvPlatform.Domain;

// Returns field name → English message; the message is also the resource key the UI localizes.
public static class AttributeValidation
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int LabelMaxLength = 100;
    public const int MaxOptions = 100;

    public static IReadOnlyDictionary<string, string> Validate(
        string? name, string? description, AttributeType type, IReadOnlyList<string> optionLabels)
    {
        var errors = new Dictionary<string, string>();

        var trimmedName = name?.Trim() ?? "";
        if (trimmedName.Length == 0)
        {
            errors[nameof(AttributeDefinition.Name)] = "Name is required.";
        }
        else if (trimmedName.Length > NameMaxLength)
        {
            errors[nameof(AttributeDefinition.Name)] = $"Name must be at most {NameMaxLength} characters.";
        }

        if ((description?.Trim().Length ?? 0) > DescriptionMaxLength)
        {
            errors[nameof(AttributeDefinition.Description)] =
                $"Description must be at most {DescriptionMaxLength} characters.";
        }

        var optionsError = ValidateOptions(AttributeTypeCatalog.For(type).HasOptions, optionLabels);
        if (optionsError is not null)
        {
            errors[nameof(AttributeDefinition.Options)] = optionsError;
        }

        return errors;
    }

    private static string? ValidateOptions(bool hasOptions, IReadOnlyList<string> labels)
    {
        if (!hasOptions)
        {
            return labels.Count == 0 ? null : "This type has no options.";
        }

        if (labels.Count == 0)
        {
            return "Add at least one option.";
        }

        if (labels.Count > MaxOptions)
        {
            return $"At most {MaxOptions} options are allowed.";
        }

        if (labels.Any(string.IsNullOrWhiteSpace))
        {
            return "Every option needs a label.";
        }

        var trimmed = labels.Select(label => label.Trim()).ToList();
        if (trimmed.Any(label => label.Length > LabelMaxLength))
        {
            return $"Option labels must be at most {LabelMaxLength} characters.";
        }

        // Same rule as attribute names: "Red" and " red " are the same choice to a reader.
        var distinct = trimmed.Select(label => label.ToUpperInvariant()).Distinct().Count();
        return distinct == trimmed.Count ? null : "Option labels must be unique.";
    }
}
