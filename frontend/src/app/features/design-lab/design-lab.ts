import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  afterRenderEffect,
  inject,
  signal,
} from '@angular/core';
import { ThemeService } from '@app/core/theme/theme-service';
import { TransparencyService } from '@app/core/theme/transparency-service';
import { THEME_LABELS, THEME_PREFERENCES, ThemePreference } from '@app/core/theme/theme';
import { APP_LANGUAGE, directionOf } from '@app/core/i18n/language';
import { LocalDatePipe } from '@app/core/i18n/locale-pipes';
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
import { AreaChart } from '@app/shared/charts/area-chart';
import { BarChart } from '@app/shared/charts/bar-chart';
import { ChartBar, ChartPart, ChartPoint, ChartSeries } from '@app/shared/charts/chart-model';
import { ColumnChart } from '@app/shared/charts/column-chart';
import { Meter } from '@app/shared/charts/meter';
import { Sparkline } from '@app/shared/charts/sparkline';
import { StackedBar } from '@app/shared/charts/stacked-bar';
import type { AnalyticsAiUsage, AnalyticsModelUsage } from '@app/features/analytics/analytics-api';
import { ProviderUsageCard } from '@app/features/analytics/provider-usage/provider-usage';
import type { WorkspaceSubscription } from '@app/features/billing/billing-api';
import { SubscriptionSummary } from '@app/features/billing/subscription-summary/subscription-summary';
import type { PlanEntitlement } from '@app/features/plan-usage/plan-usage-api';
import { QuotaRow } from '@app/features/plan-usage/quota-row/quota-row';

/** A named group of colour tokens, drawn as swatches with their live values. */
interface TokenGroup {
  readonly title: string;
  readonly tokens: readonly string[];
}

/** Seven sample days for the chart components (customers, team, AI, conversations). */
const SAMPLE_CHART_DAYS: readonly Readonly<Record<string, number>>[] = [
  { customer: 42, human: 9, ai: 35, conversations: 14 },
  { customer: 51, human: 12, ai: 40, conversations: 17 },
  { customer: 38, human: 6, ai: 33, conversations: 12 },
  { customer: 64, human: 21, ai: 44, conversations: 22 },
  { customer: 57, human: 15, ai: 43, conversations: 19 },
  { customer: 23, human: 4, ai: 19, conversations: 8 },
  { customer: 11, human: 2, ai: 9, conversations: 4 },
];

const SAMPLE_PLAN = $localize`:@@lab.admin.samplePlan:Growth (sample)`;
const SAMPLE_FALLBACK = $localize`:@@lab.admin.sampleFallback:Starter fallback (sample)`;

function sampleQuota(
  entitlement: PlanEntitlement['entitlement'],
  used: number,
  limit: number,
  state: PlanEntitlement['state'],
): PlanEntitlement {
  return {
    entitlement,
    used,
    limit,
    unlimited: false,
    percentUsed: Math.round((used / limit) * 100),
    state,
    remaining: Math.max(0, limit - used),
    period: null,
  };
}

function daysFromNow(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString();
}

