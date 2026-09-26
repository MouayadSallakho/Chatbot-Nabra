using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using OmniAI.Api.IntegrationTests.WebChat;
using Xunit.Abstractions;

namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// Phase 34 crash drill, first half: hold a provider operation and a Knowledge
/// indexing attempt open at their durable Started boundary until this process is
/// killed from outside.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Opt-in only</strong> (<c>OMNIAI_PERF_CRASH=1</c>), and it never
/// finishes on its own: it exists to be killed (SIGKILL on its container), which
/// is the only honest way to leave a row Started — a graceful host shutdown
/// cancels the provider call and settles it Unknown immediately.
/// </para>
/// <para>
/// Once the rows are committed it writes <c>crash-boundary.json</c> (database
/// name and ids, no credentials) to <c>OMNIAI_PERF_OUT</c>. The second half is
/// run against that database by the real API: its reconciler must settle the
/// rows truthfully. Because the process is killed, the disposable database is
/// not dropped; the drill drops it afterwards.
/// </para>
/// </remarks>
[Trait("Category", "Crash")]
[Collection(LoadScenarios.Name)]
public sealed class CrashBoundaryHold(KnowledgeLoadApiFixture api, ITestOutputHelper output)
    : IClassFixture<KnowledgeLoadApiFixture>
{
    [CrashFact]
    public async Task Hold_a_started_provider_operation_and_indexing_attempt_until_killed()
    {
        api.ResetProvider().Latency = TimeSpan.FromHours(1);
        api.ResetEmbeddings().Latency = TimeSpan.FromHours(1);

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        LoadConversation conversation = await api.StartConversationAsync(workspace);

        HttpResponseMessage created = await workspace.Owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/knowledge"),
            new { title = "Crash drill", content = "Synthetic content for the crash drill. " });
        using JsonDocument doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid documentId = doc.RootElement.GetProperty("id").GetGuid();

        HttpRequestMessage index = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/knowledge/{documentId}/index"));
        index.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());

        // Neither is awaited: both block inside the provider call.
        Task<TurnResult> turn = LoadApiFixture.SendTurnAsync(conversation);
        Task<HttpResponseMessage> indexing = workspace.Owner.SendAsync(index);

        for (int attempt = 0; attempt < 120; attempt++)
        {
            List<object?[]> open = await api.QueryAsync(
                """
                select
                  (select count(*) from ai_provider_usages where tenant_id = @t and status = 'Started' and operation_type = 'TextGeneration'),
                  (select count(*) from ai_provider_usages where tenant_id = @t and status = 'Started' and operation_type = 'Embedding'),
                  (select count(*) from knowledge_index_attempts where document_id = @d and status = 'Started')
                """,
                ("t", workspace.TenantId),
                ("d", documentId));

            if ((long)open[0][0]! == 1 && (long)open[0][1]! == 1 && (long)open[0][2]! == 1)
            {
                string database = new NpgsqlConnectionStringBuilder(api.Database.ConnectionString).Database!;
                string file = Path.Combine(Environment.GetEnvironmentVariable("OMNIAI_PERF_OUT") ?? Path.GetTempPath(), "crash-boundary.json");

                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
                {
                    database,
                    tenantId = workspace.TenantId,
                    conversationId = conversation.Id,
                    documentId,
                }));

                output.WriteLine($"READY {file}");
                await Console.Out.WriteLineAsync("CRASH-BOUNDARY-READY");

                // Wait to be killed.
                await Task.WhenAny(turn, indexing, Task.Delay(TimeSpan.FromHours(1)));
                Assert.Fail("The drill process was expected to be killed while holding the boundary.");
            }

            await Task.Delay(250);
        }

        Assert.Fail("The Started boundary was never reached.");
    }
}

/// <summary>A fact that runs only when <c>OMNIAI_PERF_CRASH=1</c>.</summary>
public sealed class CrashFactAttribute : FactAttribute
{
    public CrashFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OMNIAI_PERF_CRASH") != "1")
        {
            Skip = "The crash-boundary drill runs only when OMNIAI_PERF_CRASH=1.";
        }
    }
}
