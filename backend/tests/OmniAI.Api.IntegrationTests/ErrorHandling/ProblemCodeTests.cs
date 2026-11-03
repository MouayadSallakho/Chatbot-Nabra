using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OmniAI.Api.ErrorHandling;
using OmniAI.Api.IntegrationTests.WebChat;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Files;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Tenants;
using OmniAI.Application.WebChat;
using OmniAI.Application.WhatsApp;

namespace OmniAI.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// Phase 37.5a, row 28a: every 400, 409 and 422 the knowledge, agent and channel
/// areas return carries a stable <c>code</c>, and every code is in the catalogue.
/// </summary>
/// <remarks>
/// Three layers, because each catches what the others cannot:
/// <list type="bullet">
/// <item>a source scan fails the moment somebody adds a throw site in these areas
/// without naming its rule;</item>
/// <item>the catalogue check fails when a code is published that
/// <c>problem-codes.md</c> does not explain;</item>
/// <item>the HTTP checks prove the code reaches the wire, with the fallback for
/// anything not yet given a specific one.</item>
/// </list>
/// </remarks>
public sealed partial class ProblemCodeTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private static readonly string[] AreaFolders =
    [
        "backend/src/OmniAI.Domain/AiAgents",
        "backend/src/OmniAI.Domain/Knowledge",
        "backend/src/OmniAI.Domain/WebChat",
        "backend/src/OmniAI.Domain/WhatsApp",
        "backend/src/OmniAI.Application/AiAgents",
        "backend/src/OmniAI.Application/Knowledge",
        "backend/src/OmniAI.Application/WebChat",
        "backend/src/OmniAI.Application/WhatsApp",
    ];

    // ---- the fallback ----------------------------------------------------

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "validation.failed")]
    [InlineData(StatusCodes.Status422UnprocessableEntity, "validation.failed")]
    [InlineData(StatusCodes.Status409Conflict, "state.conflict")]
    [InlineData(StatusCodes.Status412PreconditionFailed, "precondition.failed")]
    public void A_correctable_refusal_without_a_code_is_given_the_generic_one(int status, string code)
    {
        ProblemDetails problem = new() { Status = status };

        ProblemCodes.ApplyFallback(problem);

        Assert.Equal(code, problem.Extensions["code"]);
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public void Other_statuses_are_left_exactly_as_they_were(int status)
    {
        ProblemDetails problem = new() { Status = status };

        ProblemCodes.ApplyFallback(problem);

        Assert.False(problem.Extensions.ContainsKey("code"));
    }

    [Fact]
    public void A_specific_code_is_never_overwritten()
    {
        ProblemDetails problem = new() { Status = StatusCodes.Status400BadRequest };
        problem.Extensions["code"] = "ai-agents.name.required";

        ProblemCodes.ApplyFallback(problem);

        Assert.Equal("ai-agents.name.required", problem.Extensions["code"]);
    }

    [Fact]
    public void Each_area_refusal_type_has_its_own_code()
    {
        string?[] codes = [.. AreaTypes().Select(AreaCodeOf)];

        Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- every throw site names its rule ----------------------------------

    [Fact]
    public void Every_domain_refusal_in_these_areas_names_its_rule()
    {
        List<string> uncoded = [];

        foreach (FileInfo file in AreaFolders.SelectMany(folder => Source(folder)))
        {
            string text = File.ReadAllText(file.FullName);

            foreach (Match throwSite in DomainThrow().Matches(text))
            {
                int end = Closing(text, throwSite.Index + throwSite.Length - 1);
                string after = text[(end + 1)..Math.Min(text.Length, end + 80)];

                if (!InitializerWithCode().IsMatch(after))
                {
                    uncoded.Add($"{file.Name}:{LineOf(text, throwSite.Index)}");
                }
            }
        }

        Assert.True(uncoded.Count == 0, "Throw sites without a code: " + string.Join(", ", uncoded));
    }

    [Fact]
    public void Every_file_refusal_names_its_rule()
    {
        List<string> uncoded = [];

        foreach (FileInfo file in Source("backend/src/OmniAI.Infrastructure/Knowledge/Files"))
        {
            string text = File.ReadAllText(file.FullName);

            foreach (Match site in FileRefusal().Matches(text))
            {
                string call = text[site.Index..Math.Min(text.Length, site.Index + 200)];

                // A constant, or a helper's code parameter (the PDF reader's one
                // sentence for "damaged" and "encrypted").
                if (!call.Contains("KnowledgeFileRefusals.", StringComparison.Ordinal)
                    && !CodeParameter().IsMatch(call))
                {
                    uncoded.Add($"{file.Name}:{LineOf(text, site.Index)}");
                }
            }
        }

        Assert.True(uncoded.Count == 0, "File refusals without a code: " + string.Join(", ", uncoded));
    }

    [Fact]
    public void Every_published_code_is_explained_in_the_catalogue()
    {
        string catalogue = File.ReadAllText(Path.Combine(Repository().FullName, "docs", "architecture", "problem-codes.md"));

        IEnumerable<string> fromThrowSites = AreaFolders
            .Append("backend/src/OmniAI.Domain/Tenants")
            .SelectMany(folder => Source(folder))
            .SelectMany(file => CodeLiteral().Matches(File.ReadAllText(file.FullName)).Select(match => match.Groups["code"].Value));

        IEnumerable<string> fromFileRefusals = typeof(Application.Knowledge.Files.KnowledgeFileRefusals)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

        IEnumerable<string> fromTheEdge =
        [
            ProblemCodes.ValidationFailed,
            ProblemCodes.StateConflict,
            ProblemCodes.PreconditionFailed,
            ProblemCodes.PreconditionRequired,
            ProblemCodes.IfMatchInvalid,
            "website-chat.agent.required",
            "whatsapp.agent.required",
            .. AreaTypes().Select(AreaCodeOf).OfType<string>(),
        ];

        string[] missing =
        [
            .. fromThrowSites.Concat(fromFileRefusals).Concat(fromTheEdge)
                .Distinct(StringComparer.Ordinal)
                .Where(code => !catalogue.Contains($"`{code}`", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(missing.Length == 0, "Codes missing from problem-codes.md: " + string.Join(", ", missing));
    }

    // ---- on the wire -----------------------------------------------------

    [Fact]
    public async Task Refusals_from_these_areas_reach_the_client_with_their_code()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        (string Path, object Body, HttpStatusCode Status, string Code)[] cases =
        [
            ($"/api/tenants/{tenantId}/ai-agents", new { name = "", instructions = "x" }, HttpStatusCode.BadRequest, "ai-agents.name.required"),
            ($"/api/tenants/{tenantId}/ai-agents", new { name = "Ok", instructions = "" }, HttpStatusCode.BadRequest, "ai-agents.instructions.required"),
            ($"/api/tenants/{tenantId}/knowledge", new { title = "", content = "x" }, HttpStatusCode.BadRequest, "knowledge.title.required"),
            ($"/api/tenants/{tenantId}/knowledge", new { title = "Ok", content = "" }, HttpStatusCode.BadRequest, "knowledge.content.required"),
            ($"/api/tenants/{tenantId}/website-chat", new { aiAgentId = (Guid?)null }, HttpStatusCode.BadRequest, "website-chat.agent.required"),
            ($"/api/tenants/{tenantId}/whatsapp/connections", new { aiAgentId = (Guid?)null }, HttpStatusCode.BadRequest, "whatsapp.agent.required"),
        ];

        foreach ((string path, object body, HttpStatusCode status, string code) in cases)
        {
            HttpResponseMessage response = await owner.PostAsync(
                WebChatApiFixture.Relative(path),
                JsonContent.Create(body, body.GetType()));

            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await CodeAsync(response));
        }

        HttpResponseMessage paging = await owner.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents?pageSize=1000"));
        Assert.Equal(HttpStatusCode.BadRequest, paging.StatusCode);
        Assert.Equal("ai-agents.page.invalid", await CodeAsync(paging));
    }

    [Fact]
    public async Task A_state_refusal_carries_its_specific_code()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents"),
            new { name = "Draft agent", instructions = "Be brief." });
        using JsonDocument agent = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        HttpRequestMessage disable = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agent.RootElement.GetProperty("id").GetGuid()}/disable"));
        disable.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());

        HttpResponseMessage refused = await owner.SendAsync(disable);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("ai-agents.disable.draft", await CodeAsync(refused));
    }

    [Fact]
    public async Task A_refusal_outside_these_areas_still_carries_the_generic_code()
    {
        HttpClient client = await fixture.CreateSignedInClientAsync();

        // A workspace slug the slug rules refuse: Tenants/TenantSlug has no
        // specific code yet, so the guarantee gives it the generic one.
        HttpResponseMessage refused = await client.PostAsJsonAsync("/api/tenants", new { name = "Fine", slug = "NOT A SLUG" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("validation.failed", await CodeAsync(refused));
    }

    private static IEnumerable<Type> AreaTypes() =>
    [
        typeof(AiAgentPageValidationException),
        typeof(KnowledgeDocumentPageValidationException),
        typeof(KnowledgeIndexingInProgressException),
        typeof(KnowledgeDocumentHasNoSourceException),
        typeof(WebChatAgentUnusableException),
        typeof(WebChatTurnInProgressException),
        typeof(WebChatTurnAlreadyFailedException),
        typeof(WhatsAppPhoneNumberInUseException),
        typeof(ConversationNotOnWhatsAppException),
        typeof(TenantSlugAlreadyExistsException),
        typeof(Application.Ai.AiTurnValidationException),
    ];

    /// <summary>
    /// The code a refusal type always carries. The instance is never constructed:
    /// the mapping is by type alone, so no constructor's argument matters.
    /// </summary>
    private static string? AreaCodeOf(Type type) =>
        DomainProblemDetailsHandler.AreaCode(
            (Exception)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type));

    private static IEnumerable<FileInfo> Source(string folder) =>
        new DirectoryInfo(Path.Combine(Repository().FullName, folder)).EnumerateFiles("*.cs", SearchOption.AllDirectories);

    private static int Closing(string text, int open)
    {
        int depth = 0;
        bool inString = false;

        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Unbalanced constructor call.");
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

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

    [GeneratedRegex(@"new (DomainValidationException|DomainStateException)\(")]
    private static partial Regex DomainThrow();

    [GeneratedRegex(@"^\s*\{\s*Code\s*=\s*\S")]
    private static partial Regex InitializerWithCode();

    [GeneratedRegex(@"new KnowledgeFileRejectedException\(|\bnew\(\s*KnowledgeFileRejection\.")]
    private static partial Regex FileRefusal();

    [GeneratedRegex(@"KnowledgeFileRejection\.\w+,\s*code,")]
    private static partial Regex CodeParameter();

    [GeneratedRegex(@"Code\s*=\s*""(?<code>[a-z0-9._-]+)""")]
    private static partial Regex CodeLiteral();
}