function sampleSubscription(
  status: WorkspaceSubscription['status'],
  overrides: Partial<WorkspaceSubscription> = {},
): WorkspaceSubscription {
  return {
    status,
    interval: 'Monthly',
    planName: SAMPLE_PLAN,
    currentPeriodEnd: daysFromNow(21),
    trialEndsAt: null,
    cancelAtPeriodEnd: false,
    graceEndsAt: null,
    ...overrides,
  };
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
    title: $localize`:@@lab.colour.grounds:Ground and panels`,
    tokens: [
      '--color-ground',
      '--color-ground-visitor',
      '--color-panel',
      '--color-panel-soft',
      '--color-panel-raised',
      '--color-hover',
      '--color-selected',
    ],
  },
  {
    title: $localize`:@@lab.colour.text:Text`,
    tokens: ['--color-text', '--color-text-secondary', '--color-text-quiet'],
  },
  {
    title: $localize`:@@lab.colour.lines:Lines and control boundaries`,
    tokens: ['--color-line', '--color-line-strong', '--color-control-border'],
  },
  {
    title: $localize`:@@lab.colour.primary:Primary, links and focus (emerald)`,
    tokens: [
      '--color-primary',
      '--color-primary-hover',
      '--color-primary-pressed',
      '--color-on-primary',
      '--color-link',
      '--color-focus-ring',
      '--color-focus-halo',
      '--color-selection',
    ],
  },
  {
    title: $localize`:@@lab.colour.voice.ai:Voice: the AI (emerald)`,
    tokens: [
      '--voice-ai-bubble',
      '--voice-ai-text',
      '--voice-ai-label',
      '--voice-ai-dot',
      '--voice-ai-edge',
    ],
  },
  {
    title: $localize`:@@lab.colour.voice.team:Voice: the team (gold)`,
    tokens: [
      '--voice-team-bubble',
      '--voice-team-text',
      '--voice-team-label',
      '--voice-team-dot',
      '--voice-team-edge',
    ],
  },
  {
    title: $localize`:@@lab.colour.voice.customer:Voice: the customer (cream)`,
    tokens: ['--voice-customer-bubble', '--voice-customer-text', '--voice-customer-own-bubble'],
  },
  {
    title: $localize`:@@lab.colour.chips:Chips`,
    tokens: [
      '--chip-need-bg',
      '--chip-need-fg',
      '--chip-need-dot',
      '--chip-ai-bg',
      '--chip-ai-fg',
      '--chip-ai-dot',
      '--chip-failed-bg',
      '--chip-failed-fg',
      '--chip-failed-dot',
    ],
  },
  {
    title: $localize`:@@lab.colour.status:Status`,
    tokens: [
      '--status-success-fg',
      '--status-success-bg',
      '--status-warning-fg',
      '--status-warning-bg',
      '--status-danger-fg',
      '--status-danger-bg',
      '--status-danger-solid',
      '--status-info-fg',
      '--status-info-bg',
    ],
  },
  {
    title: $localize`:@@lab.colour.charts:Charts`,
    tokens: [
      '--chart-current',
      '--chart-previous',
      '--chart-ai',
      '--chart-team',
      '--chart-team-fill',
      '--chart-channel-2',
      '--chart-customer',
      '--chart-neutral',
      '--chart-ramp-1',
      '--chart-ramp-3',
      '--chart-ramp-5',
    ],
  },
  {
    title: $localize`:@@lab.colour.glass:Glass and depth`,
    tokens: [
      '--glass-bg',
      '--glass-solid-bg',
      '--glass-border',
      '--scrim',
      '--glow-a',
      '--glow-b',
      '--channel-whatsapp',
      '--channel-web',
    ],
  },
];

const SPACE_TOKENS = Array.from({ length: 10 }, (_, i) => `--space-${i + 1}`);

const RADIUS_TOKENS = [
  '--radius-input',
  '--radius-tile',
  '--radius-panel',
  '--radius-bubble',
  '--radius-pill',
] as const;

const MOTION_TOKENS = [
  '--duration-press',
  '--duration-menu',
  '--duration-dialog',
  '--duration-fade',
  '--ease-calm',
  '--motion-travel',
] as const;

/** Type roles, each with a Latin and an Arabic sample. */
const TYPE_ROLES = [
  {
    role: $localize`:@@lab.type.display:Display`,
    className: 'text-display',
    latin: 'Good morning, Layla',
    arabic: 'صباح الخير يا ليلى',
  },
  {
    role: $localize`:@@lab.type.figure:Figure`,
    className: 'text-figure',
    latin: '1,284',
    arabic: '1,284',
  },
  {
    role: $localize`:@@lab.type.pageTitle:Page title`,
    className: 'text-page-title',
    latin: 'Conversations',
    arabic: 'المحادثات',
  },
  {
    role: $localize`:@@lab.type.sectionTitle:Section title`,
    className: 'text-section-title',
    latin: 'Who is answering now',
    arabic: 'من يرد الآن',
  },
  {
    role: $localize`:@@lab.type.cardTitle:Card title`,
    className: 'text-card-title',
    latin: 'Website assistant',
    arabic: 'مساعد الموقع',
  },
  {
    role: $localize`:@@lab.type.body:Body`,
    className: 'text-body',
    latin:
      'The AI answers customers at once, and a person on the team takes the conversation over whenever it needs one.',
    arabic:
      'يرد الذكاء الاصطناعي على العملاء فورًا، ويتولى أحد أفراد الفريق المحادثة متى احتاجت إلى إنسان.',
  },
  {
    role: $localize`:@@lab.type.reading:Reading`,
    className: 'text-reading',
    latin: 'Thanks for waiting. I have checked your order and it left our warehouse this morning.',
    arabic: 'شكرًا لانتظارك. راجعت طلبك، وقد غادر مستودعنا صباح اليوم.',
  },
  {
    role: $localize`:@@lab.type.small:Small`,
    className: 'text-small',
    latin: 'Last activity 5 minutes ago',
    arabic: 'آخر نشاط قبل 5 دقائق',
  },
  {
    role: $localize`:@@lab.type.caption:Caption`,
    className: 'text-caption',
    latin: 'Sales assistant · WhatsApp',
    arabic: 'مساعد المبيعات · واتساب',
  },
  {
    role: $localize`:@@lab.type.numeric:Numeric`,
    className: 'text-numeric',
    latin: '12,480 · 3,912 · 87',
    arabic: '12,480 · 3,912 · 87',
  },
] as const;

