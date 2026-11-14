import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { hostileStorage, memoryStorage } from '@app/testing/test-doubles';
import { TransparencyService } from './transparency-service';
import { TRANSPARENCY_ATTRIBUTE, TRANSPARENCY_STORAGE_KEY } from './theme';

/**
 * "Reduce transparency" is one stored boolean and one attribute. The cases that
 * matter: a stored choice is applied on start, a change is both painted and
 * kept, turning it off removes the attribute (the token layer keys on its
 * presence), and an unusable store degrades to glass rather than to an error.
 */
describe('TransparencyService', () => {
  function setup(storage: Storage = memoryStorage()) {
    TestBed.configureTestingModule({
      providers: [{ provide: LOCAL_STORAGE, useValue: storage }],
    });

    return { storage, service: TestBed.inject(TransparencyService) };
  }

  function painted(): string | null {
    TestBed.tick();

    return TestBed.inject(DOCUMENT).documentElement.getAttribute(TRANSPARENCY_ATTRIBUTE);
  }

  afterEach(() => {
    TestBed.inject(DOCUMENT).documentElement.removeAttribute(TRANSPARENCY_ATTRIBUTE);
  });

  it('keeps glass by default: no attribute, not reduced', () => {
    const { service } = setup();

    expect(service.reduced()).toBe(false);
    expect(painted()).toBeNull();
  });

  it('applies a stored choice on start', () => {
    const storage = memoryStorage();
    storage.setItem(TRANSPARENCY_STORAGE_KEY, 'reduced');

    const { service } = setup(storage);

    expect(service.reduced()).toBe(true);
    expect(painted()).toBe('reduced');
  });

  it('paints and keeps a new choice, and removes the attribute when turned off', () => {
    const { service, storage } = setup();

    service.setReduced(true);

    expect(painted()).toBe('reduced');
    expect(storage.getItem(TRANSPARENCY_STORAGE_KEY)).toBe('reduced');

    service.setReduced(false);

    expect(painted()).toBeNull();
    expect(storage.getItem(TRANSPARENCY_STORAGE_KEY)).toBe('full');
  });

  it('ignores a value it does not know, rather than trusting a hand-edited store', () => {
    const storage = memoryStorage();
    storage.setItem(TRANSPARENCY_STORAGE_KEY, 'yes please');

    expect(setup(storage).service.reduced()).toBe(false);
  });

  it('still works for this session when the store throws', () => {
    const { service } = setup(hostileStorage());

    expect(service.reduced()).toBe(false);

    service.setReduced(true);

    expect(painted()).toBe('reduced');
  });
});
