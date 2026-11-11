import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { TenantApi } from './tenant-api';

/** The versioned read, the conditional rename (38.4, D1) and the time zone (38.5, E1). */
describe('TenantApi', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const acme = {
    id: tenantId,
    name: 'Acme Trading',
    slug: 'acme',
    status: 'Active' as const,
    timeZone: 'UTC',
  };

  let api: TenantApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    api = TestBed.inject(TenantApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the workspace with the version a rename must present', async () => {
    const read = firstValueFrom(api.getVersioned(tenantId));

    http.expectOne(`/api/tenants/${tenantId}`).flush(acme, { headers: { ETag: '"v1"' } });

    expect(await read).toEqual({ value: acme, etag: '"v1"' });
  });

  it('renames with If-Match and returns the new version', async () => {
    const renamed = firstValueFrom(api.rename(tenantId, 'Acme Holdings', '"v1"'));
    const request = http.expectOne(`/api/tenants/${tenantId}/name`);

    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"v1"');
    expect(request.request.body).toEqual({ name: 'Acme Holdings' });

    request.flush({ ...acme, name: 'Acme Holdings' }, { headers: { ETag: '"v2"' } });

    expect(await renamed).toEqual({ value: { ...acme, name: 'Acme Holdings' }, etag: '"v2"' });
  });

  it('sets the time zone with If-Match and returns the new version', async () => {
    const changed = firstValueFrom(api.changeTimeZone(tenantId, 'Asia/Riyadh', '"v2"'));
    const request = http.expectOne(`/api/tenants/${tenantId}/time-zone`);

    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"v2"');
    expect(request.request.body).toEqual({ timeZone: 'Asia/Riyadh' });

    request.flush({ ...acme, timeZone: 'Asia/Riyadh' }, { headers: { ETag: '"v3"' } });

    expect(await changed).toEqual({ value: { ...acme, timeZone: 'Asia/Riyadh' }, etag: '"v3"' });
  });

  it('fails loudly when a read carries no version, rather than saving without one later', async () => {
    const read = firstValueFrom(api.getVersioned(tenantId));

    http.expectOne(`/api/tenants/${tenantId}`).flush(acme);

    await expect(read).rejects.toThrow('The response carried no ETag.');
  });
});
