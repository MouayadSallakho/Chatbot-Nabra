import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  afterRenderEffect,
  inject,
  signal,
} from '@angular/core';
import { ThemeService } from '@app/core/theme/theme-service';
import { THEME_PREFERENCES, ThemePreference } from '@app/core/theme/theme';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import { ToastHost } from '@app/core/feedback/toast-host';
import { ToastService } from '@app/core/feedback/toast-service';
import type { MessageSender } from '@app/features/conversations/conversations-api';
import { BrandMark } from '@app/shared/brand-mark/brand-mark';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { ICON_PATHS, IconName } from '@app/shared/icon/icon-paths';
import { Menu } from '@app/shared/menu/menu';
import { Pagination } from '@app/shared/pagination/pagination';
import { Voice } from '@app/shared/voice/voice';
import { VoiceChip } from '@app/shared/voice/voice-chip';
import { VoiceSplit } from '@app/shared/voice/voice-split';
import { VoiceTrack } from '@app/shared/voice/voice-track';

/** A named group of colour tokens, drawn as swatches with their live values. */
interface TokenGroup {
  readonly title: string;
  readonly tokens: readonly string[];
}

/** One line of the sample transcript. */
interface SampleMessage {
  readonly id: number;
  readonly sender: MessageSender;
  readonly speaker: string;
  readonly text: string;
}

const COLOUR_GROUPS: readonly TokenGroup[] = [
  {
    title: 'Surfaces',
    tokens: [
      '--surface-page',
      '--surface-default',
      '--surface-muted',
      '--surface-raised',
      '--surface-inset',
      '--surface-hover',
      '--surface-selected',
    ],
  },
  { title: 'Text', tokens: ['--text-primary', '--text-secondary', '--text-muted'] },
  {
    title: 'Borders',
    tokens: ['--border-subtle', '--border-default', '--border-strong', '--border-control'],
  },
  {
    title: 'Action (ink)',
    tokens: [
      '--action',
      '--action-hover',
      '--action-pressed',
      '--on-action',
      '--link',
      '--focus-ring',
      '--selection',
    ],
  },
  {
    title: 'Voice: the AI (Lagoon)',
    tokens: ['--voice-ai-fg', '--voice-ai-strong', '--voice-ai-tint', '--voice-ai-border'],
  },
  {
    title: 'Voice: a person (Ember)',
    tokens: [
      '--voice-human-fg',
      '--voice-human-strong',
      '--voice-human-tint',
      '--voice-human-border',
    ],
  },
  {
    title: 'Status',
    tokens: [
      '--success-fg',
      '--success-bg',
      '--warning-fg',
      '--warning-bg',
      '--danger-fg',
      '--danger-bg',
      '--danger-solid',
      '--info-fg',
      '--info-bg',
    ],
  },
  {
    title: 'Charts',
    tokens: [
      '--chart-ai',
      '--chart-human',
      '--chart-customer',
      '--chart-neutral',
      '--chart-ramp-1',
      '--chart-ramp-2',
      '--chart-ramp-3',
      '--chart-ramp-4',
      '--chart-ramp-5',
    ],
  },
];

const SPACE_TOKENS = Array.from({ length: 10 }, (_, i) => `--space-${i + 1}`);

const RADIUS_TOKENS = [
  '--radius-badge',
  '--radius-control',
  '--radius-card',
  '--radius-overlay',
  '--radius-bubble',
] as const;

const MOTION_TOKENS = [
  '--duration-xs',
  '--duration-sm',
  '--duration-md',
  '--duration-lg',
  '--duration-xl',
  '--ease-standard',
  '--ease-enter',
  '--ease-exit',
  '--ease-emphasized',
] as const;

/** Type roles, each with a Latin and an Arabic sample. */
const TYPE_ROLES = [
  {
    role: 'Display',
    className: 'text-display',
    latin: '1,284',
    arabic: '1,284',
  },
  {
    role: 'Page title',
    className: 'text-page-title',
    latin: 'Conversations',
    arabic: 'المحادثات',
  },
  {
    role: 'Section title',
    className: 'text-section-title',
    latin: 'Who is answering now',
    arabic: 'من يرد الآن',
  },
  {
    role: 'Card title',
    className: 'text-card-title',
    latin: 'Website assistant',
    arabic: 'مساعد الموقع',
  },
  {
    role: 'Body',
    className: 'text-body',
    latin:
      'The AI answers customers at once, and a person on the team takes the conversation over whenever it needs one.',
    arabic:
      'يرد الذكاء الاصطناعي على العملاء فورًا، ويتولى أحد أفراد الفريق المحادثة متى احتاجت إلى إنسان.',
  },
  {
    role: 'Reading',
    className: 'text-reading',
    latin: 'Thanks for waiting. I have checked your order and it left our warehouse this morning.',
    arabic: 'شكرًا لانتظارك. راجعت طلبك، وقد غادر مستودعنا صباح اليوم.',
  },
  {
    role: 'Small',
    className: 'text-small',
    latin: 'Last activity 5 minutes ago',
    arabic: 'آخر نشاط قبل 5 دقائق',
  },
  {
    role: 'Caption',
    className: 'text-caption',
    latin: 'Sales assistant · WhatsApp',
    arabic: 'مساعد المبيعات · واتساب',
  },
  {
    role: 'Numeric',
    className: 'text-numeric',
    latin: '12,480 · 3,912 · 87',
    arabic: '12,480 · 3,912 · 87',
  },
] as const;

