import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toApiError } from '@app/core/api/api-error';
import { BusinessHours, type Hours } from './business-hours';

interface Settings {
  hours: Hours;
  rules: { channel: string; language: string; roleId: string }[];
  teams: { id: string; name: string }[];
}

@Component({
  selector: 'app-inbox-routing',
  imports: [BusinessHours],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-routing.html',
  styleUrl: './inbox-routing.scss',
})
export class InboxRouting {
  readonly tenant = input.required<string>();
  readonly zone = input.required<string>();
  protected hoursSaved(hours: Hours) {
    this.settings.update((s) => s && { ...s, hours });
  }
  private readonly http = inject(HttpClient);
  protected readonly settings = signal<Settings | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly channels = [
    { id: 'WebChat', name: $localize`:@@channel.webChat:Website chat` },
    { id: 'WhatsApp', name: $localize`:@@channel.whatsApp:WhatsApp` },
    { id: 'Instagram', name: $localize`:@@channel.instagram:Instagram` },
    { id: 'Messenger', name: $localize`:@@channel.messenger:Messenger` },
  ];
  protected readonly languages = [
    { id: 'ar', name: $localize`:@@routing.ar:Arabic` },
    { id: 'en', name: $localize`:@@routing.en:English` },
  ];

  constructor() {
    effect(() => {
      void this.load(this.tenant());
    });
  }

  private url(tenant: string) {
    return `/api/tenants/${tenant}/conversations/routing`;
  }

  protected async load(tenant: string): Promise<void> {
    this.settings.set(null);
    this.error.set('');
    this.notice.set('');
    this.busy.set(true);
    try {
      const result = await firstValueFrom(this.http.get<Settings>(this.url(tenant)));
      if (this.tenant() === tenant) this.settings.set(result);
    } catch (error) {
      if (this.tenant() === tenant) this.error.set(toApiError(error).message);
    } finally {
      if (this.tenant() === tenant) this.busy.set(false);
    }
  }

  protected target(channel: string, language: string): string {
    return (
      this.settings()?.rules.find((r) => r.channel === channel && r.language === language)
        ?.roleId ?? ''
    );
  }

  protected async save(
    channel: string,
    language: string,
    control: HTMLSelectElement,
  ): Promise<void> {
    const tenant = this.tenant(),
      roleId = control.value,
      previous = this.target(channel, language);
    if (this.busy() || previous === roleId) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const url = this.url(tenant) + '/' + channel + '/' + language;
    try {
      await firstValueFrom(
        roleId ? this.http.put<void>(url, { roleId }) : this.http.delete<void>(url),
      );
      if (this.tenant() !== tenant) return;
      this.settings.update(
        (s) =>
          s && {
            ...s,
            rules: [
              ...s.rules.filter((r) => r.channel !== channel || r.language !== language),
              ...(roleId ? [{ channel, language, roleId }] : []),
            ],
          },
      );
      this.notice.set($localize`:@@routing.saved:Routing rule saved.`);
    } catch (error) {
      if (this.tenant() === tenant) {
        control.value = previous;
        this.error.set(toApiError(error).message);
      }
    } finally {
      if (this.tenant() === tenant) this.busy.set(false);
    }
  }
}
