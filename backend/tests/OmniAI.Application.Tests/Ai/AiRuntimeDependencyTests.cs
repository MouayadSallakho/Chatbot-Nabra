using System.Reflection;
using OmniAI.Application.Ai;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Ai;

/// <summary>
/// Executable guard for the AI runtime contracts: they must be plain Application
/// code, naming no vendor and no infrastructure technology.
/// </summary>
/// <remarks>
/// <para>
/// Two complementary checks, and neither scans type names for substrings -- a
/// test that searched for the letters "openai" would pass for a class called
/// <c>GptClient</c> and fail for a business term that happened to contain them.
/// </para>
/// <list type="number">
/// <item>
/// <strong>Assembly references.</strong> A provider SDK, EF Core, Npgsql or
/// ASP.NET cannot be used without the compiler recording a reference, so their
/// absence from the metadata is direct evidence rather than an inference.
/// </item>
/// <item>
/// <strong>Type signatures.</strong> Every type these contracts expose is
/// resolved and its defining assembly checked. This catches the case a reference
/// check cannot reach on its own -- a contract that has begun to speak in some
/// other library's types.
/// </item>
/// </list>
/// </remarks>
public sealed class AiRuntimeDependencyTests
{
    /// <summary>The AI runtime contracts, by their real types rather than by name.</summary>
    private static readonly Type[] Contracts =
    [
        typeof(IAiTextGenerator),
        typeof(AiTextGenerationRequest),
        typeof(AiTextGenerationResult),
        typeof(AiTurn),
        typeof(AiTurnRole),
        typeof(AiRuntimeUnavailableException),
        typeof(AiResponseInvalidException),
        typeof(AiTurnValidationException),
    ];

    [Fact]
    public void The_application_references_no_ai_provider_sdk()
    {
        // Phase 8 establishes the abstraction and deliberately implements no
        // provider. A reference appearing here would mean a vendor SDK had been
        // used from a use case -- the exact coupling this architecture exists to
        // prevent, and the reason a provider can later be swapped without any of
        // this code changing.
        string[] forbidden =
        [
            "OpenAI",
            "Azure.AI.OpenAI",
            "Anthropic",
            "Anthropic.SDK",
            "Betalgo.OpenAI",
            "OpenAI_API",
            "Microsoft.Extensions.AI",
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.SemanticKernel",
            "Microsoft.SemanticKernel.Abstractions",
            "LangChain",
            "Facebook",
            "Meta",
        ];

        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Application.dll")
                .Where(name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_ai_contracts_speak_only_in_platform_and_framework_types()
    {
        // Everything these contracts expose -- parameters, return types, property
        // types -- must come from the .NET base libraries or from this platform.
        // A type from anywhere else would mean the abstraction had adopted
        // somebody else's vocabulary, which is how a "provider-independent"
        // contract quietly stops being one.
        string[] offenders = [.. Contracts.SelectMany(ExposedTypes)
            .Select(type => type.Assembly.GetName().Name ?? string.Empty)
            .Where(assembly => !IsPlatformOrFramework(assembly))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_ai_contracts_live_in_the_application_assembly()
    {
        // The abstraction is owned by the layer that needs text generated, not by
        // the layer that eventually calls a vendor. If one of these moved to
        // Infrastructure, every use case would start depending outward.
        Assert.All(Contracts, type =>
            Assert.Equal("OmniAI.Application", type.Assembly.GetName().Name));
    }

    [Fact]
    public void The_domain_knows_nothing_about_ai_generation()
    {
        // The Domain models what an AI agent is. It does not generate anything,
        // and it must not reference the contract that does -- checked by resolving
        // the real types rather than by searching for names.
        Assembly domain = typeof(Domain.AiAgents.AiAgent).Assembly;

        Assert.All(Contracts, type => Assert.NotEqual(domain, type.Assembly));

        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        Assert.DoesNotContain(
            domain.GetTypes().SelectMany(type => type.GetProperties(Everything)),
            property => Contracts.Contains(property.PropertyType));

        Assert.DoesNotContain(
            domain.GetTypes().SelectMany(type => type.GetMethods(Everything)),
            method => Contracts.Contains(method.ReturnType)
                || method.GetParameters().Any(parameter => Contracts.Contains(parameter.ParameterType)));
    }

    /// <summary>Every type an AI contract exposes through its public surface.</summary>
    private static IEnumerable<Type> ExposedTypes(Type contract)
    {
        foreach (PropertyInfo property in contract.GetProperties())
        {
            foreach (Type type in Unwrap(property.PropertyType))
            {
                yield return type;
            }
        }

        foreach (MethodInfo method in contract.GetMethods().Where(method => method.IsPublic))
        {
            // Object's own members are inherited by everything and say nothing
            // about this contract.
            if (method.DeclaringType == typeof(object))
            {
                continue;
            }

            foreach (Type type in Unwrap(method.ReturnType))
            {
                yield return type;
            }

            foreach (Type type in method.GetParameters().SelectMany(p => Unwrap(p.ParameterType)))
            {
                yield return type;
            }
        }
    }

    /// <summary>
    /// A type and, for a generic one, its arguments.
    /// </summary>
    /// <remarks>
    /// Without this, <c>Task&lt;SomeVendorResponse&gt;</c> would be judged solely
    /// on <c>Task</c> and the interesting half would go unchecked.
    /// </remarks>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (Type argument in type.GetGenericArguments().SelectMany(Unwrap))
        {
            yield return argument;
        }
    }

    /// <summary>
    /// Whether an assembly is this platform's own or part of the .NET base
    /// libraries.
    /// </summary>
    /// <remarks>
    /// The base libraries are matched by their real assembly identities --
    /// <c>System.*</c>, <c>System.Private.CoreLib</c>, <c>netstandard</c> --
    /// rather than by namespace text. EF Core, Npgsql and ASP.NET Core all ship
    /// under <c>Microsoft.*</c> and are deliberately not on this list, so a
    /// contract that started returning a <c>DbSet</c>, a <c>NpgsqlParameter</c>
    /// or an <c>IResult</c> fails.
    /// </remarks>
    private static bool IsPlatformOrFramework(string assembly) =>
        assembly.StartsWith("OmniAI.", StringComparison.Ordinal)
        || assembly.StartsWith("System.", StringComparison.Ordinal)
        || assembly is "System" or "mscorlib" or "netstandard";
}