/** Invented for this page. Never shown outside the Design Lab. */
const SAMPLE_TRANSCRIPT: readonly SampleMessage[] = [
  { id: 1, sender: 'Customer', speaker: 'Customer', text: 'Hi, where is my order #4821?' },
  {
    id: 2,
    sender: 'AiAgent',
    speaker: 'AI',
    text: 'It left our warehouse this morning and should arrive tomorrow.',
  },
  {
    id: 3,
    sender: 'Customer',
    speaker: 'Customer',
    text: 'أريد تغيير عنوان التوصيل، من فضلك.',
  },
  {
    id: 4,
    sender: 'AiAgent',
    speaker: 'AI',
    text: 'I cannot change an address myself. Let me bring in someone from the team.',
  },
  {
    id: 5,
    sender: 'HumanAgent',
    speaker: 'Sara (team)',
    text: 'Hello, this is Sara. I have updated the address; the courier has it now.',
  },
  { id: 6, sender: 'Customer', speaker: 'Customer', text: 'Perfect, thank you!' },
];

/** A transcript line with what the template draws for it, worked out once. */
interface SampleRow extends SampleMessage {
  readonly bubble: string;
  /** The voice a handoff divider before this line hands to, or null for none. */
  readonly handoffTo: Voice | null;
}

const isReply = (sender: MessageSender) => sender === 'AiAgent' || sender === 'HumanAgent';

/**
 * The sample transcript as rows. A divider goes before a reply whose author
 * differs from the previous reply's: the same rule the real transcript uses,
 * applied here to the sample messages only.
 */
const SAMPLE_ROWS: readonly SampleRow[] = SAMPLE_TRANSCRIPT.map((message, index) => {
  const previous = SAMPLE_TRANSCRIPT.slice(0, index)
    .reverse()
    .find((m) => isReply(m.sender));
  const changes = isReply(message.sender) && !!previous && previous.sender !== message.sender;

  return {
    ...message,
    bubble:
      message.sender === 'AiAgent'
        ? 'bubble-ai'
        : message.sender === 'HumanAgent'
          ? 'bubble-human'
          : 'bubble-customer',
    handoffTo: changes ? (message.sender === 'HumanAgent' ? 'human' : 'ai') : null,
  };
});

/**
 * The Design Lab: the whole "Two Voices" system on one page, for review.
 *
 * DEVELOPMENT ONLY. The route is registered only in development builds (see
 * app.routes.ts), and the production build does not contain this chunk at all.
 * It reads no API and writes nothing anywhere: every number, name and message
 * on it is sample content written in this file, and the page says so wherever
 * a sample could be mistaken for data.
 *
 * Hover, press and focus are left live rather than faked with classes, so what
 * a reviewer sees is exactly what the primitives do.
 */
@Component({
  selector: 'app-design-lab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BrandMark,
    ConfirmDialog,
    EmptyState,
    Icon,
    Menu,
    Pagination,
    ToastHost,
    VoiceChip,
    VoiceSplit,
    VoiceTrack,
  ],
  templateUrl: './design-lab.html',
  styleUrl: './design-lab.scss',
})
export class DesignLab {
  private readonly document = inject(DOCUMENT);
  private readonly toasts = inject(ToastService);
  protected readonly theme = inject(ThemeService);

  protected readonly themes = THEME_PREFERENCES;
  protected readonly colourGroups = COLOUR_GROUPS;
  protected readonly spaceTokens = SPACE_TOKENS;
  protected readonly radiusTokens = RADIUS_TOKENS;
  protected readonly motionTokens = MOTION_TOKENS;
  protected readonly typeRoles = TYPE_ROLES;
  protected readonly transcript = SAMPLE_TRANSCRIPT;
  protected readonly rows = SAMPLE_ROWS;
  protected readonly iconNames = Object.keys(ICON_PATHS) as IconName[];

  /** The direction the lab is shown in, applied to the lab's own root only. */
  protected readonly direction = signal<'ltr' | 'rtl'>('ltr');

  /** The voice of the "Take over" demonstration chip. */
  protected readonly demoVoice = signal<Voice>('ai');

  protected readonly samplePage = signal(2);

  protected readonly switchOn = signal(true);

  /** The live, resolved value of every token on the page, by name. */
  protected readonly values = signal<Readonly<Record<string, string>>>({});

  constructor() {
    // Re-read the values after any render in which the theme may have changed:
    // the swatches show what the browser actually resolved, not what this file
    // believes the palette to be.
    afterRenderEffect({
      read: () => {
        this.theme.resolved();
        this.direction();
        const style = getComputedStyle(this.document.documentElement);
        const names = [
          ...COLOUR_GROUPS.flatMap((group) => group.tokens),
          ...SPACE_TOKENS,
          ...RADIUS_TOKENS,
          ...MOTION_TOKENS,
        ];
        this.values.set(
          Object.fromEntries(names.map((name) => [name, style.getPropertyValue(name).trim()])),
        );
      },
    });
  }

  protected selectTheme(preference: ThemePreference): void {
    this.theme.select(preference);
  }

  protected toggleVoice(): void {
    this.demoVoice.update((voice) => (voice === 'ai' ? 'human' : 'ai'));
  }

  protected toastSuccess(): void {
    this.toasts.success('Sample: settings saved.');
  }

  protected toastError(): void {
    this.toasts.error('Sample: the change could not be saved. Nothing was lost; try again.');
  }
}
