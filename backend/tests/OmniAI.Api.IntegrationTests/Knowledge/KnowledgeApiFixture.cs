using OmniAI.Api.IntegrationTests.Authorization;

namespace OmniAI.Api.IntegrationTests.Knowledge;

/// <summary>
/// The knowledge API over HTTP, against real PostgreSQL, real Redis and no AI
/// provider.
/// </summary>
/// <remarks>
/// <para>
/// It inherits the role fixture because most of what these tests prove is about
/// permissions, and building a restricted member is exactly what that fixture is
/// for. Nothing is substituted: the handlers, the repositories, EF Core, Npgsql,
/// the migrations and the composition root are the real ones.
/// </para>
/// <para>
/// <strong>No embedding provider is configured, and that is deliberate.</strong>
/// The base fixture blanks the credential, so the embedding generator this host
/// resolves is the one that refuses. Every write and read below therefore works
/// exactly as it does on a deployment with no key -- which is what makes the
/// 503 assertions real rather than simulated, and what guarantees no test here
/// can reach the internet.
/// </para>
/// </remarks>
public sealed class KnowledgeApiFixture : RoleApiFixture;
