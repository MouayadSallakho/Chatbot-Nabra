/** The three sounds of section F. */
export type SoundName = 'ai-failed' | 'needs-person' | 'new-message';

/** A desktop alert: the customer's label and the reason, never transcript text. */
export interface DesktopAlert {
  readonly title: string;
  readonly body: string;
  readonly conversationId: string;
}

/** One thing to tell the member. */
export interface SoundRequest {
  readonly sound: SoundName;
  readonly alert?: DesktopAlert;
}