/** Invented for this page. Never shown outside the Design Lab. */
const SAMPLE_TRANSCRIPT: readonly SampleMessage[] = [
  {
    id: 1,
    sender: 'Customer',
    speaker: $localize`:@@lab.transcript.customer:Customer`,
    text: 'Hi, where is my order #4821?', // token-lint-ignore: an order number, not a colour
  },
  {
    id: 2,
    sender: 'AiAgent',
    speaker: $localize`:@@voice.ai:AI`,
    text: 'It left our warehouse this morning and should arrive tomorrow.',
  },
  {
    id: 3,
    sender: 'Customer',
    speaker: $localize`:@@lab.transcript.customer:Customer`,
    text: 'أريد تغيير عنوان التوصيل، من فضلك.',
  },
  {
    id: 4,
    sender: 'AiAgent',
    speaker: $localize`:@@voice.ai:AI`,
    text: 'I cannot change an address myself. Let me bring in someone from the team.',
  },
  {
    id: 5,
    sender: 'HumanAgent',
    speaker: $localize`:@@lab.transcript.sara:Sara (team)`,
    text: 'Hello, this is Sara. I have updated the address; the courier has it now.',
  },
  {
    id: 6,
    sender: 'Customer',
    speaker: $localize`:@@lab.transcript.customer:Customer`,
    text: 'Perfect, thank you!',
  },
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
    LocalDatePipe,
    Menu,
    Pagination,
    ToastHost,
    VoiceChip,
    VoiceSplit,
    VoiceTrack,
    AreaChart,
    BarChart,
    ColumnChart,
    Meter,
    Sparkline,
    StackedBar,
    ProviderUsageCard,
    QuotaRow,
    SubscriptionSummary,
  ],
  templateUrl: './design-lab.html',
  styleUrl: './design-lab.scss',
})
export class DesignLab {
  private readonly document = inject(DOCUMENT);
  private readonly toasts = inject(ToastService);
  protected readonly theme = inject(ThemeService);
  protected readonly transparency = inject(TransparencyService);

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
  protected readonly themeLabels = THEME_LABELS;

  /** The preview's reading direction; starts as the page's own. */
  protected readonly direction = signal<'ltr' | 'rtl'>(directionOf(inject(APP_LANGUAGE)));

  /** Two sample days for the sample table, formatted like every real date. */
  protected readonly sampleDays = ['2026-10-16T12:00:00Z', '2026-10-17T12:00:00Z'] as const;

  /** The voice of the "Take over" demonstration chip. */
  protected readonly demoVoice = signal<Voice>('ai');

  protected readonly samplePage = signal(2);

  /** Phase 36.7: sample series for the chart components, written here and labelled as sample. */
  protected readonly chartSeries: readonly ChartSeries[] = [
    {
      key: 'customer',
      label: $localize`:@@lab.charts.customers:Customers (sample)`,
      tone: 'customer',
    },
    { key: 'human', label: $localize`:@@lab.charts.team:Team (sample)`, tone: 'human' },
    { key: 'ai', label: $localize`:@@lab.charts.ai:AI (sample)`, tone: 'ai' },
  ];

  protected readonly chartPoints: readonly ChartPoint[] = SAMPLE_CHART_DAYS.map((values, index) => {
    const day = index + 1;
    const label = $localize`:@@lab.charts.day:Day ${day}:day:`;

    return {
      key: `day-${day}`,
      label,
      longLabel: label,
      partial: index === SAMPLE_CHART_DAYS.length - 1,
      values,
    };
  });

  protected readonly lineSeries: readonly ChartSeries[] = [
    {
      key: 'conversations',
      label: $localize`:@@lab.charts.conversations:Conversations (sample)`,
      tone: 'neutral',
    },
  ];

  protected readonly sampleBars: readonly ChartBar[] = [
    { key: 'a', label: $localize`:@@lab.charts.categoryA:Sample A`, value: 64, note: '58%' },
    { key: 'b', label: $localize`:@@lab.charts.categoryB:Sample B`, value: 31, note: '28%' },
    { key: 'c', label: $localize`:@@lab.charts.categoryC:Sample C`, value: 15, note: '14%' },
  ];

  protected readonly sampleParts: readonly ChartPart[] = [
    { key: 'ai', label: $localize`:@@lab.charts.ai:AI (sample)`, value: 223, tone: 'ai' },
    { key: 'human', label: $localize`:@@lab.charts.team:Team (sample)`, value: 69, tone: 'human' },
  ];

