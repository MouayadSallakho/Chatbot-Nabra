import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface SimulatorAgent {
  readonly id: string;
  readonly name: string;
  readonly customerId: string;
}
export interface MailboxMessage {
  readonly id: string;
  readonly sender: 'Customer' | 'AiAgent' | 'HumanAgent';
  readonly text: string;
  readonly sentAt: string;
}
export type DeliveryScenario = 'Accept' | 'Refuse' | 'LoseConfirmation';
export interface SimulatorDeliveryIssue {
  readonly id: string;
  readonly sender: 'AiAgent' | 'HumanAgent';
  readonly text: string;
  readonly status: 'Refused' | 'Unconfirmed';
}
export interface SimulatorOwnership {
  readonly externalOwner: boolean;
  readonly version: number;
}
export interface SimulatorMailbox {
  readonly lastHandoverTimestamp?: number;
  readonly ownership?: SimulatorOwnership | null;
  readonly deliveryIssues: readonly SimulatorDeliveryIssue[];
  readonly conversationId: string | null;
  readonly messages: readonly MailboxMessage[];
}

@Injectable({ providedIn: 'root' })
export class MessengerSimulatorApi {
  private readonly http = inject(HttpClient);
  private url(tenant: string) {
    return `/api/tenants/${tenant}/messenger-simulator`;
  }
  agents(tenant: string) {
    return this.http.get<readonly SimulatorAgent[]>(this.url(tenant));
  }
  statuses(tenant: string, conversation: string) {
    return this.http.get<Readonly<Record<string, string>>>(
      this.url(tenant) + '/conversations/' + conversation + '/deliveries',
    );
  }
  recover(tenant: string, agent: string, id: string) {
    return this.http.post<SimulatorMailbox>(
      this.url(tenant) + '/' + agent + '/deliveries/' + id + '/recover',
      {},
    );
  }
  receive(tenant: string, agent: string, scenario: DeliveryScenario) {
    return this.http.post<SimulatorMailbox>(
      this.url(tenant) + '/' + agent + '/receive',
      {},
      { params: { scenario } },
    );
  }
  send(
    tenant: string,
    agent: SimulatorAgent,
    eventId: string,
    text: string,
    scenario: DeliveryScenario,
  ) {
    const pageId = agent.id.replaceAll('-', '');
    return this.http.post<SimulatorMailbox>(
      this.url(tenant) + '/' + agent.id + '/page-events',
      {
        object: 'page',
        entry: [
          {
            id: pageId,
            messaging: [
              {
                sender: { id: agent.customerId },
                recipient: { id: pageId },
                message: { mid: eventId.replaceAll('-', ''), text },
              },
            ],
          },
        ],
      },
      { params: { scenario } },
    );
  }
}
