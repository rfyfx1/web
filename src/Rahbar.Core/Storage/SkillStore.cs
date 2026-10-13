namespace Rahbar.Core.Storage;

/// <summary>
/// "Skills" are .md files with extra instructions (e.g. a Seller Central guide or our brand voice).
/// The chosen one is added to the brain's system prompt.
/// </summary>
public sealed class SkillStore
{
    public const int MaxChars = 8000;
    public string Folder { get; }

    public SkillStore(string folder) => Folder = folder;

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(Folder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(Folder, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <returns>The skill text (trimmed to <see cref="MaxChars"/>), or null if it doesn't exist.</returns>
    public string? Read(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) return null;
        var path = Path.Combine(Folder, name + ".md");
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path).Trim();
        return text.Length > MaxChars ? text[..MaxChars] : text;
    }

    /// <summary>Creates the folder with example skills the first time.</summary>
    public void EnsureExamples()
    {
        if (Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.md").Any()) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "Seller Central guide.md"), """
            # Amazon Seller Central guide
            The user is learning Amazon Seller Central (sellercentral.amazon.*).
            - Explain Amazon terms simply the first time you use them: ASIN, SKU, FBA, FBM, Buy Box, listing, inventory.
            - Most menus are under the ☰ hamburger menu at the top left: Catalog, Inventory, Pricing, Orders, Advertising, Reports.
            - Never click or suggest anything that changes prices, deletes listings or spends ad money without warning the user first.
            - If a page looks different from what you expect, say Amazon may have changed the layout and describe what you see.
            """);
        File.WriteAllText(Path.Combine(Folder, "Excel beginner.md"), """
            # Excel for beginners
            The user is new to Excel. Use the names on the ribbon tabs exactly (Home, Insert, Formulas, Data).
            Explain one action per step. When a formula is needed, say it slowly and show where to type it (the formula bar).
            """);
        File.WriteAllText(Path.Combine(Folder, "README.txt"), """
            Put .md files here with extra instructions for Rahbar, then pick one from the tray menu › Skill.
            Write them in plain language. Keep each under about 8,000 characters.
            """);
    }
}
