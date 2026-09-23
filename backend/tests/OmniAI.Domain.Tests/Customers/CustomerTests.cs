using OmniAI.Domain;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Customers;

/// <summary>
/// The canonical customer, and the rules that keep two people from becoming one
/// by accident.
/// </summary>
public sealed class CustomerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------ the label

    [Fact]
    public void A_customer_starts_with_no_name_and_that_is_normal()
    {
        // Most customers never have one. An anonymous visitor has no name to
        // record, and the platform does not invent one.
        Customer customer = Customer.Create(TenantId.New(), displayName: null, Noon);

        Assert.Null(customer.DisplayName);
        Assert.True(customer.IsCanonical);
        Assert.False(customer.IsMerged);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_and_absent_are_the_same_answer(string? value)
    {
        // Clearing the field is how somebody removes a label they should not have
        // written, so an empty form field must not be a validation error.
        Assert.Null(CustomerDisplayName.CreateOrNull(value));
    }

    [Fact]
    public void A_name_is_trimmed_but_never_otherwise_repaired()
    {
        Assert.Equal("Dana Okafor", CustomerDisplayName.Create("  Dana Okafor  ").Value);
    }

    [Fact]
    public void A_name_longer_than_the_bound_is_refused_rather_than_truncated()
    {
        Assert.Throws<DomainValidationException>(
            () => CustomerDisplayName.Create(new string('a', CustomerDisplayName.MaxLength + 1)));
    }

    [Theory]
    [InlineData("Dana\nOkafor")]
    [InlineData("Dana\tOkafor")]
    [InlineData("Dana\u0000Okafor")]
    public void A_name_with_a_control_character_is_refused(string value)
    {
        // It is a single-line label. A newline in one is a paste accident, and
        // storing it would break every list that renders one row per customer.
        Assert.Throws<DomainValidationException>(() => CustomerDisplayName.Create(value));
    }

    [Fact]
    public void Markup_in_a_name_is_stored_exactly_as_typed()
    {
        // The Domain does not escape; rendering does. Sanitising here would mean
        // the stored value silently differed from what somebody typed, and would
        // put the platform's only defence in the layer least able to know the
        // output context.
        const string Hostile = "<script>alert(1)</script>";

        Assert.Equal(Hostile, CustomerDisplayName.Create(Hostile).Value);
    }

    // ----------------------------------------------------------- renaming

    [Fact]
    public void Renaming_to_the_same_label_changes_nothing_and_says_so()
    {
        Customer customer = Customer.Create(
            TenantId.New(),
            CustomerDisplayName.Create("Dana"),
            Noon);

        Assert.False(customer.Rename(CustomerDisplayName.Create("Dana"), Noon.AddHours(1)));
        Assert.Equal(Noon, customer.UpdatedAt);
    }

    [Fact]
    public void Renaming_records_the_change_and_the_instant()
    {
        Customer customer = Customer.Create(TenantId.New(), displayName: null, Noon);

        Assert.True(customer.Rename(CustomerDisplayName.Create("Dana"), Noon.AddHours(1)));
        Assert.Equal("Dana", customer.DisplayName?.Value);
        Assert.Equal(Noon.AddHours(1), customer.UpdatedAt);
    }

    [Fact]
    public void A_merged_record_cannot_be_renamed()
    {
        (Customer source, Customer target) = Pair();

        source.MergeInto(target, UserId.New(), Noon);

        CustomerAlreadyMergedException failure = Assert.Throws<CustomerAlreadyMergedException>(
            () => source.Rename(CustomerDisplayName.Create("Dana"), Noon));

        // The caller is told where to go rather than merely refused.
        Assert.Equal(target.Id, failure.CanonicalId);
    }

    // -------------------------------------------------------------- merging

    [Fact]
    public void Merging_points_the_source_at_the_target_and_records_who_and_when()
    {
        (Customer source, Customer target) = Pair();

        UserId administrator = UserId.New();

        source.MergeInto(target, administrator, Noon);

        Assert.Equal(target.Id, source.MergedIntoCustomerId);
        Assert.Equal(Noon, source.MergedAt);
        Assert.Equal(administrator, source.MergedBy);
        Assert.True(source.IsMerged);

        // The target is untouched. It is the record that survives, and a merge is
        // not an edit of it.
        Assert.True(target.IsCanonical);
        Assert.Null(target.MergedIntoCustomerId);
    }

    [Fact]
    public void A_customer_cannot_be_merged_into_itself()
    {
        Customer customer = Customer.Create(TenantId.New(), displayName: null, Noon);

        Assert.Throws<CustomerMergeNotAllowedException>(
            () => customer.MergeInto(customer, UserId.New(), Noon));
    }

    [Fact]
    public void A_customer_cannot_be_merged_across_workspaces()
    {
        // Two workspaces' customers are two populations. A merge across them
        // would move one workspace's conversations under a record the other can
        // read.
        Customer source = Customer.Create(TenantId.New(), displayName: null, Noon);
        Customer target = Customer.Create(TenantId.New(), displayName: null, Noon);

        Assert.Throws<CustomerMergeNotAllowedException>(
            () => source.MergeInto(target, UserId.New(), Noon));
    }

    [Fact]
    public void An_already_merged_record_cannot_be_merged_somewhere_else()
    {
        TenantId workspace = TenantId.New();

        Customer source = Customer.Create(workspace, displayName: null, Noon);
        Customer first = Customer.Create(workspace, displayName: null, Noon);
        Customer second = Customer.Create(workspace, displayName: null, Noon);

        source.MergeInto(first, UserId.New(), Noon);

        CustomerAlreadyMergedException failure = Assert.Throws<CustomerAlreadyMergedException>(
            () => source.MergeInto(second, UserId.New(), Noon));

        // Silently remapping it would move a history a second time on the say-so
        // of somebody who did not know it had already moved once.
        Assert.Equal(first.Id, failure.CanonicalId);
        Assert.Equal(first.Id, source.MergedIntoCustomerId);
    }

    [Fact]
    public void A_merged_record_cannot_be_a_merge_target()
    {
        // This is the rule that makes a cycle impossible rather than unlikely: a
        // record that points at somebody is no longer a legal destination, so
        // A -> B leaves nothing for B -> A to attach to, and no chain can form for
        // a longer cycle to close.
        TenantId workspace = TenantId.New();

        Customer a = Customer.Create(workspace, displayName: null, Noon);
        Customer b = Customer.Create(workspace, displayName: null, Noon);
        Customer c = Customer.Create(workspace, displayName: null, Noon);

        a.MergeInto(b, UserId.New(), Noon);

        // b -> a is refused because a is merged.
        Assert.Throws<CustomerAlreadyMergedException>(
            () => b.MergeInto(a, UserId.New(), Noon));

        // c -> a is refused for the same reason, so no chain c -> a -> b forms.
        Assert.Throws<CustomerAlreadyMergedException>(
            () => c.MergeInto(a, UserId.New(), Noon));

        Assert.True(b.IsCanonical);
        Assert.True(c.IsCanonical);
    }

    [Fact]
    public void Every_merge_graph_is_one_hop_deep()
    {
        // Stated as a property rather than as three examples: whatever order a
        // workspace merges in, no record ever points at a record that points
        // somewhere.
        TenantId workspace = TenantId.New();

        Customer survivor = Customer.Create(workspace, displayName: null, Noon);

        List<Customer> merged = [];

        for (int i = 0; i < 5; i++)
        {
            Customer next = Customer.Create(workspace, displayName: null, Noon);

            next.MergeInto(survivor, UserId.New(), Noon);

            merged.Add(next);
        }

        Assert.All(merged, record => Assert.Equal(survivor.Id, record.MergedIntoCustomerId));
        Assert.True(survivor.IsCanonical);
    }

    // ------------------------------------------------- the conversation link

    [Fact]
    public void A_conversation_must_name_a_customer()
    {
        Assert.Throws<DomainValidationException>(
            () => Conversation.Start(
                TenantId.New(),
                default,
                ChannelKind.WebChat,
                Noon));
    }

    [Fact]
    public void A_conversation_carries_the_customer_it_was_started_for()
    {
        CustomerId customer = CustomerId.New();

        Conversation conversation = Conversation.Start(
            TenantId.New(),
            customer,
            ChannelKind.WhatsApp,
            Noon);

        Assert.Equal(customer, conversation.CustomerId);
    }

    [Fact]
    public void Moving_a_conversation_to_a_customer_touches_nothing_else()
    {
        // A merge changes who a conversation belongs to and nothing about what was
        // said in it. The transcript, its order and the activity high-water mark
        // are the things a customer-identity change must never disturb.
        Conversation conversation = Conversation.Start(
            TenantId.New(),
            CustomerId.New(),
            ChannelKind.WebChat,
            Noon);

        conversation.AddTextMessage(MessageSender.Customer, "First", Noon.AddMinutes(1));
        conversation.AddTextMessage(MessageSender.AiAgent, "Second", Noon.AddMinutes(2));

        DateTimeOffset activity = conversation.LastActivityAt;
        string[] before = [.. conversation.Messages.Select(message => message.Text)];

        CustomerId canonical = CustomerId.New();

        conversation.MoveToCustomer(canonical);

        Assert.Equal(canonical, conversation.CustomerId);
        Assert.Equal(activity, conversation.LastActivityAt);
        Assert.Equal(before, conversation.Messages.Select(message => message.Text));
        Assert.Equal(Noon, conversation.StartedAt);
    }

    [Fact]
    public void A_conversation_cannot_be_moved_to_nobody()
    {
        Conversation conversation = Conversation.Start(
            TenantId.New(),
            CustomerId.New(),
            ChannelKind.WebChat,
            Noon);

        Assert.Throws<DomainValidationException>(() => conversation.MoveToCustomer(default));
    }

    private static (Customer Source, Customer Target) Pair()
    {
        TenantId workspace = TenantId.New();

        return (
            Customer.Create(workspace, displayName: null, Noon),
            Customer.Create(workspace, displayName: null, Noon));
    }
}
