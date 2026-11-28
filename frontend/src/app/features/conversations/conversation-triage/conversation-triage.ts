import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConversationDetail, TranscriptMessage, WorkspaceMember } from '../conversations-api';
import { ConversationNotes } from '../conversation-notes/conversation-notes';
import { memberName } from '../inbox-rows';

@Component({
  selector: 'app-conversation-triage',
  imports: [RouterLink, ConversationNotes],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conversation-triage.html',
  styleUrl: './conversation-triage.scss',
})
export class ConversationTriage {
  readonly conversation = input.required<ConversationDetail>();
  readonly messages = input.required<readonly TranscriptMessage[]>();
  readonly members = input.required<readonly WorkspaceMember[]>();
  readonly membersState = input.required<'loading' | 'ready' | 'failed'>();
  readonly canAssign = input.required<boolean>();
  readonly busy = input.required<boolean>();
  readonly stale = input.required<boolean>();
  readonly assign = output<string>();
  readonly reloadMembers = output<void>();

  protected readonly candidate = linkedSignal({
    source: () => this.conversation().id,
    computation: () => '',
  });
  protected readonly latestCustomer = computed(() =>
    [...this.messages()].reverse().find((message) => message.sender === 'Customer'),
  );
  protected readonly assignedName = computed(() => {
    const assignee = this.conversation().assignedTo;
    return assignee ? memberName(assignee).text : null;
  });
  protected readonly memberName = memberName;
}
