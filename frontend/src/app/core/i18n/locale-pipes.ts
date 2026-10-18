import { Pipe, PipeTransform, inject } from '@angular/core';
import { DateStyle, LocaleFormat } from './locale-format';

/**
 * `{{ value | localDate: 'dateTime' }}`: a date in this page's language, with the
 * product's digit and calendar policy. Replaces Angular's `date` pipe, whose
 * patterns ('MMM d, y') are English word order.
 *
 * Pure: the language cannot change while the page runs.
 */
@Pipe({ name: 'localDate' })
export class LocalDatePipe implements PipeTransform {
  private readonly format = inject(LocaleFormat);

  transform(
    value: string | number | Date | null | undefined,
    style: DateStyle = 'date',
    timeZone?: string,
  ): string {
    return value === null || value === undefined || value === ''
      ? ''
      : this.format.date(value, style, timeZone);
  }
}

/** `{{ count | localNumber }}`: grouped, Western digits, in both languages. */
@Pipe({ name: 'localNumber' })
export class LocalNumberPipe implements PipeTransform {
  private readonly format = inject(LocaleFormat);

  transform(value: number | null | undefined, maximumFractionDigits?: number): string {
    if (value === null || value === undefined) {
      return '';
    }

    return this.format.number(
      value,
      maximumFractionDigits === undefined ? undefined : { maximumFractionDigits },
    );
  }
}
