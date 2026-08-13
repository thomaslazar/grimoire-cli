using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>Option factories shared across command groups.</summary>
public static class OptionHelpers
{
    /// <summary>
    /// A string option restricted to a fixed value set, rejected at parse time and
    /// offered as shell completions. The rendered help lists the values itself, so
    /// the description must not repeat them.
    /// </summary>
    public static Option<string?> Choice(string name, string description, string[] allowed)
    {
        var option = new Option<string?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !allowed.Contains(value))
                result.AddError($"'{value}' is not a valid value for {name}. Must be one of: {string.Join(", ", allowed)}");
        });
        option.CompletionSources.Add(allowed);
        return option;
    }
}
