using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using OmniAI.Application.Knowledge.Files;

namespace OmniAI.Api.Http;

/// <summary>
/// What the generated OpenAPI document cannot infer by itself (Phase 38.7b, BA-5).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Metadata only.</strong> Nothing here changes a route, a status, a
/// header or a body. It describes what the API already does, so a client's types
/// can be checked against the document instead of being written by hand.
/// </para>
/// <list type="bullet">
/// <item>The problem body's extension members (<c>code</c>, <c>codes</c>,
/// <c>limit</c>, <c>limitUnit</c>, <c>entitlement</c>, <c>current</c>,
/// <c>periodFrom</c>, <c>periodTo</c>, <c>traceId</c>, and the merged-customer
/// ids), which the problem writer adds at run time and the generator never
/// sees.</item>
/// <item>A member written only when it has a value
/// (<c>JsonIgnoreCondition.WhenWritingNull</c>, such as the dashboard's
/// <c>previous</c> or the workspace's <c>scheduleCount</c>) is optional: absent
/// is its normal state, so it is not listed as required.</item>
/// <item>The invitation list's <c>status</c> parameter, which the endpoint reads
/// from the query itself so that an unknown value can be refused with its own
/// code.</item>
/// <item>The public name: readers of the document see Nabra, not the internal
/// code name.</item>
/// </list>
/// <para>
/// <c>OpenApiDocumentContractTests</c> reads the document from the test host and
/// checks each item, so none of it drifts back.
/// </para>
/// </remarks>
internal static class OpenApiContract
{
    /// <summary>The operation whose <c>status</c> parameter is read by hand.</summary>
    private const string ListInvitations = "ListTenantInvitations";

    public static IServiceCollection AddNabraOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info ??= new OpenApiInfo();
                document.Info.Title = "Nabra API";

                return Task.CompletedTask;
            });

            options.AddSchemaTransformer((schema, context, _) =>
            {
                Type type = context.JsonTypeInfo.Type;

                if (typeof(ProblemDetails).IsAssignableFrom(type))
                {
                    DescribeProblemMembers(schema);
                }

                OmitWhenNullIsOptional(schema, context.JsonTypeInfo.Properties);

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, _, _) =>
            {
                if (string.Equals(operation.OperationId, ListInvitations, StringComparison.Ordinal))
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "status",
                        In = ParameterLocation.Query,
                        Required = false,
                        Description = "Only invitations in this state, in any letter case. "
                            + "A pending invitation past its expiry is Expired. "
                            + "Anything else is 400 invitations.status.invalid.",
                        Schema = Text(
                            "One of Pending, Accepted, Revoked, Expired.",
                            "Pending",
                            "Accepted",
                            "Revoked",
                            "Expired"),
                    });
                }

                return Task.CompletedTask;
            });
        });

    /// <summary>Adds the members the problem writer sets beside the standard five.</summary>
    /// <remarks>
    /// All optional: each appears only on the refusals that carry it, as
    /// <c>docs/architecture/problem-codes.md</c> lists.
    /// </remarks>
    private static void DescribeProblemMembers(OpenApiSchema schema)
    {
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        schema.Properties["code"] = Text(
            "The stable code of the refusal (problem-codes.md). Present on every 400, 409, 412, "
            + "422 and 428 this API writes, and on some other statuses. A client words the "
            + "refusal by it and never shows detail.");
        schema.Properties["codes"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema { Type = JsonSchemaType.String },
            Description = "Every rule that refused, in order, when there were several "
                + "(registration and password reset); code is the first.",
        };
        schema.Properties["limit"] = Number(
            "The bound that was broken: for knowledge file refusals in the unit limitUnit "
            + "names; for an entitlement, the plan's cap of that entitlement.");
        schema.Properties["limitUnit"] = Text(
            "What limit counts, always beside it on a knowledge file refusal (Phase 38.7b).",
            [.. KnowledgeFileLimitUnits.All.Order(StringComparer.Ordinal)]);
        schema.Properties["entitlement"] = Text(
            "The entitlement a plan limit refused (entitlement.<kind>.exhausted).");
        schema.Properties["current"] = Number(
            "How much of the entitlement the workspace already uses.");
        schema.Properties["periodFrom"] = Instant(
            "Start of the period a monthly entitlement is counted in, when it has one.");
        schema.Properties["periodTo"] = Instant(
            "End of that period.");
        schema.Properties["traceId"] = Text(
            "The request's trace id, for an operator. Carries no meaning for a client.");
        schema.Properties["customerId"] = Identifier(
            "customers.merged: the customer the request named, which was merged away.");
        schema.Properties["canonicalCustomerId"] = Identifier(
            "customers.merged: the customer it was merged into, which is the one to use.");
    }

    /// <summary>
    /// A member serialised only when it has a value is not required: absent is
    /// its normal state.
    /// </summary>
    private static void OmitWhenNullIsOptional(
        OpenApiSchema schema,
        IList<System.Text.Json.Serialization.Metadata.JsonPropertyInfo> properties)
    {
        if (schema.Required is not { Count: > 0 } required)
        {
            return;
        }

        foreach (System.Text.Json.Serialization.Metadata.JsonPropertyInfo property in properties)
        {
            JsonIgnoreAttribute? ignore = (property.AttributeProvider as MemberInfo)
                ?.GetCustomAttribute<JsonIgnoreAttribute>();

            if (ignore?.Condition is JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault)
            {
                required.Remove(property.Name);
            }
        }
    }

    private static OpenApiSchema Text(string description, params string[] values)
    {
        OpenApiSchema schema = new() { Type = JsonSchemaType.String, Description = description };

        if (values.Length > 0)
        {
            schema.Enum = [.. values.Select(value => (JsonNode)JsonValue.Create(value))];
        }

        return schema;
    }

    private static OpenApiSchema Number(string description) =>
        new() { Type = JsonSchemaType.Integer, Format = "int64", Description = description };

    private static OpenApiSchema Instant(string description) =>
        new() { Type = JsonSchemaType.String, Format = "date-time", Description = description };

    private static OpenApiSchema Identifier(string description) =>
        new() { Type = JsonSchemaType.String, Format = "uuid", Description = description };
}
