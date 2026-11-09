import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WebChatAdminApi } from '@app/features/channels/website-chat-api';
import { AgentChannels, channelStatusNote, visibleChannels } from './agent-channels';
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

describe('visibleChannels', () => {
  it('links each channel to the page that configures it', () => {
    expect(
      visibleChannels([
        { kind: 'WebChat', id: 'w1', status: 'Active' },
        { kind: 'WhatsApp', id: 'p1', status: 'Draft' },
      ]).map((use) => use.link),
    ).toEqual([
      ['/channels/website-chat', 'w1'],
      ['/channels/whatsapp', 'p1'],
    ]);
  });

  it('leaves out a kind this build has no page for', () => {
    expect(visibleChannels([{ kind: 'Telegram', id: 't1', status: 'Active' } as never])).toEqual(
      [],
    );
  });
});

/** The one channel read left: the widget's look, for the preview. */
describe('AgentChannels', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const web = (id: string, status: string) => ({
    channel: 'WebChat' as const,
    id,
    status,
    link: ['/channels/website-chat', id],
  });

  let granted: Set<string>;
  let http: HttpTestingController;
  let channels: AgentChannels;

  beforeEach(() => {
    granted = new Set(['website-chat.view']);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        WebChatAdminApi,
        AgentChannels,
        { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    channels = TestBed.inject(AgentChannels);
  });

  afterEach(() => http.verify());

  it("reads the active website chat's look first", async () => {
    const pending = channels.widgetFor(tenantId, [web('w1', 'Draft'), web('w2', 'Active')]);

    await Promise.resolve();
    http
      .expectOne(`/api/tenants/${tenantId}/website-chat/w2/widget`)
      .flush({ widget: { accentColor: '#112233' } }, { headers: { ETag: '"v1"' } });

    expect((await pending)?.accentColor).toBe('#112233');
  });

  it('reads nothing for an agent on no website chat', async () => {
    expect(await channels.widgetFor(tenantId, [])).toBeNull();
  });

  it('reads nothing for somebody who may not see website chat', async () => {
    granted.clear();

    expect(await channels.widgetFor(tenantId, [web('w1', 'Active')])).toBeNull();
  });

  it('answers null when the look cannot be read', async () => {
    const pending = channels.widgetFor(tenantId, [web('w1', 'Active')]);

    await Promise.resolve();
    http
      .expectOne(`/api/tenants/${tenantId}/website-chat/w1/widget`)
      .flush(null, { status: 500, statusText: 'Error' });

    expect(await pending).toBeNull();
  });
});
