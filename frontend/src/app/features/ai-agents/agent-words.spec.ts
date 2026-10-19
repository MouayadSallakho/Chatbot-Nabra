import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WebChatAdminApi } from '@app/features/channels/website-chat-api';
import { WhatsAppApi } from '@app/features/channels/whatsapp-api';
import { AgentChannels, channelStatusNote } from './agent-channels';
import { agentStatusSentence, agentStatusTone, agentStatusWord } from './agent-words';

describe('agent words', () => {
  it('says each status, and shows an unknown one as sent', () => {
    expect(agentStatusWord('Draft')).toBe('Draft');
    expect(agentStatusWord('Active')).toBe('Active');
    expect(agentStatusWord('Disabled')).toBe('Disabled');
    expect(agentStatusWord('Archived')).toBe('Archived');
  });

  it('gives Draft and Disabled different markers, not only different words', () => {
    expect(agentStatusTone('Active')).toBe('badge-success');
    expect(agentStatusTone('Draft')).toContain('badge-draft');
    expect(agentStatusTone('Disabled')).not.toContain('badge-draft');
  });

  it('says what each status means for customers', () => {
    expect(agentStatusSentence('Draft')).toContain('until you activate it');
    expect(agentStatusSentence('Active')).toContain('In service');
  });

  it('notes a channel only when it is not on', () => {
    expect(channelStatusNote('Active')).toBeNull();
    expect(channelStatusNote('Draft')).toBe('Draft');
  });
});

/**
 * Which channels use each agent, joined from the two channel lists. Each list is
 * read only with its own permission, and a failed one marks the result partial.
 */
describe('AgentChannels', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  let granted: Set<string>;
  let http: HttpTestingController;
  let channels: AgentChannels;

  beforeEach(() => {
    granted = new Set(['website-chat.view', 'whatsapp.view']);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        WebChatAdminApi,
        WhatsAppApi,
        AgentChannels,
        { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    channels = TestBed.inject(AgentChannels);
  });

  afterEach(() => http.verify());

  async function flushLists(webChat: object[] | 'fail', whatsApp: object[]): Promise<void> {
    await Promise.resolve();
    const web = http.match(`/api/tenants/${tenantId}/website-chat`);
    const wa = http.match(`/api/tenants/${tenantId}/whatsapp/connections`);

    for (const request of web) {
      if (webChat === 'fail') {
        request.flush(null, { status: 500, statusText: 'Error' });
      } else {
        request.flush(webChat);
      }
    }

    for (const request of wa) {
      request.flush(whatsApp);
    }
  }

  it('groups both kinds of channel under the agent they name', async () => {
    const pending = channels.load(tenantId);

    await flushLists(
      [{ id: 'w1', aiAgentId: 'a1', status: 'Active' }],
      [{ id: 'p1', aiAgentId: 'a1', status: 'Draft' }],
    );

    const map = await pending;

    expect(map.visible).toBe(true);
    expect(map.incomplete).toBe(false);
    expect(map.byAgent.get('a1')?.map((use) => use.channel)).toEqual(['WebChat', 'WhatsApp']);
    expect(map.byAgent.get('a1')?.[1].link).toEqual(['/channels/whatsapp', 'p1']);
  });

  it('never reads a channel list the reader may not see', async () => {
    granted.delete('whatsapp.view');

    const pending = channels.load(tenantId);

    await flushLists([], []);
    await pending;

    http.expectNone(`/api/tenants/${tenantId}/whatsapp/connections`);
  });

  it('marks the result partial when a list fails, rather than "no channels"', async () => {
    const pending = channels.load(tenantId);

    await flushLists('fail', []);

    expect((await pending).incomplete).toBe(true);
  });
});
