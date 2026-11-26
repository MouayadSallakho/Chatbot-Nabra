import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toApiError } from '@app/core/api/api-error';
import { LocaleFormat } from '@app/core/i18n/locale-format';

interface Shift {
  day: number;
  startMinute: number;
  endMinute: number;
}
export interface Hours {
  enabled: boolean;
  shifts: Shift[];
  afterHoursRoleId: string | null;
}

@Component({
  selector: 'app-business-hours',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './business-hours.html',
  styleUrl: './business-hours.scss',
})
export class BusinessHours {
  readonly tenant = input.required<string>();
  readonly zone = input.required<string>();
  readonly roles = input.required<{ id: string; name: string }[]>();
  readonly hours = input.required<Hours>();
  readonly saved = output<Hours>();
  protected readonly draft = linkedSignal(() => this.hours());
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly dirty = computed(() => this.draft() !== this.hours());
  protected readonly valid = computed(() =>
    this.draft().shifts.every(
      (s) => s.startMinute >= 0 && s.endMinute >= 0 && s.startMinute !== s.endMinute,
    ),
  );
  private readonly http = inject(HttpClient);
  private readonly format = inject(LocaleFormat);
  protected readonly days = [1, 2, 3, 4, 5, 6, 0].map((day) => ({
    day,
    name: new Intl.DateTimeFormat(this.format.language, {
      weekday: 'long',
      timeZone: 'UTC',
    }).format(Date.UTC(2024, 0, 7 + day)),
  }));

  protected discard() {
    this.draft.set(this.hours());
    this.error.set('');
    this.notice.set('');
  }
  protected edit(change: Partial<Hours>) {
    this.notice.set('');
    this.error.set('');
    this.draft.update((h) => ({ ...h, ...change }));
  }
  protected shift(day: number) {
    return this.draft().shifts.find((s) => s.day === day);
  }
  protected replace(day: number, shift?: Shift) {
    const remaining = this.draft().shifts.filter((s) => s.day !== day);
    this.edit({ shifts: shift ? [...remaining, shift] : remaining });
  }
  protected setMode(day: number, mode: string) {
    this.replace(
      day,
      mode === 'closed'
        ? undefined
        : { day, startMinute: mode === 'all' ? 0 : 540, endMinute: mode === 'all' ? 1440 : 1020 },
    );
  }
  protected time(minute: number) {
    if (minute < 0) return '';
    return [Math.floor(minute / 60) % 24, minute % 60]
      .map((n) => String(n).padStart(2, '0'))
      .join(':');
  }
  protected changeTime(s: Shift, key: 'startMinute' | 'endMinute', value: string) {
    const [h, m] = value.split(':').map(Number);
    this.replace(s.day, { ...s, [key]: value ? h * 60 + m : -1 });
  }
  protected async save() {
    if (!this.valid() || this.busy() || !this.dirty()) return;
    const tenant = this.tenant(),
      draft = this.draft();
    this.busy.set(true);
    try {
      await firstValueFrom(
        this.http.put<void>(`/api/tenants/${tenant}/conversations/routing/hours`, draft),
      );
      if (this.tenant() === tenant) {
        this.saved.emit(draft);
        this.notice.set($localize`:@@hours.saved:Business hours saved.`);
      }
    } catch (error) {
      if (this.tenant() === tenant) this.error.set(toApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
