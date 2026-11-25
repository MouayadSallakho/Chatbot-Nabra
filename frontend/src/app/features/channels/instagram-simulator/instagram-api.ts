import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { SimulatorAgent, SimulatorMailbox } from '../messenger-simulator/messenger-api';

@Injectable({ providedIn: 'root' })
export class InstagramSimulatorApi {
  private readonly http = inject(HttpClient);

  private url(tenant: string) {
    return `/api/tenants/${tenant}/instagram-simulator`;
  }

  agents(tenant: string) {
    return this.http.get<readonly SimulatorAgent[]>(this.url(tenant));
  }

  receive(tenant: string, agent: string) {
    return this.http.get<SimulatorMailbox>(this.url(tenant) + '/' + agent + '/mailbox');
  }

  send(tenant: string, agent: SimulatorAgent, eventId: string, text: string) {
    const account = 'ig-account-' + agent.id.replaceAll('-', '');
    return this.http.post<SimulatorMailbox>(this.url(tenant) + '/' + agent.id + '/direct-events', {
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
    });
  }
}
