import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface SimulatorAgent {
  readonly id: string;
  readonly name: string;
}
export interface MailboxMessage {
  readonly id: string;
  readonly sender: 'Customer' | 'AiAgent' | 'HumanAgent';
  readonly text: string;
  readonly sentAt: string;
}
export interface SimulatorMailbox {
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
  receive(tenant: string, agent: string) {
    return this.http.post<SimulatorMailbox>(this.url(tenant) + '/' + agent + '/receive', {});
  }
  send(tenant: string, agent: string, eventId: string, text: string) {
    return this.http.post<SimulatorMailbox>(this.url(tenant) + '/' + agent, { eventId, text });
  }
}
