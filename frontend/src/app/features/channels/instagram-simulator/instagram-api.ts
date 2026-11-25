import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  DeliveryScenario,
  SimulatorAgent,
  SimulatorMailbox,
  SimulatorOwnership,
} from '../messenger-simulator/messenger-api';

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

  ownership(tenant: string, agent: string, requested: SimulatorOwnership) {
    return this.http.post<SimulatorMailbox>(
      this.url(tenant) + '/' + agent + '/ownership',
      requested,
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
