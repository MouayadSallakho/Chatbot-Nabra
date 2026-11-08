import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { ConversationCustomer } from './conversations-api';

/** A customer as the inbox names them: a name somebody gave, or a label. */
export type CustomerLabelText =
  | { readonly kind: 'name'; readonly name: string }
  | { readonly kind: 'label'; readonly word: string; readonly code: string | null };

/**
 * What the inbox calls a customer.
 *
 * <strong>The server decides, by `labelCode`.</strong> The client no longer works
 * the rule out from channels here; the customers screens still do, until their
 * reads carry the same fields (BA-1). A name somebody chose is shown exactly as
 * typed. Without one, a word and something to tell customers apart: the short code,
 * or the masked number on WhatsApp. A code this build does not know reads as a plain
 * customer, and the short code is only ever shown, never used as a key.
 */
export function customerLabel(customer: ConversationCustomer | null): CustomerLabelText {
  if (customer === null) {
    return { kind: 'label', word: unnamedWord(), code: null };
  }

  if (customer.displayName !== null && customer.displayName.trim() !== '') {
    return { kind: 'name', name: customer.displayName };
  }

  switch (customer.labelCode) {
    case 'AnonymousVisitor':
      return {
        kind: 'label',
        word: $localize`:@@inbox.customer.visitor:Visitor`,
        code: customer.shortCode,
      };
    case 'WhatsAppCustomer':
      return {
        kind: 'label',
        word: $localize`:@@inbox.customer.whatsApp:WhatsApp customer`,
        code: customer.reference ?? customer.shortCode,
      };
    default:
      return { kind: 'label', word: unnamedWord(), code: customer.shortCode };
  }
}

function unnamedWord(): string {
  return $localize`:@@inbox.customer.unnamed:Customer`;
}

/**
 * The customer's name or label, isolated for direction.
 *
 * A name takes its own direction. A short code or a masked number always reads left
 * to right, inside either language, and is never machine translated.
 */
@Component({
  selector: 'app-customer-label',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let current = label();
    @if (current.kind === 'name') {
      <bdi dir="auto">{{ current.name }}</bdi>
    } @else {
      <span>{{ current.word }}</span>
      @if (current.code; as code) {
        &ngsp;<bdi dir="ltr" translate="no">{{ code }}</bdi>
      }
    }
  `,
})
export class CustomerLabel {
  readonly customer = input.required<ConversationCustomer | null>();

  protected readonly label = computed(() => customerLabel(this.customer()));
}
