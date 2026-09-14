using OmniAI.Api.Realtime;
using OmniAI.Api.AiAgents;
using OmniAI.Api.Auth;
using OmniAI.Api.Authorization;
using OmniAI.Api.Composition;
using OmniAI.Api.Conversations;
using OmniAI.Api.Knowledge;
using OmniAI.Api.WebChat;
using OmniAI.Api.WhatsApp;
using OmniAI.Api.Security;
using OmniAI.Api.Team;
using OmniAI.Api.Tenants;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// OpenAPI describes the HTTP surface that the Angular admin application consumes.
builder.Services.AddOpenApi();

// Problem Details is the single, uniform error contract for the whole API.
builder.Services.AddProblemDetails();

// Persistence, authentication, use cases and error translation. See CompositionRoot.
builder.Services.AddOmniAi(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();

// FIRST, and the order is not negotiable -- including the order of these two.
//
// TLS is terminated by the reverse proxy, so the connection this process accepts
// is plain HTTP even when the browser spoke HTTPS. The first pass restores the
// browser's scheme from X-Forwarded-Proto; the second resolves the real client
// address from X-Forwarded-For, which the public chat rate limits need. Both
// believe their header only when the connection came from a trusted proxy,
// because both headers are attacker-controlled input. See ForwardedHeadersSetup.
//
// Two passes rather than one, and scheme before address. One pass opted in to
// both headers abandons all of its work at the first unparsable address, which
// would let a proxy forwarding one malformed value cost every request its scheme
// and take the whole deployment down. And the address pass rewrites the peer
// address that both trust checks read, so running it first would have the scheme
// pass compare the client's address to the trusted-proxy list and silently
// discard the scheme.
//
// Everything after this point may read Request.Scheme: the antiforgery service
// refuses to issue a token under a Secure cookie policy over a non-HTTPS request,
// and the cookie handler decides whether to mark cookies Secure. Both would be
// deciding from a false premise if this ran later.
ForwardedHeaderPolicy forwarded = app.Services.GetRequiredService<ForwardedHeaderPolicy>();

app.UseForwardedHeaders(forwarded.Scheme);
app.UseForwardedHeaders(forwarded.ClientAddress);

// The fail-safe. Outside Development, a request that did not arrive over HTTPS is
// refused rather than served with downgraded cookies -- see
// SecureTransportMiddleware for why the alternative was rejected. Placed
// immediately after the scheme is established and before anything that could
// issue a cookie.
app.UseOmniAiSecureTransport();

app.UseExceptionHandler();

// Fills in a Problem Details body for error responses produced by the pipeline
// rather than by a handler -- notably the 401 the cookie scheme writes when an
// unauthenticated request reaches a protected endpoint, which would otherwise
// have no body at all.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Establishes who the caller is, then enforces what each endpoint requires.
// Order matters and is not interchangeable: authorization can only act on an
// identity that authentication has already resolved.
app.UseAuthentication();
app.UseAuthorization();

// Liveness probe. Intentionally dependency-free: it answers "is the process up",
// not "are downstream services healthy". Readiness checks arrive with the
// components they check.
//
// Anonymous, deliberately. A liveness probe that requires a session cannot be
// called by the thing that needs it, and it discloses nothing but that the
// process is running.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("GetHealth")
   .AllowAnonymous();

// Registration, sign-in, sign-out, the current account and the antiforgery
// bootstrap. Three of these are necessarily anonymous; each says so at its own
// mapping rather than inheriting anonymity from the group.
app.MapAuthEndpoints();

// The workspaces the signed-in person belongs to. This is the only source of
// tenant ids the admin application may use.
app.MapMeEndpoints();

// Tenant onboarding. Authenticated: creating a workspace makes the creator its
// first member, and reading one requires membership.
app.MapTenantEndpoints();

// Role administration and the capability catalogue, scoped to the workspace.
// Reading needs roles.view, writing needs roles.manage, and neither implies the
// other. There is deliberately no member-assignment surface here.
app.MapRoleEndpoints();

// Team administration: members, their role assignments, and invitations.
//
// Reading needs members.view, inviting needs members.invite, and handing out
// roles needs members.roles.manage -- none of which implies another. Invitation
// acceptance is mapped by the same call but sits outside the workspace group,
// because the person accepting is not a member yet.
app.MapTeamEndpoints();

// AI agent configuration, scoped to the tenant that owns each agent.
//
// Authenticated, and both membership and permission are checked inside every use
// case. The tenant id in the route is still supplied by the caller, but it is not
// believed: it addresses a resource, and reaching that resource requires a
// membership row pairing the signed-in person with that tenant, and a role of
// theirs granting the capability the operation needs. The database predicates
// that scope every query to the tenant remain in place beneath both checks, so
// three independent layers would have to fail for one tenant's data to reach
// another's request.
app.MapAiAgentEndpoints();

// Website chat administration: which agent is reachable from the public web, and
// whether it is switched on. Authenticated, and permission-checked inside every
// use case.
app.MapKnowledgeEndpoints();
app.MapWebChatEndpoints();
app.MapWhatsAppEndpoints();

// Reading the conversations customers have actually had. Read-only in this
// release: there is no reply, no assignment and no handoff yet.
app.MapConversationEndpoints();

// The hosted chat page's own API. Anonymous by design -- a customer has no
// account -- and the only surface in the platform where an unauthenticated
// request reaches an AI provider. Access to a conversation comes from a
// per-conversation credential in an HttpOnly cookie, never from a session, and it
// grants nothing beyond that one conversation.
app.MapHostedChatEndpoints();
app.MapEmbeddedChatEndpoints();
app.MapWidgetLoaderEndpoints();

// Meta's webhook. Anonymous, like the widget routes above it, and authenticated
// by an HMAC over the exact request body rather than by a session.
app.MapWhatsAppWebhookEndpoints();

// The realtime hubs.
//
// Mapped after authentication and authorisation, so the admin hub sees the
// signed-in principal the rest of the application sees. The public hub is
// anonymous and authorises itself from the ticket its connection carries -- the
// visitor credential never reaches this far, by design.
app.MapHub<WorkspaceInboxHub>("/hubs/workspace-inbox");
app.MapHub<PublicChatHub>("/hubs/public-chat");

// TLS is terminated by the reverse proxy in front of this process, so the
// application deliberately does not perform its own HTTPS redirection -- there is
// nothing to redirect to from here, and the proxy is where that belongs.
//
// What the application does insist on is knowing the truth about the scheme: the
// forwarded-headers middleware above restores it from a trusted proxy, every
// cookie requires Secure outside Development, and a request that arrives without
// a trusted HTTPS context is refused rather than served insecurely.

await app.RunAsync();

/// <summary>
/// Exposed so the API integration test project can host this application in memory.
/// </summary>
public partial class Program;
