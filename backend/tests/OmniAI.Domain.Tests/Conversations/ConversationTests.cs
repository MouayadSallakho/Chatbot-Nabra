using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Conversations;

public sealed class ConversationTests
{
    // Fixed instants. Every timestamp is supplied by the caller, so these tests
    // never read the system clock and never vary between runs.
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OneMinuteLater = Start.AddMinutes(1);
    private static readonly DateTimeOffset TwoMinutesLater = Start.AddMinutes(2);
    private static readonly DateTimeOffset ThreeMinutesLater = Start.AddMinutes(3);

    private static Conversation StartConversation(ChannelKind channel = ChannelKind.WebChat) =>
        Conversation.Start(TenantId.New(), CustomerId.New(), channel, Start);

    // ---------------------------------------------------------------- creation

    [Fact]
    public void Start_records_the_owning_tenant()
    {
        TenantId tenantId = TenantId.New();

        Conversation conversation = Conversation.Start(tenantId, CustomerId.New(), ChannelKind.WhatsApp, Start);

        Assert.Equal(tenantId, conversation.TenantId);
    }

    [Fact]
    public void Start_records_the_channel()
    {
        Assert.Equal(ChannelKind.Instagram, StartConversation(ChannelKind.Instagram).Channel);
    }

    [Fact]
    public void Start_assigns_a_non_empty_identity()
    {
        Assert.False(StartConversation().Id.IsEmpty);
    }

    [Fact]
    public void Start_assigns_a_distinct_identity_to_each_conversation()
    {
        Assert.NotEqual(StartConversation().Id, StartConversation().Id);
    }

    [Fact]
    public void Start_opens_the_conversation()
    {
        Assert.Equal(ConversationStatus.Open, StartConversation().Status);
    }

    [Fact]
    public void Start_sets_both_timestamps_to_the_supplied_instant()
    {
        Conversation conversation = StartConversation();

        Assert.Equal(Start, conversation.StartedAt);
        Assert.Equal(Start, conversation.LastActivityAt);
    }

    [Fact]
    public void Start_begins_with_no_messages()
    {
        Assert.Empty(StartConversation().Messages);
    }

    [Fact]
    public void Start_rejects_an_uninitialised_tenant_id()
    {
        Assert.Throws<DomainValidationException>(
            () => Conversation.Start(default, CustomerId.New(), ChannelKind.WebChat, Start));
    }

    [Fact]
    public void Start_rejects_an_unrecognised_channel()
    {
        Assert.Throws<DomainValidationException>(
            () => Conversation.Start(TenantId.New(), CustomerId.New(), (ChannelKind)99, Start));
    }

    [Fact]
    public void Start_rejects_the_uninitialised_default_channel()
    {
        Assert.Throws<DomainValidationException>(
            () => Conversation.Start(TenantId.New(), CustomerId.New(), default, Start));
    }

    // -------------------------------------------------------------------- time

    [Fact]
    public void Start_rejects_an_unset_instant()
    {
        Assert.Throws<DomainValidationException>(
            () => Conversation.Start(TenantId.New(), CustomerId.New(), ChannelKind.WebChat, default));
    }

    [Fact]
    public void Start_normalises_the_instant_to_utc()
    {
        DateTimeOffset withOffset = new(2026, 1, 1, 15, 0, 0, TimeSpan.FromHours(3));

        Conversation conversation = Conversation.Start(TenantId.New(), CustomerId.New(), ChannelKind.WebChat, withOffset);

        Assert.Equal(TimeSpan.Zero, conversation.StartedAt.Offset);
        Assert.Equal(withOffset.UtcDateTime, conversation.StartedAt.UtcDateTime);
    }

