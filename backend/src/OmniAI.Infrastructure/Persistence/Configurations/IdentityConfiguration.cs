using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Identity;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps ASP.NET Core Identity's tables onto this platform's schema conventions.
/// </summary>
/// <remarks>
/// <strong>The default names were not accepted.</strong> Identity ships with
/// <c>AspNetUsers</c>, <c>Id</c>, <c>NormalizedEmail</c> and friends; every other
/// table in this database is <c>snake_case</c> with <c>snake_case</c> columns.
/// Leaving the defaults would have produced a schema where half the tables shout
/// which framework wrote them, and where a hand-written query has to remember
/// which convention applies to which table. The rename costs one file and is
/// verified against the real migration.
/// <para>
/// <strong>Renamed, not reshaped.</strong> Not one column is dropped, widened or
/// retyped. Identity's stores read and write these columns by name through their
/// own model, and quietly removing one -- a token column that looks unused, say --
/// would break a store method that is only reached in a flow nobody exercised
/// yet. What is here is Identity's schema wearing this platform's naming.
/// </para>
/// <para>
/// Applied after <c>IdentityUserContext.OnModelCreating</c> has run, through the
/// same <c>ApplyConfigurationsFromAssembly</c> call that configures the business
/// tables, so these settings win over the defaults.
/// </para>
/// </remarks>
internal sealed class OmniAiUserConfiguration : IEntityTypeConfiguration<OmniAiUser>
{
    /// <summary>
    /// The bound Identity applies to its own string columns.
    /// </summary>
    /// <remarks>
    /// Identity's default key length, used for every user-supplied string it
    /// stores. Restated rather than inherited by accident, because these become
    /// real <c>character varying</c> lengths in the migration and a reviewer
    /// should be able to see where the number came from.
    /// </remarks>
    private const int MaxKeyLength = 256;

