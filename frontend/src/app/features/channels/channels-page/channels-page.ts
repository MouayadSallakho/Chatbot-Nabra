import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PermissionService } from '@app/core/authorization/permission-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';

/**
 * The channels a workspace can be reached on.
 *
 * <strong>One is real and the rest say so.</strong> Website chat is built;
 * WhatsApp, Instagram and Messenger are not, and they are listed as not built
 * rather than shown as empty screens somebody could mistake for a configuration
 * they have not finished. An honest "not yet" is more useful than a page that
 * looks broken.
 */
@Component({
  selector: 'app-channels-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState],
  templateUrl: './channels-page.html',
  styleUrl: './channels-page.scss',
})
export class ChannelsPage {
  private readonly permissions = inject(PermissionService);

  protected readonly canView = computed(() => this.permissions.has('website-chat.view'));

  /** The channels, built and unbuilt alike. */
  protected readonly channels = [
    {
      name: 'Website chat',
      description: 'A chat page you can link to from your own site.',
      available: true,
      path: '/channels/website-chat',
    },
    {
      name: 'WhatsApp',
      description: 'Not built yet.',
      available: false,
      path: null,
    },
    {
      name: 'Instagram',
      description: 'Not built yet.',
      available: false,
      path: null,
    },
    {
      name: 'Messenger',
      description: 'Not built yet.',
      available: false,
      path: null,
    },
  ] as const;
}
