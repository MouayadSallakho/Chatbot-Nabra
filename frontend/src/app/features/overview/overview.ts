import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NAV_ITEMS, NavItem } from '@app/layout/navigation';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';

/**
 * The landing page.
 *
 * EVERYTHING ON IT IS TRUE. There are no metrics, no counts, no revenue, no
 * resolution rates and no customers, because none of those exist: this
 * application has no API layer yet and is not permitted one in this phase. The
 * panels show empty states, and the table below them reports the one thing the
 * frontend genuinely knows -- which product areas are built and which are not,
 * read from the same navigation model the router and sidebar use.
 *
 * Inventing plausible-looking numbers here would make the shell demo well and
 * would be a lie told to everybody who reviews it, including the people
 * deciding what to build next.
 *
 * Its second job is to exercise the design system inside a real page: page
 * header, the responsive grid, cards, badges, empty states and the table
 * foundation, all in one view where they can be seen together.
 */
@Component({
  selector: 'app-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  /** Every product area, for the status table. */
  protected readonly areas = NAV_ITEMS;

  /**
   * The three areas given a panel of their own.
   *
   * The operational ones -- what the agents are, what they are handling, and how
   * they are reached. Knowledge, analytics and settings are configuration and
   * review rather than things a reader checks on arrival.
   */
  protected readonly panels: readonly NavItem[] = NAV_ITEMS.filter((item) =>
    ['ai-agents', 'conversations', 'channels'].includes(item.path),
  );
}