  /**
   * Sample limits, one per state the server reports, plus an unlimited one and
   * a monthly one (Phase 36.10). The local stack runs an unlimited plan, so
   * these exist only here, labelled as sample.
   */
  protected readonly sampleQuotas: readonly PlanEntitlement[] = [
    sampleQuota('members', 6, 10, 'Normal'),
    sampleQuota('aiAgents', 4, 5, 'Warning'),
    sampleQuota('knowledgeDocuments', 46, 50, 'Critical'),
    sampleQuota('webChatEndpoints', 3, 3, 'Exhausted'),
    {
      entitlement: 'whatsAppConnections',
      used: 2,
      limit: null,
      unlimited: true,
      percentUsed: null,
      state: 'Normal',
      remaining: null,
      period: null,
    },
    {
      ...sampleQuota('monthlyProviderOperations', 1820, 2000, 'Critical'),
      period: { from: '2026-10-01T00:00:00Z', to: '2026-11-01T00:00:00Z' },
    },
  ];

  /** Sample subscriptions, one per state worth seeing (Phase 36.10). */
  protected readonly sampleSubscriptions: readonly {
    readonly key: string;
    readonly subscription: WorkspaceSubscription;
    readonly current: string;
  }[] = [
    {
      key: 'past-due',
      current: SAMPLE_PLAN,
      subscription: sampleSubscription('PastDue', { graceEndsAt: daysFromNow(5) }),
    },
    {
      key: 'suspended',
      current: SAMPLE_FALLBACK,
      subscription: sampleSubscription('Suspended'),
    },
    {
      key: 'canceling',
      current: SAMPLE_PLAN,
      subscription: sampleSubscription('Active', { cancelAtPeriodEnd: true }),
    },
    {
      key: 'canceled',
      current: SAMPLE_FALLBACK,
      subscription: sampleSubscription('Canceled', { currentPeriodEnd: null }),
    },
    {
      key: 'trial',
      current: SAMPLE_PLAN,
      subscription: sampleSubscription('Trialing', { trialEndsAt: daysFromNow(9) }),
    },
    {
      key: 'pending',
      current: SAMPLE_FALLBACK,
      subscription: sampleSubscription('PendingActivation', { currentPeriodEnd: null }),
    },
  ];

  protected readonly sampleUsage: AnalyticsAiUsage = {
    calls: 412,
    generationCalls: 318,
    embeddingCalls: 94,
    succeeded: 398,
    failed: 9,
    unresolved: 5,
    generationInputTokens: 1_284_000,
    generationCachedInputTokens: 402_000,
    generationOutputTokens: 186_500,
    generationReasoningTokens: 21_000,
    embeddingInputTokens: 655_000,
    unratedCalls: 12,
    ratedCost: [
      { currency: 'USD', estimatedCost: 3.418275, calls: 360 },
      { currency: 'EUR', estimatedCost: 0.004812, calls: 26 },
    ],
    byPurpose: [
      samplePurpose('ConversationGeneration', 300, 1_210_000, 176_000, 9, [
        { currency: 'USD', estimatedCost: 3.21, calls: 290 },
      ]),
      samplePurpose('ConversationRetrievalEmbedding', 60, 410_000, 0, 0, [
        { currency: 'EUR', estimatedCost: 0.004812, calls: 26 },
      ]),
      samplePurpose('KnowledgeIndexEmbedding', 30, 240_000, 0, 3, []),
      samplePurpose('AgentPreviewGeneration', 18, 74_000, 10_500, 0, [
        { currency: 'USD', estimatedCost: 0.208275, calls: 18 },
      ]),
      samplePurpose('AgentRetrievalTest', 4, 5_000, 0, 0, []),
    ],
  };

  protected readonly sampleModels: readonly AnalyticsModelUsage[] = [
    {
      provider: 'Sample',
      model: 'sample-generation-model',
      operationType: 'TextGeneration',
      calls: 318,
      inputTokens: 1_284_000,
      outputTokens: 186_500,
      estimatedCost: 3.418275,
      currency: 'USD',
      unratedCalls: 0,
    },
    {
      provider: 'Sample',
      model: 'sample-embedding-model',
      operationType: 'Embedding',
      calls: 94,
      inputTokens: 655_000,
      outputTokens: 0,
      estimatedCost: null,
      currency: null,
      unratedCalls: 12,
    },
  ];

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
        this.transparency.reduced();
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
    this.toasts.success($localize`:@@lab.toast.success:Sample: settings saved.`);
  }

  protected toastError(): void {
    this.toasts.error(
      $localize`:@@lab.toast.error:Sample: the change could not be saved. Nothing was lost; try again.`,
    );
  }
}

/** One purpose row for the sample usage card. */
function samplePurpose(
  purpose: string,
  calls: number,
  inputTokens: number,
  outputTokens: number,
  unratedCalls: number,
  ratedCost: AnalyticsAiUsage['ratedCost'],
): AnalyticsAiUsage['byPurpose'][number] {
  return {
    purpose,
    calls,
    succeeded: calls,
    failed: 0,
    unresolved: 0,
    inputTokens,
    outputTokens,
    unratedCalls,
    ratedCost,
  };
}
