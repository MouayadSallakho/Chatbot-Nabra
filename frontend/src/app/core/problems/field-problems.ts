import type { ApiError } from '@app/core/api/api-error';

/**
 * Said beside a field a refusal named, when the form has no sentence of its own
 * for that field.
 */
export const CHECK_THIS_FIELD = $localize`:@@problem.field.check:Check this field.`;

/**
 * Which of a form's fields a 400 named in its `errors` map.
 *
 * Names only, compared without regard to case, because the map's messages are
 * the server's English and are never shown: the form marks these fields with its
 * own translated rule, or with `CHECK_THIS_FIELD`. A name the form does not have
 * is ignored, and the refusal's own message still says something went wrong.
 *
 * No endpoint answers with an `errors` map today (`problem-codes.md`); this is
 * what keeps a person told which field to fix once one does.
 */
export function refusedFields<Field extends string>(
  failure: Pick<ApiError, 'fields'>,
  formFields: readonly Field[],
): ReadonlySet<Field> {
  const named = new Set((failure.fields ?? []).map((name) => name.toLowerCase()));

  return new Set(formFields.filter((field) => named.has(field.toLowerCase())));
}