    public void Configure(EntityTypeBuilder<OmniAiUser> builder)
    {
        builder.ToTable(PersistenceSchema.UsersTable, table =>
            table.HasCheckConstraint("ck_users_language", "language IS NULL OR language IN ('en', 'ar')"));

        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.UserName).HasColumnName("user_name").HasMaxLength(MaxKeyLength);
        builder.Property(user => user.NormalizedUserName)
            .HasColumnName("normalized_user_name")
            .HasMaxLength(MaxKeyLength);
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(MaxKeyLength);
        builder.Property(user => user.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(MaxKeyLength);
        builder.Property(user => user.EmailConfirmed).HasColumnName("email_confirmed");
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash");
        builder.Property(user => user.SecurityStamp).HasColumnName("security_stamp");
        builder.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        builder.Property(user => user.PhoneNumber).HasColumnName("phone_number");
        builder.Property(user => user.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
        builder.Property(user => user.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        builder.Property(user => user.LockoutEnd).HasColumnName("lockout_end");
        builder.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled");
        builder.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count");

        // Phase 37.3 (M1). Nullable: an account that has not chosen has none, and
        // the server writes to it in English. No index: read by primary key only.
        builder.Property(user => user.Language)
            .HasColumnName("language")
            .HasMaxLength(UserLanguages.MaxCodeLength);

        // Phase 39.1 (AddUserDisplayName). Nullable, no backfill: an account
        // without a name is shown by its email, as before. No index: read by
        // primary key, beside the email, in the queries that already read it.
        builder.Property(user => user.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(MemberDisplayName.MaxLength);

        // Identity's own indexes, renamed. The normalized-email index is made
        // unique here rather than left as Identity's non-unique default: email is
        // the login identifier for this platform, RequireUniqueEmail is on, and a
        // uniqueness rule that lives only in a UserManager option is enforced by
        // a read-then-write that two concurrent registrations can both pass. The
        // index is what makes exactly one of them win.
        builder.HasIndex(user => user.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ix_users_normalized_user_name");

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ix_users_normalized_email");

        // The three satellite relationships the base context declares, restated
        // only to name their constraints. EF matches an existing relationship by
        // its foreign key property, so these rename rather than duplicate.
        //
        // Cascade is kept, and it is right here in a way it is emphatically not
        // right for tenant_memberships: a claim, an external login and a token are
        // parts of the account rather than things that merely reference it, so
        // they have no meaning once it is gone. A membership is the opposite -- it
        // is what makes a workspace full of business data reachable.
        builder.HasMany<IdentityUserClaim<Guid>>()
            .WithOne()
            .HasForeignKey(claim => claim.UserId)
            .HasConstraintName("fk_user_claims_user")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasMany<IdentityUserLogin<Guid>>()
            .WithOne()
            .HasForeignKey(login => login.UserId)
            .HasConstraintName("fk_user_logins_user")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasMany<IdentityUserToken<Guid>>()
            .WithOne()
            .HasForeignKey(token => token.UserId)
            .HasConstraintName("fk_user_tokens_user")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}

/// <summary>Renames Identity's user-claim table.</summary>
/// <remarks>
/// Unused by Phase 12 -- no claim is stored in the database, and the sign-in
/// cookie carries only what Identity puts in it. The table exists because
/// <c>UserOnlyStore</c>, the store Identity resolves for a role-free
/// configuration, implements <c>IUserClaimStore</c> and reads this set. Dropping
/// it would mean writing a bespoke store: re-implementing credential persistence
/// to avoid an empty table is a poor trade in a security-critical component, and
/// the table is where per-user claims will live when they are needed.
/// </remarks>
internal sealed class OmniAiUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        builder.ToTable("user_claims");

        builder.Property(claim => claim.Id).HasColumnName("id");
        builder.Property(claim => claim.UserId).HasColumnName("user_id");
        builder.Property(claim => claim.ClaimType).HasColumnName("claim_type");
        builder.Property(claim => claim.ClaimValue).HasColumnName("claim_value");

        // The index the relationship creates anyway, named to the convention.
        builder.HasIndex(claim => claim.UserId).HasDatabaseName("ix_user_claims_user");
    }
}

/// <summary>Renames Identity's external-login table.</summary>
/// <remarks>
/// Empty in this phase and expected to stay empty: there is no SSO, no OIDC and
/// no social sign-in, and none is planned before a dedicated phase. Present for
/// the same reason as the claims table -- the standard store contract requires
/// the set.
/// </remarks>
internal sealed class OmniAiUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.ToTable("user_logins");

        builder.Property(login => login.LoginProvider).HasColumnName("login_provider");
        builder.Property(login => login.ProviderKey).HasColumnName("provider_key");
        builder.Property(login => login.ProviderDisplayName).HasColumnName("provider_display_name");
        builder.Property(login => login.UserId).HasColumnName("user_id");

        builder.HasIndex(login => login.UserId).HasDatabaseName("ix_user_logins_user");
    }
}

/// <summary>Renames Identity's user-token table.</summary>
/// <remarks>
/// Where password-reset, email-confirmation and two-factor tokens are persisted
/// when those flows exist. All three are explicitly deferred, so this table is
/// empty today. It is not a refresh-token store: this platform issues no bearer
/// tokens at all.
/// </remarks>
internal sealed class OmniAiUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.ToTable("user_tokens");

        builder.Property(token => token.UserId).HasColumnName("user_id");
        builder.Property(token => token.LoginProvider).HasColumnName("login_provider");
        builder.Property(token => token.Name).HasColumnName("name");
        builder.Property(token => token.Value).HasColumnName("value");
    }
}

/// <summary>
/// Maps <see cref="UserReferenceRow"/>: the users table as a foreign-key target,
/// never created or altered by a migration.
/// </summary>
/// <remarks>
/// See <see cref="UserReferenceRow"/> for why it exists. The key name is never
/// emitted, because the table is excluded from migrations; it only has to differ
/// from Identity's <c>PK_users</c>, or EF renames that one to keep names unique.
/// </remarks>
internal sealed class UserReferenceRowConfiguration : IEntityTypeConfiguration<UserReferenceRow>
{
    public void Configure(EntityTypeBuilder<UserReferenceRow> builder)
    {
        builder.ToTable(
            PersistenceSchema.UsersTable,
            "public",
            table => table.ExcludeFromMigrations());

        builder.HasKey(user => user.Id).HasName("pk_users_reference");

        builder.Property(user => user.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.UserId)
            .ValueGeneratedNever();
    }
}
