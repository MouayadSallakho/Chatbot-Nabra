using System.Text.RegularExpressions;

namespace OmniAI.Infrastructure.IntegrationTests.Deployment;

/// <summary>
/// The product is called Nabra wherever a person can read it.
/// </summary>
/// <remarks>
/// <strong>OmniAI is the internal code name, and it stays internal.</strong>
/// Namespaces, resource names, cookie and header names, configuration keys,
/// environment variables, Redis keys, metric names and the widget's attribute,
/// global and message names all keep it, because renaming them would break
/// running sessions, existing installs and deployments for no reader's benefit.
/// Everything else (an email's subject or footer, a sender name, a PDF's
/// properties, the text of the loader a customer's website downloads) says
/// Nabra.
/// <para>
/// <strong>What is scanned.</strong> In C#, every string literal under
/// <c>backend/src</c> except in generated migrations; comments are never shipped
/// and are skipped. In the widget loader, the whole file, comments included,
/// because a site owner can read every byte of it. In JSON settings, every
/// value and key. An internal name passes only when it has one of the shapes
/// listed in <see cref="InternalName"/>; a bare "OmniAI" passes only in the two
/// places named in <see cref="InternalLiterals"/>, which are identifiers no
/// person reads.
/// </para>
/// </remarks>
public sealed partial class PublicNameTests
{
    /// <summary>
    /// Literals that are exactly the code name and are still internal.
    /// </summary>
    /// <remarks>
    /// The data-protection application discriminator (changing it would sign
    /// every member out and orphan every protected key) and the meter name that
    /// dashboards query. Neither reaches a person.
    /// </remarks>
    private static readonly (string File, string Literal)[] InternalLiterals =
    [
        ("backend/src/OmniAI.Api/Security/DataProtectionSetup.cs", "\"OmniAI\""),
        ("backend/src/OmniAI.Infrastructure/Reliability/OmniAiMetrics.cs", "\"OmniAI\""),
    ];

    [Fact]
    public void No_user_facing_backend_text_says_OmniAI()
    {
        DirectoryInfo repository = Repository();
        DirectoryInfo source = new(Path.Combine(repository.FullName, "backend", "src"));
        List<string> findings = [];

        foreach (FileInfo file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repository.FullName, file.FullName).Replace('\\', '/');

            if (relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal))
            {
                continue;
            }

            IEnumerable<string> texts = file.Extension switch
            {
                ".cs" => CSharpLiterals(File.ReadAllLines(file.FullName)),
                ".js" or ".json" or ".html" or ".txt" or ".resx" or ".md" =>
                    File.ReadAllLines(file.FullName),
                _ => [],
            };

            foreach (string text in texts)
            {
                if (InternalLiterals.Contains((relative, text)))
                {
                    continue;
                }

                if (SaysOmniAi(text))
                {
                    findings.Add($"{relative}: {text.Trim()}");
                }
            }
        }

        Assert.True(findings.Count == 0, "User-facing text names OmniAI:\n" + string.Join('\n', findings));
    }

    [Theory]
    [InlineData("\"OmniAI: \"")]
    [InlineData("\"Open OmniAI\"")]
    [InlineData("\"OmniAI\"")]
    [InlineData("\"OmniAI reports\"")]
    [InlineData("\"in your Omni AI notification preferences\"")]
    [InlineData(" * OmniAI website chat loader, version 1.")]
    [InlineData("none of that is OmniAI's business")]
    public void The_scan_catches_the_code_name_in_words(string text) =>
        Assert.True(SaysOmniAi(text), text);

    [Theory]
    [InlineData("\"omniai.session\"")]
    [InlineData("\"omniai.webchat\"")]
    [InlineData("\"X-OmniAI-Embed-Origin\"")]
    [InlineData("\"OmniAI.Api.Widget.v1.js\"")]
    [InlineData("\"OmniAI:SuppressForeignKeyIndex\"")]
    [InlineData("\"omniai:pcp:v1\"")]
    [InlineData("\"omniai-preflight-probe\"")]
    [InlineData("\"omniai_tenant_id\"")]
    [InlineData("\"OMNIAI_SECRET_MASTER_KEY\"")]
    [InlineData("\"OmniAiDatabase\": \"\",")]
    [InlineData("var publicKey = script.getAttribute('data-omniai-key');")]
    [InlineData("if (window.__omniaiWidgetLoaded) {")]
    [InlineData("\"omniaiEncryptedKey\"")]
    public void The_scan_lets_internal_names_through(string text) =>
        Assert.False(SaysOmniAi(text), text);

    /// <summary>Whether the text names the product by its code name once internal names are removed.</summary>
    private static bool SaysOmniAi(string text) =>
        CodeName().IsMatch(InternalName().Replace(text, string.Empty));

    /// <summary>
    /// The string literals on each line of C#, skipping comment lines.
    /// </summary>
    /// <remarks>
    /// A line-level reading rather than a parser: good enough for a guard whose
    /// failure message shows the line, and it errs towards reporting.
    /// </remarks>
    private static IEnumerable<string> CSharpLiterals(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith('*'))
            {
                continue;
            }

            foreach (Match literal in Literal().Matches(line))
            {
                yield return literal.Value;
            }
        }
    }

    /// <summary>The code name as a word, however it is spaced or cased.</summary>
    [GeneratedRegex(@"omni[\s-]?ai", RegexOptions.IgnoreCase)]
    private static partial Regex CodeName();

    /// <summary>
    /// The shapes an internal name takes, all case-sensitive.
    /// </summary>
    /// <remarks>
    /// Lower-case keys joined by a separator (cookies, Redis keys, metric names,
    /// client names, the widget's <c>data-omniai-key</c>, <c>omniai:v1:</c>
    /// messages and <c>__omniaiWidgetLoaded</c> global); dotted resource names;
    /// the <c>X-OmniAI-</c> headers; environment variables; PascalCase
    /// configuration keys; and one model annotation.
    /// </remarks>
    [GeneratedRegex(
        @"omniai[._:\-]\w|__omniaiWidgetLoaded|omniaiEncryptedKey|OmniAI\.[A-Z]\w*|X-OmniAI-|OMNIAI_[A-Z]|OmniAi[A-Z]\w*|OmniAI:SuppressForeignKeyIndex")]
    private static partial Regex InternalName();

    /// <summary>A C# string literal on one line: regular, verbatim or interpolated.</summary>
    [GeneratedRegex("""[$@]{0,2}"(?:[^"\\]|\\.|"")*"(?!")""")]
    private static partial Regex Literal();

    private static DirectoryInfo Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
