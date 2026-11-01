import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '@app/core/auth/auth-service';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { APP_LANGUAGE, AppLanguage, LANGUAGE_STORAGE_KEY } from './language';
import { LANGUAGE_SAVE_WAIT_MS, LanguageService, PAGE_RELOAD } from './language-service';

/**
 * Choosing a language (Phase 37.3: the server keeps a copy for a signed-in
 * member's emails).
 *
 * What matters most is what must not happen: the copy on the server must never
 * stand between somebody and the language they asked for, however the request
 * ends.
 */
describe('LanguageService', () => {
  const url = '/api/me/language';

  let http: HttpTestingController;
  let storage: Storage;
  let reloads: number;

  function create(signedIn: boolean, current: AppLanguage = 'en'): LanguageService {
    reloads = 0;
    storage = memoryStorage();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
        { provide: PAGE_RELOAD, useValue: () => reloads++ },
        { provide: APP_LANGUAGE, useValue: current },
        { provide: AuthService, useValue: { isAuthenticated: () => signedIn } },
      ],
    });

    http = TestBed.inject(HttpTestingController);

    return TestBed.inject(LanguageService);
  }

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('switches at once before sign-in, with nothing sent to the server', async () => {
    const choosing = create(false).choose('ar');

    // Synchronous, as it always was: no account, nothing to wait for.
    expect(reloads).toBe(1);
    expect(storage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
    http.expectNone(url);

    await choosing;
  });

  it("records a signed-in member's choice on the server before restarting the page", async () => {
    const choosing = create(true).choose('ar');

    const request = http.expectOne(url);

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ language: 'ar' });
    // The browser's stored choice is written first, and is what the page uses.
    expect(storage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
    expect(reloads).toBe(0);

    request.flush(null, { status: 204, statusText: 'No Content' });
    await choosing;

    expect(reloads).toBe(1);
  });

  it('switches anyway, and quietly, when the server refuses', async () => {
    const choosing = create(true).choose('ar');

    http
      .expectOne(url)
      .flush(
        { code: 'language.unsupported', detail: 'That language is not supported.' },
        { status: 400, statusText: 'Bad Request' },
      );

    await expect(choosing).resolves.toBeUndefined();
    expect(reloads).toBe(1);
  });

  it('switches anyway when the server cannot be reached', async () => {
    const choosing = create(true).choose('ar');

    http.expectOne(url).error(new ProgressEvent('error'), { status: 0 });

    await expect(choosing).resolves.toBeUndefined();
    expect(reloads).toBe(1);
  });

  it('does not wait on a slow server for longer than it promises', async () => {
    vi.useFakeTimers();

    const choosing = create(true).choose('ar');
    const request = http.expectOne(url);

    await vi.advanceTimersByTimeAsync(LANGUAGE_SAVE_WAIT_MS - 1);
    expect(reloads).toBe(0);

    await vi.advanceTimersByTimeAsync(1);
    await choosing;

    expect(reloads).toBe(1);
    expect(request.cancelled).toBe(true);
  });

  it('records the choice without a reload when it is the language already in use', async () => {
    const choosing = create(true, 'ar').choose('ar');

    http.expectOne(url).flush(null, { status: 204, statusText: 'No Content' });
    await choosing;

    expect(reloads).toBe(0);
  });
});
