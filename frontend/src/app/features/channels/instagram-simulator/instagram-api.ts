import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  DeliveryScenario,
  SimulatorAgent,
  SimulatorMailbox,
} from '../messenger-simulator/messenger-api';

// Mirrors the local Instagram messaging_handover fixture.
export function handoverNotice(agent: SimulatorAgent, external: boolean, timestamp: number) {
  const account = 'ig-account-' + agent.id.replaceAll('-', '');
  const transfer = {
    previous_owner_app_id: external ? '10001' : '10002',
    new_owner_app_id: external ? '10002' : '10001',
  };
  return {
    object: 'instagram',
    entry: [
      {
        id: account,
        changes: [
          {
            field: 'messaging_handover',
            value: {
              sender: { id: agent.customerId },
              recipient: { id: account },
              timestamp,
              ...(external ? { take_thread_control: transfer } : { pass_thread_control: transfer }),
            },
          },
        ],
      },
    ],
  };
}

export type HandoverNotice = ReturnType<typeof handoverNotice>;
export type HandoverOutcome = 'Applied' | 'Duplicate' | 'Stale' | 'Conflict';

@Injectable({ providedIn: 'root' })
export class InstagramSimulatorApi {
  private readonly http = inject(HttpClient);

  private url(tenant: string) {
    return `/api/tenants/${tenant}/instagram-simulator`;
  }

  agents(tenant: string) {
    return this.http.get<readonly SimulatorAgent[]>(this.url(tenant));
  }

  receive(tenant: string, agent: string, scenario: DeliveryScenario) {
    return this.http.post<SimulatorMailbox>(
      this.url(tenant) + '/' + agent + '/receive',
      {},
      { params: { scenario } },
    );
  }

  recover(tenant: string, agent: string, id: string) {
    return this.http.post<SimulatorMailbox>(
      this.url(tenant) + '/' + agent + '/deliveries/' + id + '/recover',
      {},
    );
  }

  notify(tenant: string, agent: string, notice: HandoverNotice) {
    return this.http.post<{ outcome: HandoverOutcome; mailbox: SimulatorMailbox }>(
      this.url(tenant) + '/' + agent + '/handover-events',
      notice,
    );
  }

  state(tenant: string, conversation: string) {
    return this.http.get<{ externalOwner: boolean; statuses: Readonly<Record<string, string>> }>(
      this.url(tenant) + '/conversations/' + conversation + '/state',
    );
  }

  send(
    tenant: string,
    agent: SimulatorAgent,
    eventId: string,
    text: string,
    scenario: DeliveryScenario,
  ) {
    const account = 'ig-account-' + agent.id.replaceAll('-', '');
    return this.http.post<SimulatorMailbox>(
      this.url(tenant) + '/' + agent.id + '/direct-events',
      {
        object: 'instagram',
        entry: [
          {
            id: account,
            messaging: [
              {
                sender: { id: agent.customerId },
                recipient: { id: account },
                message: { mid: eventId, text },
              },
            ],
          },
        ],
      },
      { params: { scenario } },
    );
  }
}
