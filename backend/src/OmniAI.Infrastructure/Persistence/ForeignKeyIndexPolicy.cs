using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Lets a relationship say, in the model, that it must not get EF's automatic
/// foreign-key index.
/// </summary>
/// <remarks>
/// <para>
/// EF indexes every foreign key whose columns do not already lead an index or a
/// key. That is the right default and almost every relationship here keeps it.
/// A few do not, and the reasons are accepted schema decisions rather than
/// oversights: the reply table's tenant index serves no query
/// (<c>HandoffSchemaTests</c> asserts it is absent); the chunk table's
/// document key is already led by <c>ux_knowledge_chunks_document_ordinal</c>;
/// the assignee key is served by the partial <c>ix_conversations_assigned_to</c>.
/// </para>
/// <para>
/// Before Phase 33.1 those decisions lived only in hand-edited migrations, so a
/// migration regenerated from the model quietly brought the indexes back. They
/// now live on the relationship itself, where the migration generator reads
/// them.
/// </para>
/// <para>
/// This derives from EF's own <see cref="ForeignKeyIndexConvention"/> and
/// overrides its one documented hook, <c>CreateIndex</c>. Every other
/// relationship behaves exactly as it did.
/// </para>
/// </remarks>
internal static class ForeignKeyIndexPolicy
{
    /// <summary>The model annotation a relationship carries to opt out.</summary>
    public const string SuppressAnnotation = "OmniAI:SuppressForeignKeyIndex";

    /// <summary>
    /// Marks the relationship as deliberately not indexed by convention.
    /// </summary>
    /// <remarks>
    /// Use only where an existing index already serves the key, or where an
    /// accepted decision says nothing should. Say which in a comment beside the
    /// call.
    /// </remarks>
    public static ReferenceCollectionBuilder<TPrincipal, TDependent> WithoutConventionIndex<TPrincipal, TDependent>(
        this ReferenceCollectionBuilder<TPrincipal, TDependent> builder)
        where TPrincipal : class
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.HasAnnotation(SuppressAnnotation, true);
    }

    /// <summary>Installs the policy in place of EF's default convention.</summary>
    public static void Apply(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Conventions.Replace<ForeignKeyIndexConvention>(
            services => new PolicyConvention(
                services.GetService(typeof(ProviderConventionSetBuilderDependencies))
                    as ProviderConventionSetBuilderDependencies
                ?? throw new InvalidOperationException(
                    "EF Core did not provide its convention dependencies.")));
    }

    private static bool IsSuppressed(IReadOnlyForeignKey foreignKey) =>
        foreignKey[SuppressAnnotation] is true;

    private sealed class PolicyConvention(ProviderConventionSetBuilderDependencies dependencies)
        : ForeignKeyIndexConvention(dependencies)
    {
        // The annotation is set after the relationship is created, so the base
        // convention has usually built the index already by the time a
        // configuration marks it. Finalizing removes those; the override below
        // stops the base from putting them back when it sees the removal.
        public override void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (IConventionForeignKey foreignKey in entityType.GetDeclaredForeignKeys().Where(IsSuppressed).ToList())
                {
                    IConventionIndex? index = entityType.FindIndex(foreignKey.Properties);

                    // Only an index the convention made. One a configuration
                    // declared on purpose is left exactly where it is.
                    if (index is not null && index.GetConfigurationSource() == ConfigurationSource.Convention)
                    {
                        entityType.Builder.HasNoIndex(index);
                    }
                }
            }

            base.ProcessModelFinalizing(modelBuilder, context);
        }

        protected override IConventionIndex? CreateIndex(
            IReadOnlyList<IConventionProperty> properties,
            bool unique,
            IConventionEntityTypeBuilder entityTypeBuilder)
        {
            IReadOnlyList<IConventionForeignKey> matching =
                [.. entityTypeBuilder.Metadata.FindForeignKeys(properties)];

            if (matching.Count > 0 && matching.All(IsSuppressed))
            {
                return null;
            }

            return base.CreateIndex(properties, unique, entityTypeBuilder);
        }
    }
}