    [Fact]
    public void Adding_a_message_rejects_an_unset_instant()
    {
        Conversation conversation = StartConversation();

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.Customer, "Hello", default));
    }

    [Fact]
    public void Adding_a_message_accepts_the_same_instant_as_the_last_activity()
    {
        Conversation conversation = StartConversation();

        conversation.AddTextMessage(MessageSender.Customer, "Hello", Start);

        Assert.Single(conversation.Messages);
    }

    // ------------------------------------------- out-of-order ingestion

    [Fact]
    public void A_delayed_message_sent_before_the_last_activity_is_accepted()
    {
        // Webhook delay, provider redelivery, queue retries and imports all mean
        // a message can be ingested after one that was sent later. The aggregate
        // must accept it rather than force adapters to reorder first.
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Sent later", ThreeMinutesLater);

        Message delayed = conversation.AddTextMessage(
            MessageSender.Customer, "Sent earlier, arrived late", TwoMinutesLater);

        Assert.Equal(2, conversation.Messages.Count);
        Assert.Equal("Sent earlier, arrived late", delayed.Text);
    }

    [Fact]
    public void A_delayed_message_keeps_its_own_timestamp()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Sent later", ThreeMinutesLater);

        Message delayed = conversation.AddTextMessage(
            MessageSender.Customer, "Sent earlier", TwoMinutesLater);

        // The supplied instant is recorded as given, never rewritten to "now"
        // or clamped to the conversation's last activity.
        Assert.Equal(TwoMinutesLater, delayed.SentAt);
    }

    [Fact]
    public void A_delayed_message_is_appended_in_ingestion_order_not_sorted()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Ingested first", ThreeMinutesLater);

        conversation.AddTextMessage(MessageSender.Customer, "Ingested second", TwoMinutesLater);

        // History is ingestion order. The aggregate never reorders or sorts.
        Assert.Equal(
            ["Ingested first", "Ingested second"],
            conversation.Messages.Select(message => message.Text));
    }

    [Fact]
    public void A_delayed_message_does_not_move_the_last_activity_backwards()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Sent later", ThreeMinutesLater);

        conversation.AddTextMessage(MessageSender.Customer, "Sent earlier", TwoMinutesLater);

        Assert.Equal(ThreeMinutesLater, conversation.LastActivityAt);
    }

    [Fact]
    public void A_later_message_still_moves_the_last_activity_forward()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Sent later", ThreeMinutesLater);
        conversation.AddTextMessage(MessageSender.Customer, "Sent earlier", TwoMinutesLater);

        conversation.AddTextMessage(MessageSender.Customer, "Newest", ThreeMinutesLater.AddMinutes(5));

        Assert.Equal(ThreeMinutesLater.AddMinutes(5), conversation.LastActivityAt);
    }

    [Fact]
    public void A_delayed_system_message_is_also_accepted()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Sent later", ThreeMinutesLater);

        Message note = conversation.AddSystemMessage("Imported note", OneMinuteLater);

        Assert.Equal(OneMinuteLater, note.SentAt);
        Assert.Equal(ThreeMinutesLater, conversation.LastActivityAt);
    }

    [Fact]
    public void Closing_with_an_earlier_instant_does_not_move_the_last_activity_backwards()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Hello", ThreeMinutesLater);

        conversation.Close(OneMinuteLater);

        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        Assert.Equal(ThreeMinutesLater, conversation.LastActivityAt);
    }

    [Fact]
    public void A_rejected_instant_leaves_the_conversation_unchanged()
    {
        Conversation conversation = StartConversation();

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.Customer, "Hello", default));

        Assert.Empty(conversation.Messages);
        Assert.Equal(Start, conversation.LastActivityAt);
    }

    // --------------------------------------------------------------- lifecycle

    [Fact]
    public void Close_closes_an_open_conversation()
    {
        Conversation conversation = StartConversation();

        conversation.Close(OneMinuteLater);

        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        Assert.Equal(OneMinuteLater, conversation.LastActivityAt);
    }

    [Fact]
    public void Reopen_reopens_a_closed_conversation()
    {
        Conversation conversation = StartConversation();
        conversation.Close(OneMinuteLater);

        conversation.Reopen(TwoMinutesLater);

        Assert.Equal(ConversationStatus.Open, conversation.Status);
        Assert.Equal(TwoMinutesLater, conversation.LastActivityAt);
    }

    [Fact]
    public void Closing_an_already_closed_conversation_is_a_no_op()
    {
        Conversation conversation = StartConversation();
        conversation.Close(OneMinuteLater);

        conversation.Close(TwoMinutesLater);

        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        // Nothing happened, so nothing is recorded as activity.
        Assert.Equal(OneMinuteLater, conversation.LastActivityAt);
    }

    [Fact]
    public void Reopening_an_already_open_conversation_is_a_no_op()
    {
        Conversation conversation = StartConversation();

        conversation.Reopen(OneMinuteLater);

        Assert.Equal(ConversationStatus.Open, conversation.Status);
        Assert.Equal(Start, conversation.LastActivityAt);
    }

    [Fact]
    public void A_conversation_can_be_closed_and_reopened_repeatedly()
    {
        Conversation conversation = StartConversation();

        conversation.Close(OneMinuteLater);
        conversation.Reopen(TwoMinutesLater);
        conversation.Close(ThreeMinutesLater);

        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        Assert.Equal(ThreeMinutesLater, conversation.LastActivityAt);
    }

    [Fact]
    public void Lifecycle_changes_leave_identity_tenant_and_channel_untouched()
    {
        TenantId tenantId = TenantId.New();
        Conversation conversation = Conversation.Start(tenantId, CustomerId.New(), ChannelKind.Messenger, Start);
        ConversationId originalId = conversation.Id;

        conversation.Close(OneMinuteLater);
        conversation.Reopen(TwoMinutesLater);

        Assert.Equal(originalId, conversation.Id);
        Assert.Equal(tenantId, conversation.TenantId);
        Assert.Equal(ChannelKind.Messenger, conversation.Channel);
        Assert.Equal(Start, conversation.StartedAt);
    }

    // ---------------------------------------------------------------- messages

    [Theory]
    [InlineData(MessageSender.Customer)]
    [InlineData(MessageSender.AiAgent)]
    [InlineData(MessageSender.HumanAgent)]
    public void A_text_message_records_its_sender_content_and_instant(MessageSender sender)
    {
        Conversation conversation = StartConversation();

        Message message = conversation.AddTextMessage(sender, "Hello there", OneMinuteLater);

        Assert.Equal(sender, message.Sender);
        Assert.Equal(MessageContentType.Text, message.ContentType);
        Assert.Equal("Hello there", message.Text);
        Assert.Equal(OneMinuteLater, message.SentAt);
    }

    [Fact]
    public void A_system_message_is_recorded_as_system_sender_and_system_content()
    {
        Conversation conversation = StartConversation();

        Message message = conversation.AddSystemMessage("Conversation transferred.", OneMinuteLater);

        Assert.Equal(MessageSender.System, message.Sender);
        Assert.Equal(MessageContentType.System, message.ContentType);
        Assert.Equal("Conversation transferred.", message.Text);
    }

    [Fact]
    public void A_text_message_may_not_claim_the_system_sender()
    {
        Conversation conversation = StartConversation();

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.System, "Hello", OneMinuteLater));
    }

    [Fact]
    public void A_text_message_rejects_an_unrecognised_sender()
    {
        Conversation conversation = StartConversation();

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage((MessageSender)99, "Hello", OneMinuteLater));
    }

    [Fact]
    public void Every_message_belongs_to_the_conversation_that_created_it()
    {
        Conversation conversation = StartConversation();

        Message message = conversation.AddTextMessage(MessageSender.Customer, "Hello", OneMinuteLater);

        Assert.Equal(conversation.Id, message.ConversationId);
    }

    [Fact]
    public void Each_message_receives_a_distinct_non_empty_identity()
    {
        Conversation conversation = StartConversation();

        Message first = conversation.AddTextMessage(MessageSender.Customer, "One", OneMinuteLater);
        Message second = conversation.AddTextMessage(MessageSender.AiAgent, "Two", TwoMinutesLater);

        Assert.False(first.Id.IsEmpty);
        Assert.False(second.Id.IsEmpty);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Messages_are_kept_in_insertion_order()
    {
        Conversation conversation = StartConversation();

        conversation.AddTextMessage(MessageSender.Customer, "First", OneMinuteLater);
        conversation.AddTextMessage(MessageSender.AiAgent, "Second", TwoMinutesLater);
        conversation.AddSystemMessage("Third", ThreeMinutesLater);

        Assert.Equal(
            ["First", "Second", "Third"],
            conversation.Messages.Select(message => message.Text));
    }

    [Fact]
    public void Adding_a_message_advances_the_last_activity()
    {
        Conversation conversation = StartConversation();

        conversation.AddTextMessage(MessageSender.Customer, "Hello", OneMinuteLater);

        Assert.Equal(OneMinuteLater, conversation.LastActivityAt);
    }

    [Fact]
    public void An_existing_message_is_unaffected_by_later_messages()
    {
        Conversation conversation = StartConversation();
        Message first = conversation.AddTextMessage(MessageSender.Customer, "First", OneMinuteLater);

        conversation.AddTextMessage(MessageSender.AiAgent, "Second", TwoMinutesLater);

        Assert.Equal("First", first.Text);
        Assert.Equal(OneMinuteLater, first.SentAt);
        Assert.Same(first, conversation.Messages[0]);
    }

    // ------------------------------------------------------- message text rules

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Message_text_is_required(string? text)
    {
        Conversation conversation = StartConversation();

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.Customer, text, OneMinuteLater));
    }

    [Fact]
    public void System_message_text_is_required()
    {
        Conversation conversation = StartConversation();

        Assert.Throws<DomainValidationException>(
            () => conversation.AddSystemMessage("   ", OneMinuteLater));
    }

    [Fact]
    public void Message_text_is_trimmed_at_the_edges()
    {
        Conversation conversation = StartConversation();

        Message message = conversation.AddTextMessage(MessageSender.Customer, "  Hello  ", OneMinuteLater);

        Assert.Equal("Hello", message.Text);
    }

    [Fact]
    public void Message_text_preserves_interior_whitespace_and_line_breaks()
    {
        Conversation conversation = StartConversation();
        const string Written = "Line one\nLine two\n\n\tIndented";

        Message message = conversation.AddTextMessage(MessageSender.Customer, Written, OneMinuteLater);

        Assert.Equal(Written, message.Text);
    }

    [Fact]
    public void Message_text_accepts_the_maximum_length()
    {
        Conversation conversation = StartConversation();
        string text = new('a', Message.MaxTextLength);

        Message message = conversation.AddTextMessage(MessageSender.Customer, text, OneMinuteLater);

        Assert.Equal(Message.MaxTextLength, message.Text.Length);
    }

    [Fact]
    public void Message_text_rejects_one_character_beyond_the_maximum()
    {
        Conversation conversation = StartConversation();
        string text = new('a', Message.MaxTextLength + 1);

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.Customer, text, OneMinuteLater));
    }

    [Fact]
    public void Message_text_is_never_silently_truncated()
    {
        Conversation conversation = StartConversation();
        string text = new('a', Message.MaxTextLength + 1);

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.Customer, text, OneMinuteLater));

        Assert.Empty(conversation.Messages);
    }

    [Fact]
    public void Message_text_length_is_measured_after_trimming()
    {
        Conversation conversation = StartConversation();
        string text = "  " + new string('a', Message.MaxTextLength) + "  ";

        Message message = conversation.AddTextMessage(MessageSender.Customer, text, OneMinuteLater);

        Assert.Equal(Message.MaxTextLength, message.Text.Length);
    }

    [Fact]
    public void Message_text_rejects_control_characters_other_than_tab_and_newline()
    {
        Conversation conversation = StartConversation();

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.Customer, "Hello\u0007there", OneMinuteLater));
    }

    [Fact]
    public void A_rejected_message_leaves_the_conversation_unchanged()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Kept", OneMinuteLater);

        Assert.Throws<DomainValidationException>(
            () => conversation.AddTextMessage(MessageSender.Customer, "   ", TwoMinutesLater));

        Assert.Single(conversation.Messages);
        Assert.Equal(OneMinuteLater, conversation.LastActivityAt);
    }

    // ------------------------------------------------- closed-state enforcement

    [Fact]
    public void A_closed_conversation_rejects_a_text_message()
    {
        Conversation conversation = StartConversation();
        conversation.Close(OneMinuteLater);

        Assert.Throws<DomainStateException>(
            () => conversation.AddTextMessage(MessageSender.Customer, "Hello", TwoMinutesLater));
    }

    [Fact]
    public void A_closed_conversation_rejects_a_system_message()
    {
        // One rule, no exceptions: a closed conversation accepts nothing at all.
        Conversation conversation = StartConversation();
        conversation.Close(OneMinuteLater);

        Assert.Throws<DomainStateException>(
            () => conversation.AddSystemMessage("Note", TwoMinutesLater));
    }

    [Fact]
    public void A_rejected_message_on_a_closed_conversation_changes_nothing()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Kept", OneMinuteLater);
        conversation.Close(TwoMinutesLater);

        Assert.Throws<DomainStateException>(
            () => conversation.AddTextMessage(MessageSender.Customer, "Blocked", ThreeMinutesLater));

        Assert.Single(conversation.Messages);
        Assert.Equal(TwoMinutesLater, conversation.LastActivityAt);
    }

    [Fact]
    public void A_reopened_conversation_accepts_messages_again()
    {
        Conversation conversation = StartConversation();
        conversation.Close(OneMinuteLater);
        conversation.Reopen(TwoMinutesLater);

        Message message = conversation.AddTextMessage(MessageSender.Customer, "Back", ThreeMinutesLater);

        Assert.Single(conversation.Messages);
        Assert.Equal("Back", message.Text);
    }

    // ------------------------------------------------- history encapsulation

    [Fact]
    public void Message_history_is_not_handed_out_as_the_internal_list()
    {
        Conversation conversation = StartConversation();

        Assert.IsNotAssignableFrom<List<Message>>(conversation.Messages);
    }

    [Fact]
    public void Message_history_rejects_mutation_through_a_mutable_interface()
    {
        Conversation conversation = StartConversation();
        conversation.AddTextMessage(MessageSender.Customer, "Hello", OneMinuteLater);

        ICollection<Message> view = (ICollection<Message>)conversation.Messages;

        Assert.True(view.IsReadOnly);
        Assert.Throws<NotSupportedException>(view.Clear);
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public void Message_history_view_reflects_later_additions()
    {
        Conversation conversation = StartConversation();
        IReadOnlyList<Message> view = conversation.Messages;

        conversation.AddTextMessage(MessageSender.Customer, "Hello", OneMinuteLater);

        Assert.Single(view);
    }

    // ------------------------------------------------------- structural guards

    [Fact]
    public void Tenant_ownership_has_no_setter_at_all()
    {
        // The strongest form of "a conversation cannot be moved between tenants":
        // there is no setter to call, public or private.
        Assert.Null(typeof(Conversation).GetProperty(nameof(Conversation.TenantId))!.SetMethod);
    }

    [Fact]
    public void Conversation_identity_start_and_channel_have_no_setters()
    {
        Assert.Null(typeof(Conversation).GetProperty(nameof(Conversation.Id))!.SetMethod);
        Assert.Null(typeof(Conversation).GetProperty(nameof(Conversation.Channel))!.SetMethod);
        Assert.Null(typeof(Conversation).GetProperty(nameof(Conversation.StartedAt))!.SetMethod);
    }

    [Fact]
    public void Conversation_exposes_no_public_setters()
    {
        Assert.DoesNotContain(
            typeof(Conversation).GetProperties(),
            property => property.SetMethod is { IsPublic: true });
    }
}
