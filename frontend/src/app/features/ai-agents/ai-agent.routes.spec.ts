import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Provider } from '@angular/core';
import { KnowledgeApi } from '@app/features/knowledge/knowledge-api';
import { AiAgentApi } from './ai-agent-api';
import { aiAgentRoutes } from './ai-agent.routes';

/**
 * What the AI agent route makes available to the screens under it.
 *
 * <strong>This exists because a browser found what the component tests could
 * not.</strong> Every spec for the editor provides its dependencies explicitly,
 * so all of them passed while the real route provided only `AiAgentApi` — and
 * the editor, which now hosts the knowledge section, failed to render at all
 * with `NG0201: No provider found for KnowledgeApi`. A component spec cannot
 * catch that: the thing it gets wrong is the wiring the spec itself replaces.
 *
 * The check is deliberately made by building an injector from the route's own
 * provider list and resolving through it, rather than by comparing that list to
 * an expected array. The question worth asking is "can these screens get what
 * they inject", not "does this array have two entries in it".
 */
describe('aiAgentRoutes', () => {
  /** The providers the route actually declares. */
  function routeProviders(): readonly Provider[] {
    const providers = aiAgentRoutes[0].providers;

    expect(providers).toBeDefined();

    return providers as readonly Provider[];
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), ...routeProviders()],
    });
  });

  it('gives the agent screens their own agent client', () => {
    expect(TestBed.inject(AiAgentApi)).toBeInstanceOf(AiAgentApi);
  });

  it('gives them the knowledge client the editor knowledge section injects', () => {
    // The one that was missing. Without it the whole editor throws on render --
    // not a degraded panel, a blank page.
    expect(TestBed.inject(KnowledgeApi)).toBeInstanceOf(KnowledgeApi);
  });
});
