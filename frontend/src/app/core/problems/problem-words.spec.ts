import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideProblemWords, problemWording, registerProblemWords } from './problem-words';
import { passwordProblemLines, throttledMessage } from './core-problems';
import { CHECK_THIS_FIELD, refusedFields } from './field-problems';

const facts = { language: 'en' as const, codes: [] };

describe('the words for a refusal', () => {
  it('finds the account codes from the very start, before any feature has loaded', () => {
    expect(problemWording('auth.credentials.invalid', facts)).toBe(
      'The email address or password is incorrect.',
    );
  });

  it('has nothing for a code nobody registered', () => {
    expect(problemWording('spec.nobody.registered', facts)).toBeUndefined();
  });

  it('builds a sentence from the facts of the refusal, including the limit', () => {
    registerProblemWords({
      'spec.limit': ({ limit }) => `More than ${limit} pages.`,
    });

    expect(problemWording('spec.limit', { ...facts, limit: 500 })).toBe('More than 500 pages.');
  });

  it('takes an area catalogue from the providers of its routes', () => {
    TestBed.configureTestingModule({
      providers: [provideProblemWords({ 'spec.lazy': 'Words that arrived with the feature.' })],
    });
    TestBed.inject(ApplicationRef);

    expect(problemWording('spec.lazy', facts)).toBe('Words that arrived with the feature.');
  });
});

describe('throttledMessage', () => {
  it.each([
    [900, 'Too many attempts. Try again in 15 minutes.'],
    [61, 'Too many attempts. Try again in 2 minutes.'],
    [60, 'Too many attempts. Try again in 1 minute.'],
    [59, 'Too many attempts. Try again in 59 seconds.'],
    [1, 'Too many attempts. Try again in 1 second.'],
  ])('says when to try again after %i seconds', (seconds, words) => {
    expect(throttledMessage(seconds, 'en')).toBe(words);
  });

  it.each([undefined, 0, -3])('invents no number for a wait of %o', (seconds) => {
    expect(throttledMessage(seconds, 'en')).toBe('Too many attempts. Wait a moment and try again.');
  });

  it('picks the Arabic plural category for the count, in Western digits', () => {
    // No Arabic catalogue is loaded in the test run, so the English source of the
    // chosen category is what comes back; the choice is what is being proved.
    expect(throttledMessage(120, 'ar')).toContain('2');
    expect(throttledMessage(1500, 'ar')).toContain('25');
    expect(throttledMessage(1500, 'ar')).not.toMatch(/[٠-٩]/);
  });
});

describe('passwordProblemLines', () => {
  it('gives one line per broken rule, each once', () => {
    expect(
      passwordProblemLines({
        code: 'auth.password.too_short',
        codes: ['auth.password.too_short', 'auth.password.requires_digit'],
      }),
    ).toEqual(['Use at least 12 characters.', 'Add at least one digit.']);
  });

  it('says something honest about a rule this build has no sentence for', () => {
    expect(passwordProblemLines({ code: 'auth.password.something_new' })).toEqual([
      'This password cannot be used. Choose a different one.',
    ]);
  });

  it('is empty when the refusal is not about the password', () => {
    expect(passwordProblemLines({ code: 'auth.email.taken', codes: ['auth.email.taken'] })).toEqual(
      [],
    );
    expect(passwordProblemLines({})).toEqual([]);
  });
});

describe('refusedFields', () => {
  it('names the form fields the refusal listed, whatever the case', () => {
    expect([...refusedFields({ fields: ['Email'] }, ['email', 'password'])]).toEqual(['email']);
  });

  it('ignores a field the form does not have', () => {
    expect([...refusedFields({ fields: ['userId'] }, ['email'])]).toEqual([]);
  });

  it('is empty for a refusal that named no fields', () => {
    expect([...refusedFields({}, ['email'])]).toEqual([]);
  });

  it('has one shared sentence for a field with no rule of its own', () => {
    expect(CHECK_THIS_FIELD).toBe('Check this field.');
  });
});
