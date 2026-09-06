import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PermissionService } from './permission-service';

describe('PermissionService', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const url = `/api/me/tenants/${tenantId}/permissions`;

  let service: PermissionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(PermissionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('holds nothing before anything is loaded', () => {
    // `resolved` is what screens use to avoid flashing a read-only state at
    // somebody who does have permission: before the answer arrives, this
    // application does not know and must not guess in either direction.
    expect(service.resolved()).toBe(false);
    expect(service.has('roles.view')).toBe(false);
  });

  it('holds what the server returned', async () => {
    const loading = service.load(tenantId);

    http.expectOne(url).flush(['roles.view', 'ai-agents.edit']);
    await loading;

    expect(service.resolved()).toBe(true);
    expect(service.has('roles.view')).toBe(true);
    expect(service.has('ai-agents.edit')).toBe(true);
    expect(service.has('roles.manage')).toBe(false);
  });

  it('answers hasAny across several codes', async () => {
    const loading = service.load(tenantId);

    http.expectOne(url).flush(['ai-agents.preview']);
    await loading;

    expect(service.hasAny('ai-agents.view', 'ai-agents.preview')).toBe(true);
    expect(service.hasAny('roles.view', 'roles.manage')).toBe(false);
  });

  it('grants nothing when the request fails', async () => {
    // The only safe direction. Granting everything on a flaky connection would
    // briefly offer every action, and each one would come back refused.
    const loading = service.load(tenantId);

    http.expectOne(url).flush('nope', { status: 500, statusText: 'Server Error' });
    await loading;

    expect(service.resolved()).toBe(true);
    expect(service.all()).toEqual([]);
  });

  it('records an empty answer as a real one', async () => {
    // A member who has been given no roles is not the same as a request that
    // has not happened, and the admin has to be able to tell those apart to
    // explain why it is showing nothing.
    const loading = service.load(tenantId);

    http.expectOne(url).flush([]);
    await loading;

    expect(service.resolved()).toBe(true);
    expect(service.all()).toEqual([]);
  });

  it('replaces the held set when the workspace changes', async () => {
    const elsewhere = '22222222-2222-2222-2222-222222222222';

    const first = service.load(tenantId);
    http.expectOne(url).flush(['roles.manage']);
    await first;

    const second = service.load(elsewhere);
    http.expectOne(`/api/me/tenants/${elsewhere}/permissions`).flush(['ai-agents.view']);
    await second;

    // Authority is per workspace. Carrying the previous one's permissions over
    // would show somebody the buttons from the workspace they just left.
    expect(service.has('roles.manage')).toBe(false);
    expect(service.has('ai-agents.view')).toBe(true);
    expect(service.tenantId()).toBe(elsewhere);
  });

  it('forgets everything when cleared', async () => {
    const loading = service.load(tenantId);

    http.expectOne(url).flush(['roles.view']);
    await loading;

    service.clear();

    expect(service.resolved()).toBe(false);
    expect(service.has('roles.view')).toBe(false);
  });

  it('writes nothing to browser storage', async () => {
    // Permissions are not credentials, so there is nothing to protect -- but a
    // persisted copy would be a stale copy, and a client showing a button
    // because of what was true last week is worse than one that asks.
    const loading = service.load(tenantId);

    http.expectOne(url).flush(['roles.view']);
    await loading;

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
