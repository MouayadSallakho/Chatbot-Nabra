import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { TenantApi } from './tenant-api';

/** The versioned read and the conditional rename (Phase 38.4, D1). */
describe('TenantApi', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const acme = { id: tenantId, name: 'Acme Trading', slug: 'acme', status: 'Active' as const };

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

  it('fails loudly when a read carries no version, rather than saving without one later', async () => {
    const read = firstValueFrom(api.getVersioned(tenantId));

    http.expectOne(`/api/tenants/${tenantId}`).flush(acme);

    await expect(read).rejects.toThrow('The response carried no ETag.');
  });
});
